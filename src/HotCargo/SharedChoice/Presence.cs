using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Halfling;
using Halfling.Network;
using Halfling.Network.Rpc;
using Cosmoteer.Modes.Career;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.SharedChoice;

/// <summary>
/// Hover markers of shared choices (spec 601, 602). UI only: no MP input, no sim state.
/// It uses an unreliable RPC on a sub-channel of the game's MP connection, like the cursor RPC of creative mode, so hovers
/// skip the input queue and the lockstep. Dialogs call Report each frame from their frame hooks; after them the machine sends its hover
/// when it changed (at most every PresenceSendSeconds) and repeats it as a heartbeat. A marker goes away
/// PresenceExpirySeconds after the last update from that player.
/// </summary>
public static class Presence
{
    public sealed record Hover(MessengerID Player, string Domain, int Serial, int Option, double SeenAt);

    readonly record struct Target(string Domain, int Serial, int Option)
    {
        public static readonly Target None = new("", 0, -1);
    }

    /// <summary>Sub-channel of BaseMPManager._splitter. The game uses 0 to 11. The splitter drops messages of channels that are not open.</summary>
    const byte ChannelId = 200;
    const string RpcId = "HotCargoHover";

    static BaseMPManager? _mp;
    static RpcManager? _rpc;
    static RpcActionInvoker<string, int, int>? _send;
    static readonly Dictionary<MessengerID, Hover> Remote = new();
    static Target _want = Target.None;
    static Target _sent = Target.None;
    static double _sentAt = double.MinValue;

    /// <summary>Test only: replaces the mouse hover of this machine (headless games have no mouse). Null uses the mouse.</summary>
    internal static int? ForcedOption;

    /// <summary>Hover messages that this machine sent, for the autotest check of the send rate.</summary>
    internal static int Sent;

    /// <summary>Call each frame while a shared choice page shows, with the hovered option or -1. The last call in a frame wins.</summary>
    public static void Report(string domain, int serial, int option) => _want = new(domain, serial, ForcedOption ?? option);

    /// <summary>The hovers of the other players on a page, in a stable order.</summary>
    public static IReadOnlyList<Hover> Others(Run run, string domain, int serial)
    {
        if (!run.IsMultiplayer) return [];
        double now = App.Clock.Time.Seconds;
        float expiry = run.T.SharedChoice.PresenceExpirySeconds;
        var local = run.Game.NetManager.LocalPlayerID;
        return Remote.Values
            .Where(h => h.Player != local && h.Domain == domain && h.Serial == serial && h.Option >= 0 && now - h.SeenAt <= expiry)
            .OrderBy(h => h.Player)
            .ToList();
    }

    /// <summary>After the dialogs of the frame: their frame hooks run first (Priority.Last).</summary>
    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.Update)), HarmonyPriority(Priority.Last)]
    static class FlushPatch
    {
        static void Postfix() => Errors.Guard("shared choice presence", Flush);
    }

    static void Flush()
    {
        var want = _want;
        _want = Target.None;
        if (Run.Current is not { IsMultiplayer: true } run || !Attach(run.Game) || _send is null) return;
        double now = App.Clock.Time.Seconds;
        var t = run.T.SharedChoice;
        bool changed = want != _sent;
        if (!changed && want.Option < 0) return;
        double gap = changed ? t.PresenceSendSeconds : t.PresenceExpirySeconds / 3;
        if (now - _sentAt < gap) return;
        _send(want.Domain, want.Serial, want.Option, RpcReceivers.Others);
        Sent++;
        _sent = want;
        _sentAt = now;
    }

    /// <summary>Opens the channel on the current MP connection. A resync or a new game makes a new MP manager.</summary>
    static bool Attach(GameRoot game)
    {
        if (game.NetManager is not BaseMPManager mp) return false;
        if (mp == _mp) return true;
        if (_rpc is not null) _rpc.Channel = null;
        Remote.Clear();
        _sent = Target.None;
        var splitter = mp._splitter;
        var channel = splitter.IsChannelOpen(ChannelId) ? splitter[ChannelId] : splitter.OpenChannel(ChannelId);
        _rpc = new RpcManager(GameApp.Messenger, channel, mp._serializer, mp.IsHost);
        _send = _rpc.RegisterRpc(new RpcAction<string, int, int>(OnHover), RpcId, TransmitMode.Unreliable);
        _mp = mp;
        return true;
    }

    static void OnHover(string domain, int serial, int option, RpcInfo info) =>
        Remote[info.SenderID] = new Hover(info.SenderID, domain ?? "", serial, option, App.Clock.Time.Seconds);
}

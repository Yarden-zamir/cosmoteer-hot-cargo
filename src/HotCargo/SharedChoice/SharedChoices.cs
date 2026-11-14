using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Simulation;
using Halfling.Network;
using Halfling.Serialization;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.SharedChoice;

/// <summary>
/// A kind of shared choice page, for example events (with chain pages) or diplomacy hails. Register it once at boot with
/// SharedChoices.Register, on every machine. Both methods read and change sim state only, so they must be deterministic.
/// </summary>
public interface ISharedChoiceDomain
{
    /// <summary>Unique key. Inputs carry it.</summary>
    string Name { get; }
    /// <summary>The number of options of the open page with this serial, or 0 when that page is not open (any more).</summary>
    int OptionCount(Run run, int serial);
    /// <summary>Applies an option for a player. Returns false when the sim rejects it (for example the player cannot pay) and the page stays.</summary>
    bool Apply(Run run, SimRoot sim, int playerIndex, int serial, int option);
}

public enum SharedChoiceAction { Choose, Vote, Unvote, Force, SetVoteMode }

/// <summary>
/// Sim side of shared choices (spec 600 and later): the domains, the inputs and the vote decision.
/// All state changes run in MP inputs and in the sim tick, so every machine decides the same way.
/// </summary>
public static class SharedChoices
{
    static readonly Dictionary<string, ISharedChoiceDomain> Domains = new();

    /// <summary>The last vote decision on this machine, for autotests. Not saved.</summary>
    internal static VoteDecision? LastDecision;

    public static void Register(ISharedChoiceDomain domain) => Domains.Add(domain.Name, domain);

    public static ISharedChoiceDomain Domain(string name) =>
        Domains.TryGetValue(name, out var d) ? d : throw new InvalidOperationException($"no shared choice domain '{name}'");

    /// <summary>True when shared choices go by vote: co-op with vote mode on. Single player always chooses.</summary>
    public static bool Voting(Run run) => run.IsMultiplayer && run.State.SharedChoice.VoteMode;

    /// <summary>
    /// The voters: connected human players, in a stable order. The game changes this set only in MP inputs (join, leave),
    /// so it is the same on every machine at every tick.
    /// </summary>
    public static IReadOnlyList<MessengerID> Electorate(Run run) => run.Game._humanPlayersByID.Keys.Order().ToList();

    public static string PlayerName(Run run, MessengerID player) =>
        run.Game.NetManager is BaseMPManager mp && mp.TryGetPlayer(player, out var p) ? p.Name : Text("SomePlayer");

    internal static string Text(string key) => Strings.GetText("HotCargoSharedChoice/" + key);
    internal static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoSharedChoice/" + key, args);

    internal static void Execute(Run run, SimRoot sim, int playerIndex, MessengerID player, SharedChoiceInput input)
    {
        var s = run.State.SharedChoice;
        string who = PlayerName(run, player);
        if (input.Action == SharedChoiceAction.SetVoteMode)
        {
            bool on = input.Option != 0;
            if (s.VoteMode == on) return;
            s.VoteMode = on;
            s.Boxes.Clear();
            Log.Info($"shared choice: player {playerIndex} set vote mode {on}");
            if (run.IsMultiplayer) Ux.UxKit.ChatLine(run.Game, Format(on ? "VoteOnFmt" : "VoteOffFmt", who));
            return;
        }
        var domain = Domain(input.Domain);
        int count = domain.OptionCount(run, input.Serial);
        if (count == 0) { Log.Info($"shared choice ignored: {input.Domain} {input.Serial} is not open ({input.Action} by player {playerIndex})"); return; }
        if (input.Action is SharedChoiceAction.Choose or SharedChoiceAction.Vote && (input.Option < 0 || input.Option >= count))
        {
            Log.Info($"shared choice ignored: {input.Domain} {input.Serial} has no option {input.Option}");
            return;
        }
        if (input.Action == SharedChoiceAction.Choose)
        {
            // A click sent just before the switch to vote mode arrives late. In vote mode only the vote decides.
            if (Voting(run)) { Log.Info($"shared choice ignored: a pick by player {playerIndex} in vote mode"); return; }
            // A pick ends the advisory votes of listeners on that page (spec 1137).
            if (domain.Apply(run, sim, playerIndex, input.Serial, input.Option)) s.Boxes.RemoveAll(b => b.Domain == input.Domain && b.Serial == input.Serial);
            return;
        }
        // Outside vote mode a vote is advisory (spec 1136): it shows on every page, and the tick never decides it.
        if (input.Action == SharedChoiceAction.Force && !Voting(run)) { Log.Info($"shared choice ignored: force by player {playerIndex} outside vote mode"); return; }
        var box = s.Box(input.Domain, input.Serial);
        switch (input.Action)
        {
            case SharedChoiceAction.Vote:
                if (box is null) s.Boxes.Add(box = new VoteBox { Domain = input.Domain, Serial = input.Serial });
                box.Votes.RemoveAll(v => v.Player == player);
                box.Votes.Add(new Vote { Player = player, Option = input.Option });
                if (box.FirstVoteSeconds < 0) box.FirstVoteSeconds = run.State.GameSeconds;
                Log.Info($"shared choice: player {playerIndex} votes {input.Option + 1} on {input.Domain} {input.Serial}");
                break;
            case SharedChoiceAction.Unvote:
                if (box is null) return;
                box.Votes.RemoveAll(v => v.Player == player);
                if (box.Votes.Count == 0) box.FirstVoteSeconds = -1;
                Log.Info($"shared choice: player {playerIndex} takes back the vote on {input.Domain} {input.Serial}");
                break;
            case SharedChoiceAction.Force:
                if (!run.Game.NetManager.IsPlayerHost(player)) { Log.Info($"shared choice ignored: force by player {playerIndex}, not the host"); return; }
                if (box is null) { Log.Info("shared choice ignored: force without votes"); return; }
                box.Forced = true;
                Log.Info($"shared choice: the host forces a decision on {input.Domain} {input.Serial}");
                break;
            default:
                throw new InvalidOperationException($"unknown shared choice action {input.Action}");
        }
        // Also decide at once: a dialogue with an enemy can pause the game (vanilla setting), and a paused game has no ticks.
        if (box is not null && Voting(run)) TryDecide(run, sim, box, Electorate(run).ToHashSet());
    }

    /// <summary>Sim tick: drops the boxes of closed pages and decides the votes that are done (all voted, timeout, force, a voter left).</summary>
    internal static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("shared choice tick", () =>
    {
        var s = run.State.SharedChoice;
        if (s.Boxes.Count == 0) return;
        foreach (var closed in s.Boxes.Where(b => Domain(b.Domain).OptionCount(run, b.Serial) == 0).ToList())
        {
            Log.Info($"shared choice: {closed.Domain} {closed.Serial} closed; its {closed.Votes.Count} vote(s) drop");
            s.Boxes.Remove(closed);
        }
        if (!Voting(run)) return;
        var electorate = Electorate(run).ToHashSet();
        foreach (var box in s.Boxes.ToList()) TryDecide(run, sim, box, electorate);
    });

    /// <summary>Decides a box when its vote is done (all voted, timeout, force), and removes it. Runs in the sim tick and in the vote input.</summary>
    static void TryDecide(Run run, SimRoot sim, VoteBox box, IReadOnlySet<MessengerID> electorate)
    {
        bool timedOut = VoteModel.TimedOut(run.State.GameSeconds, box.FirstVoteSeconds, run.T.SharedChoice.VoteTimeoutSeconds);
        var decision = VoteModel.Resolve(box.Votes.Select(v => (v.Player, v.Option)), electorate, timedOut, box.Forced);
        box.Forced = false;
        if (decision is not { } d) return;
        run.State.SharedChoice.Boxes.Remove(box);
        Decide(run, sim, box, d, electorate);
    }

    /// <summary>
    /// Applies the winning option. The voters of that option pay, the lowest player index first: the next one tries when the sim
    /// rejects the first. When nobody can pay, the votes clear and the page stays.
    /// </summary>
    static void Decide(Run run, SimRoot sim, VoteBox box, VoteDecision d, IReadOnlySet<MessengerID> electorate)
    {
        LastDecision = d;
        var domain = Domain(box.Domain);
        var payers = box.Votes.Where(v => v.Option == d.Option && electorate.Contains(v.Player))
            .Select(v => (Id: v.Player, Index: run.Game.TryGetHumanPlayerIndex(v.Player, out int i) ? i : -1))
            .Where(p => p.Index >= 0)
            .OrderBy(p => p.Index).ThenBy(p => p.Id);
        Log.Info($"shared choice: vote on {box.Domain} {box.Serial} decides option {d.Option + 1} ({d.Reason})");
        foreach (var (_, index) in payers)
        {
            if (!domain.Apply(run, sim, index, box.Serial, d.Option)) continue;
            Ux.UxKit.ChatLine(run.Game, Format("DecidedFmt", d.Option + 1, Text("Reason" + d.Reason)));
            return;
        }
        Log.Info($"shared choice: no voter of option {d.Option + 1} can take it; the votes clear");
        Ux.UxKit.ChatLine(run.Game, Text("NobodyCanPay"));
    }
}

/// <summary>A player's action on a shared choice page. It changes sim state, so it goes through the MP input queue (spec 606).</summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoSharedChoice")]
public sealed class SharedChoiceInput : MPInput
{
    [Serialize] public string Domain { get; private set; }
    [Serialize] public int Serial { get; private set; }
    [Serialize] public SharedChoiceAction Action { get; private set; }
    /// <summary>The option for Choose and Vote. For SetVoteMode: 1 = vote, 0 = first pick. Unused otherwise.</summary>
    [Serialize] public int Option { get; private set; }

    public SharedChoiceInput(string domain, int serial, SharedChoiceAction action, int option)
    {
        Domain = domain;
        Serial = serial;
        Action = action;
        Option = option;
    }

    public static SharedChoiceInput SetVoteMode(bool on) => new("", 0, SharedChoiceAction.SetVoteMode, on ? 1 : 0);

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false)
    {
        if (Run.Current is not { } run || run.Game != game || game.Sim is not { } sim) return;
        Errors.Guard("shared choice input", () => SharedChoices.Execute(run, sim, playerIndex, playerID, this));
    }
}

[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.AllowMPInput))]
static class AllowSharedChoicePatch
{
    static void Postfix(MPInput mpInput, ref bool __result)
    {
        if (mpInput is SharedChoiceInput) __result = true;
    }
}

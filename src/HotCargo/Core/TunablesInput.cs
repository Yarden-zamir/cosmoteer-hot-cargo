using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Modes.Career;
using Halfling.Network;
using Halfling.Serialization;
using HarmonyLib;

namespace HotCargo.Core;

/// <summary>
/// The host's tunables for every machine (spec 60, 504). Every machine starts a new MP run with the default tunables
/// (see Run.Start). On its first frame the host sends its file, and every machine applies it at the same tick.
/// Later changes on the host (the settings menu or a change of the host's tunables.rules) go out the same way.
/// Limit: the starting vault and the first system's mission offers exist before the first input arrives, so they use the defaults.
/// Revisit if a designer needs host values for those in co-op (load a single-player save as the host instead).
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoTunables")]
internal sealed class TunablesInput : MPInput
{
    [Serialize] public byte[] Bytes { get; private set; }

    public TunablesInput(byte[] bytes) => Bytes = bytes;

    /// <summary>Frame hook of the host: sends the tunables file once. A missing or invalid file keeps the defaults and shows the error.</summary>
    internal static void SendFromHost(Run run)
    {
        run.HostTunablesPending = false;
        if (run.Game.NetManager.IsHost && TunablesFile.Read() is { } file) Send(run, file);
    }

    /// <summary>Host only: sends valid tunables to every machine. The settings menu shows them until the input executes (Run.HostSent).</summary>
    internal static void Send(Run run, TunablesFile.Loaded file)
    {
        if (!run.Game.NetManager.IsHost) throw new InvalidOperationException("only the host sends tunables");
        run.HostSent = file;
        run.HostSendsInFlight++;
        run.Game.NetManager.EnqueueInput(new TunablesInput(file.Bytes));
        Log.Info($"tunables: sent {file.Hash} to all players");
    }

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false)
    {
        if (Run.Current is not { } run || run.Game != game) return;
        // Only the host counts its sends. The host's own inputs always execute, in order, so the count reaches 0 after the last one.
        if (run.HostSendsInFlight > 0 && --run.HostSendsInFlight == 0) run.HostSent = null;
        // Parse runs on every machine with the same bytes, so a bad file fails the same way everywhere and the values in effect stay.
        Errors.Guard("host tunables", () => run.ApplyTunables(TunablesFile.Parse(Bytes)));
    }
}

[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.AllowMPInput))]
static class AllowTunablesInputPatch
{
    static void Postfix(CareerGameModeManager __instance, MPInput mpInput, MessengerID sourcePlayerID, ref bool __result)
    {
        if (mpInput is TunablesInput) __result = __instance.Game.NetManager is BaseMPManager mp && sourcePlayerID == mp.HostPlayerID;
    }
}

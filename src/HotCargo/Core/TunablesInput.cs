using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Modes.Career;
using Halfling.Network;
using Halfling.Serialization;
using HarmonyLib;

namespace HotCargo.Core;

/// <summary>
/// The host's tunables.rules for a new multiplayer run. Every machine starts a new MP run with the default tunables
/// (see Run.Start). On its first frame the host sends its file, and every machine applies it at the same tick.
/// Limit: the starting vault and the first system's mission offers exist before the input arrives, so they use the defaults.
/// Revisit if a designer needs host values for those in co-op (load a single-player save as the host instead).
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoTunables")]
internal sealed class TunablesInput : MPInput
{
    [Serialize] public byte[] Bytes { get; private set; }

    public TunablesInput(byte[] bytes) => Bytes = bytes;

    /// <summary>Frame hook of the host: sends the tunables file once. A missing file keeps the defaults.</summary>
    internal static void SendFromHost(Run run)
    {
        run.HostTunablesPending = false;
        if (!run.Game.NetManager.IsHost || TunablesFile.ReadBytes() is not { } bytes) return;
        run.Game.NetManager.EnqueueInput(new TunablesInput(bytes));
    }

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false)
    {
        if (Run.Current is not { } run || run.Game != game) return;
        // Parse runs on every machine with the same bytes, so a bad file fails the same way everywhere and the defaults stay.
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

using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Halfling.Network;
using Halfling.Serialization;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Diplomacy;

/// <summary>
/// A hail choice (spec 587). It changes the sim, so it goes through the MP input queue and runs on every machine at the same tick.
/// Amount is the slider value: tech units, credits offered or minutes. A repeated input for a once-per-ship offer does nothing.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoDiplomacy")]
internal sealed class DiplomacyInput : MPInput
{
    [Serialize] public Ship? Caller { get; private set; }
    [Serialize] public Ship? Callee { get; private set; }
    [Serialize] public Offer Offer { get; private set; }
    [Serialize] public int Amount { get; private set; }

    public DiplomacyInput(Ship caller, Ship callee, Offer offer, int amount)
    {
        Caller = caller ?? throw new ArgumentNullException(nameof(caller));
        Callee = callee ?? throw new ArgumentNullException(nameof(callee));
        Offer = offer;
        Amount = amount;
    }

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false)
    {
        if (Run.Current is not { } run || run.Game != game || game.Sim is not { } sim || Caller is null || Callee is null) return;
        Errors.Guard("diplomacy choice", () => Deals.Choose(run, sim, playerIndex, Caller, Callee, Offer, Amount));
    }
}

[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.AllowMPInput))]
static class AllowDiplomacyInputPatch
{
    static void Postfix(MPInput mpInput, ref bool __result)
    {
        if (mpInput is DiplomacyInput) __result = true;
    }
}

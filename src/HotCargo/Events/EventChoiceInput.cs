using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Modes.Career;
using Halfling.Network;
using Halfling.Serialization;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Events;

/// <summary>
/// A player picks a choice on the pending event page. The choice changes sim state, so it goes through the MP input queue
/// and runs on every client in the same order. Serial makes a second input for the same page (another player clicked too) do nothing.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoEventChoice")]
internal sealed class EventChoiceInput : MPInput
{
    [Serialize] public int Serial { get; private set; }
    [Serialize] public int Choice { get; private set; }

    public EventChoiceInput(int serial, int choice)
    {
        Serial = serial;
        Choice = choice;
    }

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false)
    {
        if (Run.Current is not { } run || run.Game != game || game.Sim is not { } sim) return;
        EventRunner.Choose(run, sim, playerIndex, Serial, Choice);
    }
}

[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.AllowMPInput))]
static class AllowEventChoicePatch
{
    static void Postfix(MPInput mpInput, ref bool __result)
    {
        if (mpInput is EventChoiceInput) __result = true;
    }
}

/// <summary>Frame hook for the event dialog. UI only: it reads sim state and sends inputs, but never changes the sim.</summary>
[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.Update))]
static class EventFramePatch
{
    static void Postfix(CareerGameModeManager __instance)
    {
        if (Run.Current is not { } run || run.Mode != __instance) return;
        try { EventDialog.Update(run); }
        catch (Exception ex)
        {
            Log.Error("event dialog failed", ex);
            Ui.Overlay.FatalError = $"event dialog: {ex.Message}";
        }
    }
}

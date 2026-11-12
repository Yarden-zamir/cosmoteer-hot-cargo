using Cosmoteer.Modes.Career;
using Cosmoteer.Simulation;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.SharedChoice;

namespace HotCargo.Events;

/// <summary>
/// Event pages (chain pages too) as a shared choice domain. A choice goes through SharedChoiceInput and runs on every client in
/// the same order. The serial makes a second input for the same page (another player clicked too) do nothing.
/// </summary>
internal sealed class EventsChoiceDomain : ISharedChoiceDomain
{
    public const string Name = "events";
    public static readonly EventsChoiceDomain Instance = new();

    string ISharedChoiceDomain.Name => Name;

    public int OptionCount(Run run, int serial) =>
        run.State.Result == RunResult.Running && run.State.Events.Pending is { } p && p.Serial == serial ? EventLibrary.Get(p.EventId).Choices.Length : 0;

    /// <summary>Applied when the page with this serial is no longer pending (a follow-up page has a new serial). The sim rejects a choice that the player cannot take.</summary>
    public bool Apply(Run run, SimRoot sim, int playerIndex, int serial, int option)
    {
        EventRunner.Choose(run, sim, playerIndex, serial, option);
        return run.State.Events.Pending?.Serial != serial;
    }

    /// <summary>The input that the dialog and the test commands send for a pick.</summary>
    public static SharedChoiceInput Pick(int serial, int choice) => new(Name, serial, SharedChoiceAction.Choose, choice);
}

/// <summary>Frame hook for the event dialog. UI only: it reads sim state and sends inputs, but never changes the sim.</summary>
[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.Update))]
static class EventFramePatch
{
    static void Postfix(CareerGameModeManager __instance)
    {
        if (Run.Current is not { } run || run.Mode != __instance) return;
        // Errors.Guard logs once, so a broken dialog does not write the log every frame.
        Errors.Guard("event dialog", () => EventDialog.Update(run));
    }
}

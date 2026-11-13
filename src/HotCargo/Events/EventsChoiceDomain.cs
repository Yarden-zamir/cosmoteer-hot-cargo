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

/// <summary>
/// Page options as a multiselect domain (spec 810): the options of the pending page while it is open, else closed. Each option's
/// value is its slider steps. Submit choices read the draft (EventRunner.Picks).
/// </summary>
internal sealed class EventsMultiDomain : MultiChoice.IMultiChoiceDomain
{
    public const string Name = "events";
    public static readonly EventsMultiDomain Instance = new();

    string MultiChoice.IMultiChoiceDomain.Name => Name;

    public IReadOnlyList<MultiChoice.MultiOption>? Options(Run run, int serial)
    {
        if (run.State.Result != RunResult.Running || run.State.Events.Pending is not { } p || p.Serial != serial) return null;
        return [.. EventLibrary.Get(p.EventId).Options.Select((o, i) => new MultiChoice.MultiOption(i, o.MinSteps, o.MaxSteps, 1))];
    }
}

/// <summary>Frame hook for the event dialog. UI only: it reads sim state and sends inputs, but never changes the sim.</summary>
[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.Update))]
static class EventFramePatch
{
    static double _next;

    static void Postfix(CareerGameModeManager __instance)
    {
        // Spec 872: the dialog builds its page from sim state, so it updates every HudRefreshSeconds, not in every frame.
        // Clicks do not wait: the buttons send their input at once.
        if (Run.Current is not { } run || run.Mode != __instance || !Ux.UxKit.HudDue(ref _next)) return;
        // Errors.Guard logs once, so a broken dialog does not write the log every frame.
        Errors.Guard("event dialog", () => EventDialog.Update(run));
    }
}

using Cosmoteer.Localization;
using Cosmoteer.Simulation;
using Halfling.Serialization;
using HotCargo.Core;

namespace HotCargo.Tech;

/// <summary>Tech area state of the run. Saved inside RunState.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class TechState
{
    /// <summary>Spec 443: true while the loss timer runs. RunState.SecondsWithoutTech holds its elapsed game seconds.</summary>
    [Serialize(Optional = true)] public bool LossTimerRunning;
    /// <summary>Player-facing reason that the heat panel shows under the countdown.</summary>
    [Serialize(Optional = true)] public string LossTimerReason = "";
}

/// <summary>
/// Spec 9, 12, 443: when the fleet tech count reaches 0, a timer of LossGraceSeconds starts. Tech back in time stops it; else the run is lost.
/// Sim state only: call Start from sim logic (sim tick, Run hooks, MP inputs), never from a UI frame.
/// </summary>
public static class LossTimer
{
    public static bool IsRunning(Run run) => run.State.Tech.LossTimerRunning;

    /// <summary>Game seconds until the loss, or null when the timer does not run.</summary>
    public static double? SecondsLeft(Run run) =>
        IsRunning(run) ? Math.Max(0, run.T.Tech.LossGraceSeconds - run.State.SecondsWithoutTech) : null;

    /// <summary>
    /// Starts the timer with a player-facing reason, or changes the reason of a running timer (the countdown goes on).
    /// While the fleet still holds tech, the next sim tick stops the timer again. So call it when the tech is gone,
    /// for example right after a hand-over that removes all tech.
    /// </summary>
    public static void Start(Run run, string reason)
    {
        var t = run.State.Tech;
        bool wasRunning = t.LossTimerRunning;
        t.LossTimerRunning = true;
        t.LossTimerReason = reason;
        if (wasRunning) return;
        run.State.SecondsWithoutTech = 0;
        Log.Info($"loss timer started ({run.T.Tech.LossGraceSeconds:0} s): {reason}");
        string text = string.Format(Strings.GetText("HotCargoTech/LossTimerStarted"), FormatTime(run.T.Tech.LossGraceSeconds));
        // A co-op tick can run on a worker thread, so the toast waits for the next frame.
        Run.OnNextFrame(() => run.Game.Gui.ShowAreaName(text));
    }

    static void Stop(Run run)
    {
        var t = run.State.Tech;
        Log.Info($"loss timer stopped after {run.State.SecondsWithoutTech:0.0} s: the fleet holds tech again");
        t.LossTimerRunning = false;
        t.LossTimerReason = "";
        run.State.SecondsWithoutTech = 0;
    }

    internal static void OnTick(Run run, SimRoot sim, double dt)
    {
        var s = run.State;
        if (s.FleetTech > 0)
        {
            if (s.Tech.LossTimerRunning) Stop(run);
            return;
        }
        // An uncertain count (a jump, friendly ships in another system) pauses the timer. It neither starts nor resets it.
        if (!CountIsCertain(run, sim)) return;
        if (!s.Tech.LossTimerRunning) Start(run, Strings.GetText("HotCargoTech/LossTimerNoTech"));
        s.SecondsWithoutTech += dt;
        if (s.SecondsWithoutTech >= run.T.Tech.LossGraceSeconds) run.Lose(Strings.GetText("HotCargoTech/LossNoTech"));
    }

    /// <summary>The heat panel line while the timer runs, else null.</summary>
    public static string? PanelLine(Run run) =>
        SecondsLeft(run) is { } left && run.State.Result == RunResult.Running
            ? string.Format(Strings.GetText("HotCargoTech/LossTimerFmt"), FormatTime(left), run.State.Tech.LossTimerReason)
            : null;

    static string FormatTime(double seconds)
    {
        int total = (int)Math.Ceiling(seconds);
        return $"{total / 60}:{total % 60:00}";
    }

    /// <summary>
    /// The conditions of the vanilla game-over check in CareerGameModeManager.Update: no jump in progress, no friendly ships in another system.
    /// It checks ships in jump limbo instead of GameRoot.IsDoingNodeJump: that flag follows local animations and the local FTL autosave
    /// setting, so it changes at different ticks on each multiplayer machine. Jump limbo changes only at synchronized jump signals.
    /// </summary>
    static bool CountIsCertain(Run run, SimRoot sim) =>
        !sim.Ships.IsDoingLocalJump && run.Game.ShipsInJumpLimbo.Count == 0 &&
        run.Mode._nodeData.All(kv => kv.Key == run.Game.CurrentMapNode || kv.Value.NumFriendlyShips == 0);
}

using HotCargo.Core;

namespace HotCargo.Pursuit;

/// <summary>
/// Spec 905 to 909: the pacer of big events. An area asks <see cref="MayStart"/> before it starts a big event and calls
/// <see cref="Started"/> when it does. Both are sim logic: call them from the sim tick, a Run hook or an MP input, never from the UI.
/// Kinds come from Pursuit.Events (defaults in <see cref="PacedKinds"/>). An unknown kind throws, so a missing tunables entry shows at once.
/// </summary>
public static class Pacer
{
    static PacedEvent Rules(Run run, string kind) =>
        run.T.Pursuit.Events.FirstOrDefault(e => e.Kind == kind)
        ?? throw new InvalidOperationException($"Pursuit.Events has no entry for big event kind '{kind}'");

    static List<PaceStart> History(Run run) => run.State.Pursuit.Started.Select(s => new PaceStart(s.Kind, s.At)).ToList();

    /// <summary>The pacer verdict for a big event of this kind now.</summary>
    public static PaceVerdict Verdict(Run run, string kind)
    {
        var r = Rules(run, kind);
        return PursuitModel.MayStart(History(run), kind, run.State.GameSeconds, r.MaxPerRun, r.MinGapSeconds, run.T.Pursuit.MinEventGapSeconds);
    }

    /// <summary>True when a big event of this kind may start now: under its run limit, past its gap and past the gap after any big event.</summary>
    public static bool MayStart(Run run, string kind) => run.PressureActive && Verdict(run, kind) == PaceVerdict.Ok;

    /// <summary>Records the start of a big event. Call it once, when the event really starts.</summary>
    public static void Started(Run run, string kind)
    {
        _ = Rules(run, kind);
        run.State.Pursuit.Started.Add(new PacedStartState { Kind = kind, At = run.State.GameSeconds, Sector = run.State.SectorIndex });
        Log.Info($"pursuit: big event {kind} started at {run.State.GameSeconds:0}s in sector {run.State.SectorIndex}");
    }

    /// <summary>Starts of this kind in the run.</summary>
    public static int Count(Run run, string kind) => run.State.Pursuit.Started.Count(s => s.Kind == kind);
}

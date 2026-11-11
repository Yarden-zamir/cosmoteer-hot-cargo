using Cosmoteer.Ships;
using HotCargo.Core;
using HotCargo.Ui;

namespace HotCargo.Tactics;

public enum Tactic { Kite, Pincer, Disable, FallBack, Bait }

/// <summary>
/// Decides if a tactic is on now (spec 43). With no active run, every tactic is off, so the modules fall through to the vanilla ones.
/// Reads the live tunables and run state. Both are the same on all multiplayer clients, so the result is deterministic.
/// </summary>
public static class TacticGate
{
    static readonly HashSet<string> ReportedStages = [];

    public static TacticsTunables? Active(Tactic tactic)
    {
        if (Run.Current is not { } run) return null;
        var t = run.T.Tactics;
        (bool enabled, string minStage) = tactic switch
        {
            Tactic.Kite => (t.KiteEnabled, t.KiteMinStage),
            Tactic.Pincer => (t.PincerEnabled, t.PincerMinStage),
            Tactic.Disable => (t.DisableEnabled, t.DisableMinStage),
            Tactic.FallBack => (t.FallBackEnabled, t.FallBackMinStage),
            Tactic.Bait => (t.BaitEnabled, t.BaitMinStage),
            _ => throw new ArgumentOutOfRangeException(nameof(tactic), tactic, null),
        };
        return enabled && StageReached(run, minStage) ? t : null;
    }

    static bool StageReached(Run run, string minStage)
    {
        if (minStage.Length == 0) return true;
        int index = Array.FindIndex(run.T.Heat.Stages, s => s.Name == minStage);
        if (index >= 0) return run.State.StagesStarted > index;
        // A wrong name in tunables.rules turns the tactic off and shows the error. It must not crash the sim tick.
        lock (ReportedStages)
        {
            if (ReportedStages.Add(minStage))
            {
                Log.Error($"tactics: min stage '{minStage}' is not a Heat.Stages name; the tactic stays off");
                Overlay.FatalError = $"tactics: unknown min stage '{minStage}'";
            }
        }
        return false;
    }
}

/// <summary>Counts tactic decisions for autotest fields, and logs them when Tactics.DebugLog is on. Not sim state.</summary>
public static class TacticsLog
{
    static readonly Dictionary<string, int> Counts = [];

    public static int Count(string name)
    {
        lock (Counts) return Counts.GetValueOrDefault(name);
    }

    public const string Hunt = "hunt";

    /// <summary>Event names: "hunt" and each Tactic name in lower case.</summary>
    public static IEnumerable<string> Names => Enum.GetNames<Tactic>().Select(n => n.ToLowerInvariant()).Prepend(Hunt);

    public static void Event(Ship ship, Tactic tactic, string message) => Event(ship, tactic.ToString().ToLowerInvariant(), message);

    public static void Event(Ship ship, string name, string message)
    {
        lock (Counts) Counts[name] = Counts.GetValueOrDefault(name) + 1;
        if (Run.Current?.T.Tactics.DebugLog == true) Log.Info($"tactic {name} [{ship.Metadata.PlainShipName} {ship.UniqueID}] {message}");
    }
}

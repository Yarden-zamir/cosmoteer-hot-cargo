using Cosmoteer.Ships;
using HotCargo.Core;

namespace HotCargo.Tactics;

public enum Tactic { Kite, Pincer, Disable, FallBack, Bait }

/// <summary>
/// Decides if a tactic is on now (spec 43). With no active run, every tactic is off, so the modules fall through to the vanilla ones.
/// Reads the live tunables and run state. Both are the same on all multiplayer clients, so the result is deterministic.
/// </summary>
public static class TacticGate
{
    public static TacticsTunables? Active(Tactic tactic)
    {
        if (Run.Current is not { } run) return null;
        var t = run.T.Tactics;
        var (enabled, minStage) = Settings(t, tactic);
        return enabled && StageReached(run, minStage) ? t : null;
    }

    public static (bool Enabled, string MinStage) Settings(TacticsTunables t, Tactic tactic) => tactic switch
    {
        Tactic.Kite => (t.KiteEnabled, t.KiteMinStage),
        Tactic.Pincer => (t.PincerEnabled, t.PincerMinStage),
        Tactic.Disable => (t.DisableEnabled, t.DisableMinStage),
        Tactic.FallBack => (t.FallBackEnabled, t.FallBackMinStage),
        Tactic.Bait => (t.BaitEnabled, t.BaitMinStage),
        _ => throw new ArgumentOutOfRangeException(nameof(tactic), tactic, null),
    };

    /// <summary>TunablesFile.Validate rejects unknown stage names, so a name that is not found is a bug.</summary>
    static bool StageReached(Run run, string minStage)
    {
        if (minStage.Length == 0) return true;
        int index = Array.FindIndex(run.T.Heat.Stages, s => s.Name == minStage);
        if (index < 0) throw new InvalidOperationException($"tactics: min stage '{minStage}' is not a Heat.Stages name");
        return run.State.StagesStarted > index;
    }
}

/// <summary>Counts tactic decisions for autotest fields, and logs them when Debug.TacticsLog is on. Not sim state.</summary>
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
        if (Run.Current?.T.Debug.TacticsLog == true) Log.Info($"tactic {name} [{ship.Metadata.PlainShipName} {ship.UniqueID}] {message}");
    }
}

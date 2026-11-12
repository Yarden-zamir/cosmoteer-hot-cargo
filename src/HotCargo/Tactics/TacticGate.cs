using Cosmoteer.Ships;
using HotCargo.Ai;
using HotCargo.Core;

namespace HotCargo.Tactics;

/// <summary>The traits that are tactics. Each value is its Trait value, so (Trait)tactic converts.</summary>
public enum Tactic
{
    Kite = Trait.Kite, Pincer = Trait.Pincer, Disable = Trait.Disable, FallBack = Trait.FallBack, Bait = Trait.Bait,
    Team = Trait.Team, Cover = Trait.Cover, Brawl = Trait.Brawl,
}

/// <summary>
/// Decides if a tactic is on for a ship now (spec 43, 360). With no active run, every tactic is off, so the modules fall through
/// to the vanilla ones. Reads the live tunables, the run state and sim state. All are the same on every multiplayer client.
/// </summary>
public static class TacticGate
{
    public static TacticsTunables? Active(Tactic tactic, Ship ship)
    {
        if (Run.Current is not { } run) return null;
        var t = run.T.Tactics;
        var (enabled, minStage) = Settings(t, tactic);
        if (!enabled) return null;
        bool hunter = AiGate.IsHunterAi(ship);
        if (hunter && !StageReached(run, minStage)) return null;
        if (AiGate.Surrendered(ship) || !AiGate.HostileToPlayers(ship)) return null;
        if (!t.PersonalitiesEnabled) return hunter ? t : null;
        return PersonalityStrategy.Traits(ship, t).Has((Trait)tactic) ? t : null;
    }

    public static (bool Enabled, string MinStage) Settings(TacticsTunables t, Tactic tactic) => tactic switch
    {
        Tactic.Kite => (t.KiteEnabled, t.KiteMinStage),
        Tactic.Pincer => (t.PincerEnabled, t.PincerMinStage),
        Tactic.Disable => (t.DisableEnabled, t.DisableMinStage),
        Tactic.FallBack => (t.FallBackEnabled, t.FallBackMinStage),
        Tactic.Bait => (t.BaitEnabled, t.BaitMinStage),
        Tactic.Team => (t.TeamEnabled, t.TeamMinStage),
        Tactic.Cover => (t.CoverEnabled, t.CoverMinStage),
        Tactic.Brawl => (t.BrawlEnabled, t.BrawlMinStage),
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

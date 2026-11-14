using Cosmoteer.Data;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Random;
using HotCargo.Core;

namespace HotCargo.Events;

/// <summary>
/// Abandoned ships (spec 764, 765). Vanilla 0.30.5 spawns abandoned civilian ships (junk allegiance, label "abandoned").
/// They carry loot (Hulks, spec 1124). Each run also has one relic ship (spawns.rules "Relic") with 1 tech unit:
/// the vault senses it with a special message, and the Relic event opens when a ship comes close.
/// </summary>
public static class Abandoned
{
    public const string Label = "abandoned";
    public static readonly ID<SimObjectSpawner> RelicTag = new("hc_relic_ship");
    public const string RelicSpawn = "Relic";
    public const string RelicTakenFlag = "relic_taken";

    static EventsState State(Run run) => run.State.Events;

    /// <summary>Picks the relic sector once per run, from the sim random source.</summary>
    internal static void OnNewRun(Run run)
    {
        var t = run.T.Events;
        // Limit: the relic sector rolls at the run start, before the start job can move the Buyer (spec 1201). A job with fewer
        // sectors can put the relic at or past the Buyer, so the run has no relic. Revisit if relics matter for short jobs.
        int last = run.T.Journey.RunSectorCount - 1;
        State(run).RelicSector = t.RelicEnabled && last >= t.RelicMinSector ? run.Game.Sim.Rand.Int32(Math.Max(1, t.RelicMinSector), last) : -1;
        Log.Info($"relic ship sector: {State(run).RelicSector}");
    }

    internal static void OnSystemEntered(Run run, bool firstVisit)
    {
        var s = State(run);
        if (!firstVisit || s.RelicSpawned || s.RelicSector < 0 || run.State.SectorIndex != s.RelicSector) return;
        SpawnRelic(run, run.Game.Sim);
    }

    /// <summary>Spawns the relic ship 5,000 to 8,000 from the fleet. Public for the autotest.</summary>
    public static void SpawnRelic(Run run, SimRoot sim)
    {
        var at = SimQueries.FleetCenter(sim) ?? throw new InvalidOperationException("no human ships to place the relic near");
        EventLibrary.Spawn(RelicSpawn).Generate(sim, sim.Rand, loadInstantly: true, rootLoc: at, addExistingSpawnTags: true);
        State(run).RelicSpawned = true;
        Log.Info($"relic ship spawned in sector {run.State.SectorIndex}");
    }

    /// <summary>
    /// The relic's location and hull radius, live or in stasis (radius 0 there). It has junk allegiance, so SimQueries.TaggedShips,
    /// which leaves out wrecks, cannot find it once it is live. It can be in stasis at message range.
    /// </summary>
    public static List<(Halfling.Geometry.Vector2 Location, float Radius)> RelicLocations(SimRoot sim)
    {
        var result = new List<(Halfling.Geometry.Vector2, float)>();
        foreach (var ship in sim.Ships.Where(sh => sh.HasTag(RelicTag)).OrderBy(sh => sh.UniqueID)) result.Add((ship.DetWorldCenter, ship.WorldBoundingRadius));
        foreach (var st in sim.Stasis.Where(st => st.HasTag(RelicTag)).OrderBy(st => st.ReservedObjectID)) result.Add((st.Location, 0f));
        return result;
    }

    /// <summary>Proximity checks for the relic. Runs in the sim tick, twice a game second.</summary>
    internal static void Check(Run run, SimRoot sim)
    {
        var s = State(run);
        var ships = sim.Ships.HumanPlayerSignificantShips.ToList();
        if (ships.Count == 0) return;
        var t = run.T.Events;

        foreach (var (location, radius) in RelicLocations(sim))
        {
            if (s.Flags.Contains(RelicTakenFlag)) break;
            float d = ships.Min(h => h.DetWorldCenter.DistanceTo(location) - h.WorldBoundingRadius) - radius;
            if (!s.RelicMessaged && d <= t.RelicMessageDistance)
            {
                s.RelicMessaged = true;
                EventText.ShowRelicMessage(run.Game);
            }
            // The event opens on the way in. While another page waits, it waits too. Leaving and coming back opens it again.
            if (d > t.RelicBoardDistance) s.RelicInside = false;
            else if (!s.RelicInside && s.Pending is null)
            {
                s.RelicInside = true;
                EventRunner.TriggerUnlimited(run, sim, EventTrigger.Relic, 1f);
            }
        }
    }
}

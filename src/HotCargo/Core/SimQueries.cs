using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling.Geometry;

namespace HotCargo.Core;

/// <summary>Shared lookups in a simulation. All results come in a stable order, so sim logic stays deterministic.</summary>
public static class SimQueries
{
    /// <summary>Career sims always have a game. A missing one is a bug, so it throws.</summary>
    public static GameRoot RequireGame(this SimRoot sim) => sim.Game ?? throw new InvalidOperationException("sim has no game");

    public static ObjectID NodeId(this SimRoot sim) => (sim.MapNode ?? throw new InvalidOperationException("sim has no map node")).UniqueID;

    public static bool HasTag(this Ship ship, ID<SimObjectSpawner> tag) => ship.Tags?.Contains(tag) == true;

    /// <summary>True when a stored ship reference is still a live ship in the same system as <paramref name="self"/>. A ship in jump limbo has no sim.</summary>
    public static bool IsLiveWith(this Ship other, Ship self) => !other.IsDisposed && !other.IsJunk && other.Sim is not null && other.Sim == self.Sim;

    /// <summary>Spawn tags of a ship in stasis. Ships spawned into stasis and ships that entered stasis use different spawner types.</summary>
    public static HashSet<ID<SimObjectSpawner>>? StasisTags(StasisSpawner s) => s switch
    {
        StasisShipSpawner ss => ss.Tags,
        SimStasisManager.SerializedStasisShip ser => ser.Tags,
        _ => null,
    };

    public static bool HasTag(this StasisSpawner s, ID<SimObjectSpawner> tag) => StasisTags(s)?.Contains(tag) == true;

    /// <summary>A ship with a spawn tag, live (Live is set) or in stasis.</summary>
    public readonly record struct TaggedShip(ObjectID Id, int PlayerIndex, Vector2 Location, Ship? Live);

    /// <summary>Live ships with the tag in sim order (wrecks left out), then ships in stasis by ID.</summary>
    public static List<TaggedShip> TaggedShips(SimRoot sim, ID<SimObjectSpawner> tag)
    {
        var result = new List<TaggedShip>();
        for (int i = 0; i < sim.Ships.Count; i++)
        {
            var ship = sim.Ships[i];
            if (ship.HasTag(tag) && !ship.IsJunk) result.Add(new(ship.UniqueID, ship.Metadata.PlayerIndex, ship.DetWorldCenter, ship));
        }
        foreach (StasisSpawner s in sim.Stasis.OrderBy(s => s.ReservedObjectID))
            if (s.HasTag(tag)) result.Add(new(s.ReservedObjectID, s.ObjectAllegiance, s.Location, null));
        return result;
    }

    /// <summary>Mean position of the significant human ships, or null when there are none.</summary>
    public static Vector2? FleetCenter(SimRoot sim)
    {
        Vector2 sum = Vector2.Zero;
        int count = 0;
        foreach (var h in sim.Ships.HumanPlayerSignificantShips) { sum += h.DetWorldCenter; count++; }
        return count == 0 ? null : sum / count;
    }

    /// <summary>The human ship of the lowest player index with the lowest ID. Null when the players have no ship in this system.</summary>
    public static Ship? FirstHumanShip(SimRoot sim) =>
        sim.Ships.HumanPlayerShips.OrderBy(s => s.Metadata.PlayerIndex).ThenBy(s => s.UniqueID).FirstOrDefault();

    /// <summary>The significant human ship nearest to a point, or null when the players have none in this system.</summary>
    public static Ship? NearestHumanShip(SimRoot sim, Vector2 at) =>
        sim.Ships.HumanPlayerSignificantShips.MinBy(s => s.DetWorldCenter.DistanceSquaredTo(at));
}

using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Repairs;
using Halfling.Geometry;
using HotCargo.Autonomy;
using HotCargo.Core;
using HotCargo.Diplomacy;
using HotCargo.Hunters;

namespace HotCargo.Tactics;

/// <summary>
/// Spec 1305-1308. What a damaged AI ship lacks for its repairs, and the ships that can give it: donors and friendly stations.
/// Reads and changes sim state only. Called from the AI tick, so it runs the same on every client.
/// </summary>
internal static class Resupply
{
    /// <summary>Every repair of the ship as one vanilla update: damaged parts, destroyed parts and destroyed doors.</summary>
    static IEnumerable<object> RepairObjects(Ship ship)
    {
        foreach (var p in ship.DestroyedParts) yield return p;
        foreach (var d in ship.DestroyedDoors) yield return d;
        foreach (var p in ship.Parts.RepairableParts.DamagedRepairableParts) yield return p;
    }

    /// <summary>Adds one repair to the update. False when the game refuses it (a rebuild that something blocks).</summary>
    static bool AddRepair(ShipUpdateInfo info, object repair) => repair switch
    {
        DestroyedPart d => info.TryRebuildPart(d, rebuildDoors: false, hireCrew: false, out _),
        DestroyedDoor d => info.TryRebuildDoor(d, out _),
        Part p => Repair(info, p),
        _ => throw new InvalidOperationException($"unexpected repair object {repair.GetType()}"),
    };

    static bool Repair(ShipUpdateInfo info, Part part)
    {
        info.RepairPart(part);
        return true;
    }

    /// <summary>
    /// Spec 1305: the resources that the ship lacks for all its repairs, by resource ID. ownOnly: count only the ship's own storage,
    /// not the loose nuggets and own-player ships nearby that the vanilla repair counts too. Crews reach those only through airlocks.
    /// </summary>
    public static Dictionary<string, int> Lacking(Ship ship, bool ownOnly = false)
    {
        var cost = new Dictionary<string, int>();
        using (var info = ShipUpdateInfo.Alloc(ship))
        {
            foreach (var repair in RepairObjects(ship).ToList()) AddRepair(info, repair);
            // returnFinalCost: false. The final cost turns resources that a trading station in range sells into credits.
            using var total = info.GetTotalPhysicalCost(MPValueType.Confirmed, returnFinalCost: false, isBuild: false, refundStoredResources: false, playerIndex: ship.Metadata.PlayerIndex);
            foreach (var (type, n) in total.Resources)
                if (n > 0) cost[type.ToString()] = n;
        }
        return RepairModel.Lacking(cost, type => ownOnly ? Stock(ship, new ID<ResourceRules>(type))
            : ship.Resources.GetUsableConstructionResourceTotal(new ID<ResourceRules>(type), includeAnticipated: true));
    }

    public static bool ShortOfCrew(Ship ship, TacticsTunables t) =>
        RepairModel.ShortOfCrew(ship.Crew.Assigned.Count, ship.Crew.TotalCrewCapacity, t.FallBackCrewFraction);

    /// <summary>Units of a resource in the ship's storage. Carried units and construction stock do not count.</summary>
    static int Stock(Ship ship, ID<ResourceRules> type) => ship.Resources.GetResourceTotal(type, includeStored: true, includeCarried: false);

    /// <summary>Free room in the ship's storage for a resource.</summary>
    static int Room(Ship ship, ID<ResourceRules> type) => Math.Max(0, ship.Resources.GetRemainingCapacity(type, includeUntypedStorages: true));

    /// <summary>
    /// Spec 1307: the nearest donor in range that holds a resource that the ship lacks and has room for. Ties go to the lower ID.
    /// A ship without storage room gets no donor: its crews could not take the units.
    /// </summary>
    public static Ship? FindDonor(Ship ship, IReadOnlyDictionary<string, int> lacking, TacticsTunables t)
    {
        var types = lacking.Keys.Select(k => new ID<ResourceRules>(k)).Where(type => Room(ship, type) > 0).ToList();
        if (types.Count == 0) return null;
        Ship? best = null;
        float bestDistance = float.MaxValue;
        foreach (var other in HunterOrders.Sim(ship).Ships)
        {
            if (other == ship || other.AI is null || other.IsHumanPlayerShip || other.IsJunk || !other.IsSignificant) continue;
            if (ship.IsEnemiesWith(other) || Situation.IsStation(other)) continue;
            float d = HunterOrders.EdgeDistance(ship, other);
            if (d > t.FallBackDonorRange || d > bestDistance || (d == bestDistance && best is not null && other.UniqueID.CompareTo(best.UniqueID) > 0)) continue;
            if (!types.Any(type => Stock(other, type) > 0)) continue;
            (best, bestDistance) = (other, d);
        }
        return best;
    }

    /// <summary>True when the two ships are within the vanilla resource transfer distance (crew rules, 400 edge to edge).</summary>
    public static bool InTransferRange(Ship a, Ship b) =>
        a.DetWorldBoundingCircle.DistanceTo(b.DetWorldBoundingCircle) <= GameApp.Rules.Crew.LoseHomeShipWarningDistance;

    /// <summary>
    /// Spec 1307: moves up to the donor share of each resource that the ship's own storage lacks, into the ship's storage.
    /// Own storage only: the vanilla repair counts an own-player donor within 400 as usable, but crews reach another ship only
    /// through airlocks. Units that find no room go back to the donor. Returns the units moved.
    /// </summary>
    public static int TakeFrom(Ship ship, Ship donor, TacticsTunables t)
    {
        int moved = 0;
        foreach (var (id, need) in Lacking(ship, ownOnly: true).OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var type = new ID<ResourceRules>(id);
            int give = Math.Min(Room(ship, type), RepairModel.DonorGives(need, Stock(donor, type), t.FallBackDonorShare));
            if (give <= 0) continue;
            int removed = donor.Resources.RemoveResources(type, give);
            int placed = ship.Resources.AddResources(type, removed, excludeConstructionSinks: true);
            if (placed < removed) donor.Resources.AddResources(type, removed - placed, excludeConstructionSinks: true);
            moved += placed;
        }
        return moved;
    }

    /// <summary>Spec 1308: the nearest friendly station (requirement 1069's list), live or in stasis. Ties go to the lower ID.</summary>
    public static StationRef? FindStation(Ship ship) =>
        AutonomyWorld.Stations(ship).Where(s => s.Live is null || Situation.IsStation(s.Live))
            .OrderBy(s => Math.Max(0f, AutonomyWorld.EdgeDistance(ship, s.Center) - s.Radius)).ThenBy(s => s.Id).Cast<StationRef?>().FirstOrDefault();

    /// <summary>The station with this ID, while it is still friendly to the ship.</summary>
    public static StationRef? Station(Ship ship, ObjectID id) =>
        AutonomyWorld.Stations(ship).Where(s => s.Id == id && (s.Live is null || Situation.IsStation(s.Live))).Cast<StationRef?>().FirstOrDefault();

    /// <summary>True when the live station allows trade with the ship (vanilla AllowResourceTrade: friendly and within 400).</summary>
    public static bool AtStation(Ship ship, StationRef station) =>
        station.Live is { } live && HunterOrders.Sim(ship).Mode.AllowResourceTrade(ship, live);

    /// <summary>
    /// Spec 1308: the station supplies the ship for free: each resource that its own storage lacks, as far as its storage has room,
    /// and crew for the free crew places. Returns the units and crew given.
    /// </summary>
    public static (int Units, int Crew) Supply(Ship ship)
    {
        int units = 0;
        foreach (var (id, need) in Lacking(ship, ownOnly: true).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            units += ship.Resources.AddResources(new ID<ResourceRules>(id), need, excludeConstructionSinks: true);
        int crew = ship.Crew.CreateCrewsAtRandomParts(ship.Crew.RemainingCrewCapacity);
        return (units, crew);
    }

    /// <summary>
    /// Spec 1308: the station repairs up to count of the ship's repairs at once, for free. Returns the repairs done.
    /// The vanilla crew repair cannot do this job for many AI ships: combat ships often have no storage bays, crews reach the
    /// station only through airlocks, and next to a trading station the vanilla repair module asks for credits, which an AI player
    /// does not have (AISelfRepairModule.TryRepairObject with the final cost of ShipUpdateInfo.GetTotalPhysicalCost).
    /// </summary>
    public static int StationRepair(Ship ship, int count)
    {
        int done = 0;
        foreach (var repair in RepairObjects(ship).ToList())
        {
            if (done >= count) break;
            using var info = ShipUpdateInfo.Alloc(ship);
            if (!AddRepair(info, repair) || !info.CanApplyPhysical()) continue;
            info.ApplyPhysical(destroyBlocking: false, builtByPlayer: false, updateDestroyed: true);
            done++;
        }
        return done;
    }

    /// <summary>A spot gap from the target's hull on the ship's side, so the ship stops next to it and not inside it.</summary>
    public static Vector2 Beside(Ship ship, Vector2 center, float radius, float gap)
    {
        var dir = center.DistanceTo(ship.DetWorldCenter) < 1f ? new Vector2(1f, 0f) : Vector2.FromAngle(center.DirectionTo(ship.DetWorldCenter));
        return center + dir * (radius + ship.WorldBoundingRadius + gap);
    }
}

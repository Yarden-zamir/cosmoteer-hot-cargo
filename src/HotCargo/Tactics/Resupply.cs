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
    /// not the loose nuggets and own-player ships nearby that the vanilla repair can also use (a station supply, so that ships at
    /// one station do not count each other's supply).
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

    /// <summary>Spec 1307: the nearest donor in range that holds a lacking resource. Ties go to the lower ID.</summary>
    public static Ship? FindDonor(Ship ship, IReadOnlyDictionary<string, int> lacking, TacticsTunables t)
    {
        if (lacking.Count == 0) return null;
        var types = lacking.Keys.Select(k => new ID<ResourceRules>(k)).ToList();
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

    /// <summary>Spec 1307: moves up to the donor share of each lacking resource from the donor's storage to the ship. Returns the units moved.</summary>
    public static int TakeFrom(Ship ship, Ship donor, IReadOnlyDictionary<string, int> lacking, TacticsTunables t)
    {
        int moved = 0;
        foreach (var (id, need) in lacking.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var type = new ID<ResourceRules>(id);
            int give = RepairModel.DonorGives(need, Stock(donor, type), t.FallBackDonorShare);
            if (give <= 0) continue;
            int removed = donor.Resources.RemoveResources(type, give);
            Give(ship, type, removed, toward: donor.DetWorldCenter);
            moved += removed;
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
    /// Spec 1308: the station supplies the ship for free: each lacking resource, crew for the free crew places, and all repairs
    /// queued. The queue skips the money check of the vanilla repair module (AISelfRepairModule.TryRepairObject): next to a
    /// trading station the vanilla cost asks for credits, which an AI player does not have, so the vanilla module queues nothing.
    /// </summary>
    public static (int Units, int Crew) Supply(Ship ship, Vector2 station)
    {
        int units = 0;
        foreach (var (id, need) in Lacking(ship, ownOnly: true).OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            Give(ship, new ID<ResourceRules>(id), need, toward: station);
            units += need;
        }
        int crew = ship.Crew.CreateCrewsAtRandomParts(ship.Crew.RemainingCrewCapacity);
        foreach (var repair in RepairObjects(ship).ToList())
        {
            using var info = ShipUpdateInfo.Alloc(ship);
            if (AddRepair(info, repair) && info.CanQueuePhysical()) info.QueuePhysical(updateDestroyed: true, isRepair: true, allowPartialConstruction: true);
        }
        return (units, crew);
    }

    /// <summary>
    /// Spec 1307-1308: puts the units into the ship's storage, and drops what does not fit as loose nuggets beside the hull,
    /// on the side of the giver. Many combat ships have no storage bays. The vanilla repair counts nuggets within 400 of the ship
    /// as usable (ResourceManager.GetUsableConstructionResourceTotal), so its crews fetch them for the repairs.
    /// </summary>
    static void Give(Ship ship, ID<ResourceRules> type, int amount, Vector2 toward)
    {
        int left = amount - ship.Resources.AddResources(type, amount, excludeConstructionSinks: true);
        if (left <= 0) return;
        var at = Beside(ship.DetWorldCenter, toward, ship.WorldBoundingRadius, NuggetGap);
        HunterOrders.Sim(ship).Nuggets.DropNuggets(type, left, new RotRect(at, new Vector2(NuggetGap, NuggetGap), Direction.Zero));
    }

    /// <summary>Nuggets land this far out from the hull, well inside the 400 that the vanilla repair reaches.</summary>
    const float NuggetGap = 60f;

    /// <summary>A spot gap from the target's hull on the ship's side, so the ship stops next to it and not inside it.</summary>
    public static Vector2 Beside(Ship ship, Vector2 center, float radius, float gap) =>
        Beside(center, ship.DetWorldCenter, radius + ship.WorldBoundingRadius, gap);

    /// <summary>A point radius + gap from center, toward the other point (or along +X when the two meet).</summary>
    static Vector2 Beside(Vector2 center, Vector2 toward, float radius, float gap)
    {
        var dir = center.DistanceTo(toward) < 1f ? new Vector2(1f, 0f) : Vector2.FromAngle(center.DirectionTo(toward));
        return center + dir * (radius + gap);
    }
}

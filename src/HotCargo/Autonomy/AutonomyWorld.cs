using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Ships.Parts.Weapons;
using Cosmoteer.Ships.Resources;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Tech;

namespace HotCargo.Autonomy;

/// <summary>A trading station, live or in stasis (spec 1069). Live is null in stasis.</summary>
internal readonly record struct StationRef(ObjectID Id, Vector2 Center, float Radius, int PlayerIndex, Ship? Live);

/// <summary>An asteroid with ore of the mine list, live or in stasis (spec 1070).</summary>
internal readonly record struct AsteroidRef(ObjectID Id, Vector2 Center, float Radius, Ship? Live);

/// <summary>
/// What the autonomy rows read and do in the sim (spec 1066 to 1075). Every result comes in a stable order, and every change
/// goes through sim state, so all co-op machines agree.
/// </summary>
internal static class AutonomyWorld
{
    // ---- Cargo: the storage bays (vanilla flexible storage grids).

    static IEnumerable<FlexResourceGrid.ResourceTile> CargoTiles(Ship ship) => FlexResourceGrid.GetAllOnShip(ship).SelectMany(g => g);

    /// <summary>Spec 1066: the mean fill of the storage bay tiles, 0 to 1; null without storage bays.</summary>
    internal static float? CargoFill(Ship ship)
    {
        float sum = 0;
        int tiles = 0;
        foreach (var t in CargoTiles(ship))
        {
            tiles++;
            if (t.ResourceTypeRules is { } r && r.MaxStackSize > 0) sum += (float)t.Resources / r.MaxStackSize;
        }
        return tiles == 0 ? null : sum / tiles;
    }

    /// <summary>The cargo by type, in type ID order.</summary>
    internal static SortedDictionary<string, int> CargoByType(Ship ship)
    {
        var byType = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in CargoTiles(ship))
            if (t.ResourceType is { } type && t.Resources > 0)
                byType[type.ToString()] = byType.GetValueOrDefault(type.ToString()) + t.Resources;
        return byType;
    }

    internal static bool HasCargoToSell(Ship ship, CargoSettings c) => CargoByType(ship).Keys.Any(c.Sells);

    /// <summary>Takes up to amount units of a type out of the storage bays, in tile order. Returns the units taken.</summary>
    static int TakeCargo(Ship ship, ID<ResourceRules> type, int amount)
    {
        int left = amount;
        foreach (var t in CargoTiles(ship))
        {
            if (left <= 0) break;
            if (t.ResourceType != type || t.Resources <= 0) continue;
            int n = Math.Min(left, t.Resources);
            t.SetResources(t.Resources - n, type);
            left -= n;
        }
        return amount - left;
    }

    /// <summary>Puts up to amount units of a type into the storage bays, in tile order. Returns the units placed.</summary>
    static int PutCargo(Ship ship, ID<ResourceRules> type, int amount)
    {
        int left = amount;
        foreach (IResourceSink t in CargoTiles(ship))
        {
            if (left <= 0) break;
            int room = t.GetRemainingCapacity(type, allowPreferredTypeMismatch: false);
            if (room > 0) left -= t.ReceiveResources(type, Math.Min(room, left), isCrewDelivery: false, sticky: false, playSound: false);
        }
        return amount - left;
    }

    static int CargoRoom(Ship ship, ID<ResourceRules> type) =>
        CargoTiles(ship).Sum(t => Math.Max(0, ((IResourceSink)t).GetRemainingCapacity(type, allowPreferredTypeMismatch: false)));

    // ---- Loot

    /// <summary>Spec 1066, 1068: loot in sight of the team that the loot filter takes, in nugget order. Tech crates have their own rows.</summary>
    internal static List<Nugget> AcceptedLoot(Ship ship, CargoSettings c)
    {
        var sim = HunterOrders.Sim(ship);
        var result = new List<Nugget>();
        for (int i = 0; i < sim.Nuggets.Count; i++)
        {
            var n = sim.Nuggets[i];
            if (n.ResourceType == Vaults.TechId || !n.IsWithinSightOfTeam(ship.Metadata.Team)) continue;
            if (c.LootAccepts(n.ResourceType.ToString(), Funnel.UnitValue(sim, n.ResourceType))) result.Add(n);
        }
        return result;
    }

    internal static float EdgeDistance(Ship ship, Vector2 point) => ship.DetWorldBoundingCircle.DistanceTo(point);

    /// <summary>The world point that the ship center flies to, so that its first Funnel (or its center) passes over the point.</summary>
    internal static Vector2 FunnelAim(Ship ship, Vector2 point)
    {
        var funnel = ship.Parts.Where(p => p.Rules.ID == Autonomous.FunnelId).OrderBy(p => p.Location.X).ThenBy(p => p.Location.Y).FirstOrDefault();
        if (funnel is null) return point;
        var box = Autonomous.BoxOf(funnel);
        var funnelWorld = ship.DetTransformPointToWorld(new Vector2(box.X + box.W / 2f, box.Y + box.H / 2f));
        return point - (funnelWorld - ship.DetWorldCenter);
    }

    /// <summary>
    /// Spec 1068: the vanilla collect order for loot within the crew safe distance, only when crew or a manipulator beam can fetch it.
    /// A crewless ship without a beam gives no order, because the open jobs would hold room in the bays that the Funnel needs.
    /// </summary>
    internal static void OrderCollect(Ship ship, IEnumerable<Nugget> loot)
    {
        bool fetchers = ship.Crew.Assigned.Count > 0 || ship.Parts.Any(p => p.Rules.ID.ToString() == "cosmoteer.manipulator_beam_emitter" && Autonomous.IsManned(p));
        if (!fetchers) return;
        foreach (var n in loot)
        {
            if (EdgeDistance(ship, n.DetWorldLocation) >= GameApp.Rules.Crew.LoseHomeShipWarningDistance) continue;
            int left = n.Quantity - ship.Resources.GetAnticipatedPickUp(n, MPValueType.Confirmed);
            while (left > 0)
            {
                var sink = ship.Resources.GetBestReceivingSink(n, left, MPValueType.Confirmed, out int capacity);
                if (sink is null || capacity <= 0) break;
                int amount = Math.Min(left, capacity);
                ship.Resources.ManualResourceTransfer(n, sink, amount, sticky: false, Cosmoteer.Ships.Crew.Jobs.TradeType.None, 0, autoCancel: true, confirmed: true, ship.Jobs.AllocGlobalJobID());
                left -= amount;
            }
        }
    }

    // ---- Stations

    /// <summary>Spec 1069: live ships that trade with this ship, then stations in stasis that are not enemies, by ID.</summary>
    internal static List<StationRef> Stations(Ship ship)
    {
        var sim = HunterOrders.Sim(ship);
        var result = new List<StationRef>();
        var seen = new HashSet<ObjectID>();
        for (int i = 0; i < sim.Ships.Count; i++)
        {
            var s = sim.Ships[i];
            if (s == ship || s.IsJunk || s.AI?.WillTradeWith(ship) != true) continue;
            result.Add(new StationRef(s.UniqueID, s.DetWorldCenter, s.WorldBoundingRadius, s.Metadata.PlayerIndex, s));
            seen.Add(s.UniqueID);
        }
        foreach (var ms in SpaceStation.GetStations(sim, onlyOperating: true).OrderBy(m => m.StationID))
        {
            if (ms.PhysicalShip is not null || ms.Spawner is not { } sp || seen.Contains(ms.StationID)) continue;
            if (ship.IsEnemiesWith(sp.ObjectAllegiance)) continue;
            result.Add(new StationRef(ms.StationID, ms.DetWorldCenter, 0f, sp.ObjectAllegiance, null));
        }
        return result;
    }

    /// <summary>Spec 1066: the nearest live station within the vanilla trade distance (AllowResourceTrade), or null.</summary>
    internal static Ship? StationInRange(Ship ship)
    {
        var sim = HunterOrders.Sim(ship);
        return Stations(ship).Where(s => s.Live is { } live && sim.Mode.AllowResourceTrade(ship, live))
            .OrderBy(s => EdgeDistance(ship, s.Center) - s.Radius).ThenBy(s => s.Id).Select(s => s.Live).FirstOrDefault();
    }

    /// <summary>Spec 1069: the station that "go to station" picks.</summary>
    internal static StationRef? PickStation(Ship ship, ShipAi.StationPick pick, CargoSettings c)
    {
        var sim = HunterOrders.Sim(ship);
        var stations = Stations(ship);
        var cargo = CargoByType(ship);
        double? Score(StationRef s) => pick switch
        {
            ShipAi.StationPick.Nearest => 0,
            ShipAi.StationPick.BestPrice => cargo.Where(kv => c.Sells(kv.Key)).Sum(kv => (double)kv.Value * sim.Mode.GetResourceSellRefund(new ID<ResourceRules>(kv.Key), s.PlayerIndex)),
            ShipAi.StationPick.Cheapest => c.Buy.Length == 0 ? null : -c.Buy.Sum(id => (double)sim.Mode.GetResourceBuyCost(new ID<ResourceRules>(id), s.PlayerIndex)),
            _ => throw new ArgumentOutOfRangeException(nameof(pick), pick, null),
        };
        var items = stations.Select(s => (Math.Max(0f, EdgeDistance(ship, s.Center) - s.Radius), s.Id, Score(s))).ToList();
        return AutonomyModel.Pick(items, useScore: pick != ShipAi.StationPick.Nearest) is { } i ? stations[i] : null;
    }

    // ---- Trade (spec 1073, 1074): at once, without crew.

    static PlayerState Owner(Ship ship) => HunterOrders.Sim(ship).RequireGame().GetPlayerState(ship.Metadata.PlayerIndex, deterministic: true);

    /// <summary>Sells all cargo of the sell list to the station. Returns the units sold.</summary>
    internal static int Sell(Ship ship, Ship station, CargoSettings c)
    {
        var sim = HunterOrders.Sim(ship);
        int sold = 0;
        foreach (var (id, have) in CargoByType(ship))
        {
            if (!c.Sells(id)) continue;
            var type = new ID<ResourceRules>(id);
            int price = sim.Mode.GetResourceSellRefund(type, station.Metadata.PlayerIndex);
            int taken = station.Resources.AddResources(type, have);
            if (taken <= 0) continue;
            int removed = TakeCargo(ship, type, taken);
            if (removed != taken) throw new InvalidOperationException($"sell {id}: the station took {taken}, the bays gave {removed}");
            Owner(ship).AddMoney(price * taken);
            sold += taken;
            Log.Info($"autonomy: {ship.Metadata.PlainShipName} sold {taken} {id} for {price * taken} to {station.Metadata.PlainShipName}");
        }
        return sold;
    }

    /// <summary>Buys the buy list from the station's stock until the bays are full, above the money reserve. Returns the units bought.</summary>
    internal static int Buy(Ship ship, Ship station, CargoSettings c, long reserve)
    {
        var sim = HunterOrders.Sim(ship);
        int bought = 0;
        foreach (string id in c.Buy)
        {
            var type = new ID<ResourceRules>(id);
            int cost = sim.Mode.GetResourceBuyCost(type, station.Metadata.PlayerIndex);
            if (cost <= 0) continue;
            int n = AutonomyModel.BuyAmount(Owner(ship).Money, reserve, cost, CargoRoom(ship, type), station.Resources.GetResourceTotal(type));
            if (n <= 0) continue;
            int removed = station.Resources.RemoveResources(type, n);
            int placed = PutCargo(ship, type, removed);
            // Units that found no tile go back to the station, so nothing is lost or paid for twice.
            if (placed < removed) station.Resources.AddResources(type, removed - placed);
            Owner(ship).AddMoney(-cost * placed);
            bought += placed;
            Log.Info($"autonomy: {ship.Metadata.PlainShipName} bought {placed} {id} for {cost * placed} from {station.Metadata.PlainShipName}");
        }
        return bought;
    }

    /// <summary>The owner's nearest crewed ship within the vanilla trade distance, or null.</summary>
    internal static Ship? CrewedInRange(Ship ship) => HunterOrders.Sim(ship).Ships.HumanPlayerShips
        .Where(s => s != ship && s.Metadata.PlayerIndex == ship.Metadata.PlayerIndex && Autonomous.IsCrewed(s)
            && ship.DetWorldBoundingCircle.DistanceTo(s.DetWorldBoundingCircle) <= GameApp.Rules.Crew.LoseHomeShipWarningDistance)
        .OrderBy(s => HunterOrders.EdgeDistance(ship, s)).ThenBy(s => s.UniqueID).FirstOrDefault();

    /// <summary>Spec 1067: moves all cargo into the crewed ship's storages that accept it (vanilla AddResources). Returns the units moved.</summary>
    internal static int Unload(Ship ship, Ship to)
    {
        int moved = 0;
        foreach (var (id, have) in CargoByType(ship))
        {
            var type = new ID<ResourceRules>(id);
            int taken = to.Resources.AddResources(type, have);
            if (taken <= 0) continue;
            int removed = TakeCargo(ship, type, taken);
            if (removed != taken) throw new InvalidOperationException($"unload {id}: the ship took {taken}, the bays gave {removed}");
            moved += taken;
        }
        if (moved > 0) Log.Info($"autonomy: {ship.Metadata.PlainShipName} unloaded {moved} into {to.Metadata.PlainShipName}");
        return moved;
    }

    // ---- Asteroids and wrecks

    /// <summary>The ore types of a part: the resources it drops. Rock parts drop none.</summary>
    static IEnumerable<string> OresOf(Part part) => part.Rules.Resources.Select(r => r.Type.ToString());

    static bool MinesPart(Part part, CargoSettings c) => OresOf(part).Any(c.Mines);

    /// <summary>Spec 1070: live asteroids with ore of the mine list, then asteroids in stasis whose doodad names such an ore, by ID.</summary>
    internal static List<AsteroidRef> Asteroids(Ship ship, CargoSettings c)
    {
        var sim = HunterOrders.Sim(ship);
        var result = new List<AsteroidRef>();
        for (int i = 0; i < sim.Ships.Count; i++)
        {
            var s = sim.Ships[i];
            if (s.Rules.IsAsteroid && s.Parts.Count > 0 && s.Parts.Any(p => MinesPart(p, c)))
                result.Add(new AsteroidRef(s.UniqueID, s.DetWorldCenter, s.WorldBoundingRadius, s));
        }
        foreach (var sp in sim.Stasis.OfType<StasisDoodadSpawner>().OrderBy(s => s.ReservedObjectID))
        {
            if (AutonomyModel.OreOfDoodad(sp.DoodadID.ToString()) is not { } ore) continue;
            if (!GameApp.Rules.HasResourceRules(new ID<ResourceRules>(ore)) || !c.Mines(ore)) continue;
            result.Add(new AsteroidRef(sp.ReservedObjectID, sp.Location, 0f, null));
        }
        return result;
    }

    internal static AsteroidRef? NearestAsteroid(Ship ship, CargoSettings c)
    {
        var all = Asteroids(ship, c);
        var items = all.Select(a => (Math.Max(0f, EdgeDistance(ship, a.Center) - a.Radius), a.Id, (double?)0)).ToList();
        return AutonomyModel.Pick(items, useScore: false) is { } i ? all[i] : null;
    }

    /// <summary>Spec 1066, 1072: the nearest wreck on radar: a junk ship with parts that is not an asteroid.</summary>
    internal static Ship? NearestWreck(Ship ship)
    {
        var sim = HunterOrders.Sim(ship);
        Ship? best = null;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < sim.Ships.Count; i++)
        {
            var s = sim.Ships[i];
            if (!s.IsJunk || s.Rules.IsAsteroid || s.Parts.Count == 0 || !s.IsWithinRadarOfTeam(ship.Metadata.Team)) continue;
            float d = HunterOrders.EdgeDistance(ship, s);
            if (d < bestDistance || (d == bestDistance && best is not null && s.UniqueID.CompareTo(best.UniqueID) < 0)) (best, bestDistance) = (s, d);
        }
        return best;
    }

    /// <summary>Spec 1070: the part that the ship works on: the nearest part (to its center) that it salvages now, else the nearest part that the order would take.</summary>
    internal static Part? WorkPart(Ship ship, Ship target, CargoSettings? mine)
    {
        var from = ship.DetWorldCenter;
        Part? Nearest(Func<Part, bool> which) => target.Parts.Where(which)
            .OrderBy(p => from.DistanceSquaredTo(p.DetWorldCenter)).ThenBy(p => p.Location.X).ThenBy(p => p.Location.Y).FirstOrDefault();
        return Nearest(p => ship.Resources.IsSalvaging(p, MPValueType.Confirmed)) ?? Nearest(p => mine is null || MinesPart(p, mine));
    }

    /// <summary>
    /// Spec 1070, 1072: where the ship holds to work on the target, and its world rotation. It stands beside the work part, on the part's
    /// side of the target (the line from the target center out through the part), so the part stays in reach as the target turns. With a
    /// mining laser, the laser with the longest range stands MineDistance outside the target's bounding circle, closer when its reach
    /// (range × MineReach) needs it, and the ship turns so that the center of the laser's arc points at the part: the beam leaves the hull
    /// forward, inside the arc and clear of the ship's own parts. Without a laser, the hull keeps MineDistance and the ship's +X axis
    /// points at the part. Null when the target has no part to work on. Limit: it plans for one laser; other lasers fire when the part
    /// happens to be in their reach.
    /// </summary>
    internal static (Vector2 Spot, Direction Rotation)? WorkSpot(Ship ship, Ship target, CargoSettings? mine, float gap, float reachShare)
    {
        if (WorkPart(ship, target, mine) is not { } part) return null;
        var p = part.DetWorldCenter;
        var c = target.DetWorldCenter;
        float inside = c.DistanceTo(in p);
        Vector2 n = inside > 1e-3f ? (p - c) / inside : Vector2.FromAngle(c.DirectionTo(ship.DetWorldCenter));
        float depth = Math.Max(0f, target.WorldBoundingRadius - inside);
        var laser = ship.Weapons.OfType<TurretWeapon>().Where(IsSalvageTurret)
            .OrderByDescending(w => w.GetEffectiveRange(WeaponCoverageMode.Confirmed)).ThenBy(w => w.Part.Location.X).ThenBy(w => w.Part.Location.Y).FirstOrDefault();
        if (laser is null)
        {
            var spot = p + n * (depth + ship.WorldBoundingRadius + gap);
            return (spot, spot.DirectionTo(in p));
        }
        float standOff = AutonomyModel.LaserStandOff(depth, gap, laser.GetEffectiveRange(WeaponCoverageMode.Confirmed) * reachShare);
        var off = ship.DetTransformPointToWorld(laser.ShipLocation) - ship.DetWorldCenter;
        float arc = (float)ship.DetTransformRotationToWorld(laser.GetLocalArc(TurretWeapon.ArcType.Targeting, WeaponCoverageMode.Confirmed).Center);
        var (x, y, turn) = AutonomyModel.LaserSpot(p.X, p.Y, n.X, n.Y, standOff, off.X, off.Y, arc);
        return (new Vector2(x, y), ship.DetRotation + turn);
    }

    /// <summary>A turret that picks salvage jobs (a mining laser): one of its auto targets is salvage (TurretWeapon.TryAcquireSalvageTarget).</summary>
    internal static bool IsSalvageTurret(TurretWeapon w) => w.Rules.AutoTargets.Any(a => a.TargetType == WeaponTargetType.Salvage);

    /// <summary>
    /// Spec 1070, 1072: the vanilla salvage order for the parts of the target nearest to the ship, so that salvage lasers pick them.
    /// Keeps at most maxJobs open jobs; a part already ordered is skipped. ores: the collect types, or null for every part (salvage).
    /// </summary>
    internal static void OrderSalvage(Ship ship, Ship target, CargoSettings? mine, int maxJobs)
    {
        int open = ship.Resources.GetSalvageJobs(MPValueType.Confirmed).Count();
        if (open >= maxJobs) return;
        HashSet<ID<ResourceRules>>? collect = mine is null ? null
            : target.Parts.SelectMany(p => p.Rules.Resources).Select(r => r.Type).Where(t => mine.Mines(t.ToString())).ToHashSet();
        var from = ship.DetWorldCenter;
        var parts = target.Parts.Where(p => (mine is null || MinesPart(p, mine)) && !ship.Resources.IsSalvaging(p, MPValueType.Confirmed))
            .OrderBy(p => from.DistanceSquaredTo(p.DetWorldCenter)).ThenBy(p => p.Location.X).ThenBy(p => p.Location.Y)
            .Take(maxJobs - open).ToList();
        foreach (var p in parts) ship.Resources.SalvagePart(p, collect, autoCancel: true, confirmed: true);
    }
}

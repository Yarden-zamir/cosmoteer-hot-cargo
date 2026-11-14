using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.AI.StrategyModules;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HotCargo.Autonomy;
using HotCargo.Core;
using HotCargo.Events;
using HotCargo.Hunters;
using HotCargo.Tech;

namespace HotCargo.Scavenge;

/// <summary>What a scavenger collects (spec 1333): a wreck that it salvages, or a loose resource that its crew fetch.</summary>
internal abstract record ScavengeTarget(ObjectID Id);

internal sealed record WreckTarget(Ship Wreck) : ScavengeTarget(Wreck.UniqueID);

internal sealed record LootTarget(Nugget Loot) : ScavengeTarget(Loot.UniqueID);

/// <summary>
/// The shared scavenge behavior (spec 1330 to 1339): any ship with crew and storage bays finds old wrecks and loose resources, salvages
/// or collects them, and sells the cargo at a station. The HcScavenge AI module drives it; other code (an AI that repairs near wrecks)
/// can call FindTarget and Work directly. Every result comes in a stable order, and every change goes through sim state.
/// </summary>
internal static class Scavenger
{
    // ---- Ages (spec 1331, 1332)

    /// <summary>A junk ship that can hold salvage: not an asteroid, with parts. IsWreck adds the rules that keep a wreck off limits.</summary>
    static bool IsJunkPiece(Ship s) => s.IsJunk && !s.Rules.IsAsteroid && s.Parts.Count > 0;

    /// <summary>A loose resource that is not a tech crate. Mission items are filtered when a target is picked (they change with the missions).</summary>
    static bool IsLooseResource(Nugget n) => n.ResourceType != Vaults.TechId;

    /// <summary>
    /// Spec 1332: records the first check at which each junk piece and loose resource was seen, and forgets the ones that are gone.
    /// Ships and nuggets go in sim order.
    /// </summary>
    internal static void CheckAges(Run run, SimRoot sim)
    {
        var seen = run.State.Scavenge.Seen;
        double now = run.State.GameSeconds;
        var live = new HashSet<ObjectID>();
        for (int i = 0; i < sim.Ships.Count; i++)
            if (sim.Ships[i] is var s && IsJunkPiece(s)) live.Add(s.UniqueID);
        for (int i = 0; i < sim.Nuggets.Count; i++)
            if (sim.Nuggets[i] is var n && IsLooseResource(n)) live.Add(n.UniqueID);
        foreach (var id in live) seen.TryAdd(id, now);
        foreach (var id in seen.Keys.Where(id => !live.Contains(id)).ToList()) seen.Remove(id);
    }

    static bool Ripe(Run run, ObjectID id) =>
        run.State.Scavenge.Seen.TryGetValue(id, out double since) && ScavengeModel.Ripe(since, run.State.GameSeconds, run.T.Scavenge.CooldownSeconds);

    // ---- Targets (spec 1330, 1333)

    /// <summary>
    /// Spec 1330: a wreck that a scavenger may take: a junk piece that is not a station or landmark, the relic, a vanilla abandoned ship
    /// or named by a mission, has no crew aboard, and that no other ship salvages (a player who salvages it keeps it).
    /// </summary>
    static bool IsWreck(Run run, Ship s, Ship self)
    {
        if (!IsJunkPiece(s) || s.SpecialComponents.Count > 0 || s.Crew.OnShip.Count > 0) return false;
        if (s.HasTag(Abandoned.RelicTag) || s.HasLabel(Abandoned.Label)) return false;
        foreach (var part in s.Parts)
            if (SalvageJob.s_jobsByPart.TryGetValue(part, out var jobs) && jobs.Any(j => j.Ship != self)) return false;
        foreach (var mission in run.Mode.Missions) if (mission.IsShipAssociated(s)) return false;
        return true;
    }

    static HashSet<ID<ResourceRules>> MissionItems(Run run) => run.Mode.Missions.SelectMany(MissionCargo.ItemTypes).ToHashSet();

    static float Distance(Ship self, ScavengeTarget t) => t switch
    {
        WreckTarget w => HunterOrders.EdgeDistance(self, w.Wreck),
        LootTarget l => AutonomyWorld.EdgeDistance(self, l.Loot.DetWorldLocation),
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, null),
    };

    /// <summary>
    /// Spec 1333: ripe wrecks on the radar of the ship's team and ripe loose resources in its sight, within SearchRange, nearest first
    /// (ties by ID). A loose resource needs room for its type in the storage bays; a wreck needs any room.
    /// </summary>
    internal static List<ScavengeTarget> Targets(Run run, Ship self)
    {
        var t = run.T.Scavenge;
        var sim = HunterOrders.Sim(self);
        int team = self.Metadata.Team;
        var found = new List<ScavengeTarget>();
        if (AutonomyWorld.CargoFill(self) is not { } fill || fill >= 1f) return found;
        for (int i = 0; i < sim.Ships.Count; i++)
        {
            var s = sim.Ships[i];
            if (s.IsWithinRadarOfTeam(team) && Ripe(run, s.UniqueID) && IsWreck(run, s, self)) found.Add(new WreckTarget(s));
        }
        var items = MissionItems(run);
        for (int i = 0; i < sim.Nuggets.Count; i++)
        {
            var n = sim.Nuggets[i];
            if (!IsLooseResource(n) || items.Contains(n.ResourceType) || !n.IsWithinSightOfTeam(team) || !Ripe(run, n.UniqueID)) continue;
            if (AutonomyWorld.CargoRoom(self, n.ResourceType) > 0) found.Add(new LootTarget(n));
        }
        return found.Select(x => (Target: x, Distance: Distance(self, x))).Where(x => x.Distance <= t.SearchRange)
            .OrderBy(x => x.Distance).ThenBy(x => x.Target.Id).Select(x => x.Target).ToList();
    }

    /// <summary>Spec 1333: the nearest target, or null.</summary>
    internal static ScavengeTarget? FindTarget(Run run, Ship self) => Targets(run, self).FirstOrDefault();

    // ---- Work (spec 1334, 1335)

    /// <summary>
    /// Spec 1334, 1335: one step of work on the target. A wreck: fly to WorkDistance from its hull, nose to it, and give salvage orders
    /// (crew and mining lasers) for its nearest parts, with the crew fetching what comes off. Loose resources: fly to the target and order
    /// the crew to fetch every ripe loose resource within their safe distance.
    /// </summary>
    internal static void Work(Run run, Ship self, ScavengeTarget target)
    {
        var t = run.T.Scavenge;
        switch (target)
        {
            case WreckTarget { Wreck: var wreck }:
            {
                // The autonomy placement (spec 1070): beside the work part, with a mining laser aimed at it.
                if (AutonomyWorld.WorkSpot(self, wreck, CargoSettings.Default, t.WorkDistance, run.T.Autonomy.MineReach) is { } work)
                    MoveFacing(self, work.Spot, work.Rotation, Math.Max(t.WorkDistance * 0.25f, 10f));
                // CargoSettings.Default takes every part with resources and every resource type: the crew fetch all of it.
                if (HunterOrders.EdgeDistance(self, wreck) <= t.WorkDistance * 3f) AutonomyWorld.OrderSalvage(self, wreck, CargoSettings.Default, t.SalvageJobs);
                break;
            }
            case LootTarget { Loot: var loot }:
            {
                float reach = GameApp.Rules.Crew.LoseHomeShipWarningDistance;
                if (AutonomyWorld.EdgeDistance(self, loot.DetWorldLocation) > reach * 0.5f) HunterOrders.MoveTo(self, loot.DetWorldLocation, reach * 0.25f);
                AutonomyWorld.OrderCollect(self, Targets(run, self).OfType<LootTarget>().Select(l => l.Loot));
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }
    }

    /// <summary>A move that ends with the given world rotation, kept while the goal moved less than tolerance and turned less than 10 degrees.</summary>
    static void MoveFacing(Ship ship, Vector2 spot, Direction face, float tolerance)
    {
        if (ship.Commands.CurConfirmedCommand is MoveCommand { Location: { } at, Rotation: { } rot } && at.DistanceTo(in spot) <= tolerance
            && (float)rot.AngularDistanceTo(face) <= 10f * MathF.PI / 180f) return;
        ship.Commands.EnqueueCommand(new MoveCommand(ship, ship.Commands.AllocGlobalCommandID(), spot, face, CollisionAvoidanceFlags.All, isTentative: false),
            append: false, confirmed: true);
    }

    // ---- Sell (spec 1336)

    /// <summary>Units that scavengers sold since start. For autotests; not sim state.</summary>
    internal static int SoldUnits;

    /// <summary>
    /// Spec 1336: flies to the nearest station that trades with the ship and, once in the vanilla trade distance, sells all storage bay cargo
    /// there. False when no station trades with the ship.
    /// </summary>
    internal static bool Sell(Run run, Ship self)
    {
        if (AutonomyWorld.StationInRange(self) is { } station)
        {
            SoldUnits += AutonomyWorld.Sell(self, station, CargoSettings.Default);
            return true;
        }
        if (AutonomyWorld.PickStation(self, ShipAi.StationPick.Nearest, CargoSettings.Default) is not { } s) return false;
        float dock = run.T.Autonomy.DockDistance;
        Vector2 dir = Vector2.FromAngle(s.Center.DirectionTo(self.DetWorldCenter));
        HunterOrders.MoveTo(self, s.Center + dir * (s.Radius + self.WorldBoundingRadius + dock), dock * 0.5f);
        return true;
    }

    /// <summary>A station trades with the ship (live or in stasis).</summary>
    internal static bool CanSell(Ship self) => AutonomyWorld.Stations(self).Count > 0;

    // ---- One AI step (spec 1337)

    /// <summary>
    /// Spec 1337: a ship scavenges only when idle: no enemy in its sight, no attack order, and its exit module does not run.
    /// </summary>
    internal static bool Idle(Ship ship, ShipAI ai) =>
        ship.Commands.CanExecuteCommands && ship.Commands.CurConfirmedCommand is not IAttackCommand
        && ai.GetStrategyModule<AIExitSectorModule>() is not { IsActivated: true } && EnemyInSight(ship) is null;

    /// <summary>
    /// Spec 1337: the first significant enemy (sim order) whose hull touches the ship's sight circle: the circle in which the vanilla
    /// aggro module (AIAggroEnemiesModule, AcquireAggroWithinRadar = false) picks targets. An enemy that is only on radar does not count,
    /// so a far enemy platform does not stop scavenging.
    /// </summary>
    internal static Ship? EnemyInSight(Ship ship)
    {
        var sight = ship.Sensors.DetWorldSightCircle;
        var ships = HunterOrders.Sim(ship).Ships;
        for (int i = 0; i < ships.Count; i++)
        {
            var other = ships[i];
            if (other != ship && other.IsSignificant && !other.IsJunk && ship.IsEnemiesWith(other) && other.DetWorldBoundingCircle.IntersectsWith(sight)) return other;
        }
        return null;
    }

    /// <summary>Spec 1336: decides and does one step. sells: the ship sells its cargo (else it keeps it). Returns the step it took.</summary>
    internal static ScavengeStep Step(Run run, Ship self, bool sells)
    {
        if (AutonomyWorld.CargoFill(self) is not { } fill) return ScavengeStep.Idle;
        var target = FindTarget(run, self);
        var step = ScavengeModel.Next(Math.Clamp(fill, 0f, 1f), target is not null, sells && CanSell(self), run.T.Scavenge.FullFraction);
        switch (step)
        {
            case ScavengeStep.Collect:
                Work(run, self, target!);
                break;
            case ScavengeStep.Sell:
                if (!Sell(run, self)) return ScavengeStep.Idle;
                break;
        }
        return step;
    }
}

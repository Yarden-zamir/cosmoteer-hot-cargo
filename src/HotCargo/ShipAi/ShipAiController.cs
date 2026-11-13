using System.Collections.Concurrent;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts.Resources;
using HotCargo.Ai;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Defenses;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Ships.Parts.Thrusters;
using Cosmoteer.Ships.Parts.Weapons;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Geometry;
using Halfling.Timing;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Tech;

namespace HotCargo.ShipAi;

/// <summary>
/// Runs the tactics of player ships in the sim tick (spec 403 to 408). Every ShipAi.TickSeconds a ship with Auto on reads its facts,
/// evaluates its rows and turns the decision into vanilla commands (attack, move, follow, local FTL jump) and weapon targets.
/// Ships go in sim order and every input is sim state, so all co-op machines decide the same.
/// </summary>
internal static class ShipAiController
{
    /// <summary>Limit: the battery resource is found by its vanilla ID, because no rule names it. Revisit if a game update renames it.</summary>
    static readonly ID<ResourceRules> Battery = new("battery");

    /// <summary>Evaluations since start. For autotests; not sim state.</summary>
    internal static int Evaluations;

    internal static void OnTick(object? sender, SimRoot sim)
    {
        if (Run.Current is not { } run || run.Game.Sim != sim) return;
        var tun = run.T.ShipAi;
        Time now = sim.LogicalTime;
        for (int i = 0; i < sim.Ships.Count; i++)
        {
            var ship = sim.Ships[i];
            if (ShipTactics.Of(ship) is not { } t) continue;
            if (!tun.Enabled || !t.Auto || !ship.IsHumanPlayerShip)
            {
                if (t.Matched != 0 || t.Issued is not null || t.Waiting) Errors.Guard("ship tactics release", () => Release(ship, t));
                continue;
            }
            // Each system has its own sim clock. A ship that arrives from another system starts at once.
            if (t.NextTick > now + (Time)tun.TickSeconds) t.NextTick = now;
            if (now < t.NextTick) continue;
            t.NextTick = now + (Time)tun.TickSeconds;
            Errors.Guard("ship tactics", () => Think(run, ship, t, tun));
        }
    }

    /// <summary>The tactics input (spec 409). Clears the old decision, so the new rows decide from scratch at the next tick.</summary>
    internal static void Set(Ship ship, string text, bool auto, string cargo, bool recall, SimRoot sim)
    {
        var rows = ShipAiModel.Decode(text);
        if (Run.Current is { } run && ShipAiModel.StageError(rows, run.T.Heat.Stages.Length) is { } error) throw new FormatException(error);
        Autonomy.Cargo.Decode(cargo);
        var t = ShipTactics.For(ship);
        Release(ship, t);
        t.SetRows(text);
        t.SetCargo(cargo);
        t.Auto = auto;
        t.Recall = recall;
        t.NextTick = sim.LogicalTime;
        Log.Info($"ship tactics: {ship.Metadata.PlainShipName} auto={auto} recall={recall} rows={t.Rows.Count} {text} cargo={cargo}");
    }

    /// <summary>
    /// Spec 408: a command with a player ID came from a player (a click or a key), not from the sim. A move that arrived counts as done:
    /// the game keeps the last command in the queue after it is satisfied, and the tactics must take over again.
    /// </summary>
    internal static bool HasManualOrder(Ship ship)
    {
        foreach (var c in ship.Commands.ConfirmedCommands)
            if (c.UniqueID.PlayerID is not null && !c.IsSatisfied()) return true;
        return false;
    }

    static void Think(Run run, Ship ship, ShipTactics t, ShipAiTunables tun)
    {
        if (!ship.Commands.CanExecuteCommands) return;
        if (HasManualOrder(ship))
        {
            if (!t.Waiting)
            {
                Release(ship, t);
                t.Waiting = true;
                Trace(ship, tun, "waits for a manual order");
            }
            return;
        }
        t.Waiting = false;
        var rows = t.Rows;
        var view = new View(run, ship, t);
        var d = ShipAiModel.Evaluate(rows, view.Facts(), view.CanRun, t.Memory, HunterOrders.Sim(ship).LogicalTime.Seconds,
            new Hysteresis(tun.HysteresisMargin, tun.MinHoldSeconds));
        Ship? target = d.TargetRow >= 0 ? view.TargetFor(rows[d.TargetRow].Act) : null;
        // Spec 1064: "return to me" replaces the move rows.
        Act? move = t.Recall ? Act.ReturnToFleet : d.MoveRow >= 0 ? rows[d.MoveRow].Act : null;
        Ship? guarded = move == Act.Guard ? view.GuardTarget((GuardWho)rows[d.MoveRow].ActParam) : null;
        // A guard with no target row attacks whoever attacks the guarded ship.
        if (target is null && guarded is not null) target = view.AttackerOf(guarded);
        Act? before = t.Recall ? Act.ReturnToFleet : t.MoveRow >= 0 && t.MoveRow < rows.Count ? rows[t.MoveRow].Act : null;
        Side? facing = d.FacingRow >= 0 ? (Side)rows[d.FacingRow].ActParam : null;
        Side? facingBefore = t.FacingRow >= 0 && t.FacingRow < rows.Count ? (Side)rows[t.FacingRow].ActParam : null;
        int moveParam = !t.Recall && d.MoveRow >= 0 ? rows[d.MoveRow].ActParam : 0;
        var attack = Move(ship, t, tun, view, move, moveParam, target, guarded, fresh: before != move || facing != facingBefore);
        if (attack is not null && facing is { } side) Face(attack, side);
        if (d.TradeRow >= 0) Trade(run, ship, t, view, rows[d.TradeRow].Act);
        int mask = 0;
        for (int i = 0; i < d.Matched.Length; i++) if (d.Matched[i]) mask |= 1 << i;
        Weapons(ship, t, tun, rows, t.Matched, mask, target, view.Caller);
        if (mask != t.Matched || d.TargetRow != t.TargetRow || d.MoveRow != t.MoveRow || d.FacingRow != t.FacingRow || d.TradeRow != t.TradeRow
            || target?.UniqueID != t.Target)
            Trace(ship, tun, $"target row {d.TargetRow} ({target?.Metadata.PlainShipName ?? "-"}) move row {d.MoveRow} ({move?.ToString() ?? "-"}) " +
                             $"facing row {d.FacingRow} trade row {d.TradeRow} matched {Convert.ToString(mask, 2)}");
        t.TargetRow = d.TargetRow;
        t.MoveRow = d.MoveRow;
        t.FacingRow = d.FacingRow;
        t.TradeRow = d.TradeRow;
        t.Memory = d.Memory;
        t.Matched = mask;
        t.Target = target?.UniqueID;
        Evaluations++;
    }

    static void Trace(Ship ship, ShipAiTunables tun, string message)
    {
        if (tun.Log) Log.Info($"ship tactics [{ship.Metadata.PlainShipName} {ship.UniqueID}] {message}");
    }

    /// <summary>Drops the decision: removes the tactics' own command and their weapon targets. The rows and Auto stay.</summary>
    internal static void Release(Ship ship, ShipTactics t)
    {
        Stop(ship, t);
        if (t.Matched != 0)
        {
            var groups = PartGroups(ship);
            var rows = t.Rows;
            var matched = t.MatchedRows;
            // Without the tunables here, a weapon counts as aimed when it aims as a disruptor or as another weapon.
            foreach (var weapon in ship.Weapons)
            {
                var g = GroupsOf(weapon, groups);
                if (ShipAiModel.AimsAtParts(ShipAiModel.WeaponAct(rows, matched, g, disruptor: false)) ||
                    ShipAiModel.AimsAtParts(ShipAiModel.WeaponAct(rows, matched, g, disruptor: true))) ClearAim(ship, weapon);
            }
        }
        t.Matched = 0;
        t.TargetRow = t.MoveRow = t.FacingRow = t.TradeRow = -1;
        t.Memory = null;
        t.Target = null;
        t.Anchor = null;
        t.Held = null;
        t.Waiting = false;
    }

    // ---- Movement and target: one vanilla command at a time.

    /// <summary>Gives the move command. Returns the attack command when the ship attacks, so a facing row can turn it.</summary>
    static AttackCommand? Move(Ship ship, ShipTactics t, ShipAiTunables tun, View view, Act? move, int moveParam, Ship? target, Ship? guarded, bool fresh)
    {
        if (move != Act.HoldPosition) t.Anchor = null;
        switch (move)
        {
            case Act.HoldPosition:
                t.Anchor ??= ship.DetWorldCenter;
                MoveTo(ship, t, t.Anchor.Value, tun.HoldTolerance);
                return null;
            case Act.RetreatToAlly:
                Follow(ship, t, view.NearestAlly ?? throw new InvalidOperationException("RetreatToAlly ran without an ally"));
                return null;
            case Act.FollowLeader:
                Follow(ship, t, view.Leader ?? throw new InvalidOperationException("FollowLeader ran without a leader"));
                return null;
            case Act.Guard when target is not null:
                return Attack(ship, t, target, fresh);
            case Act.Guard:
                Follow(ship, t, guarded ?? throw new InvalidOperationException("Guard ran without a ship to guard"));
                return null;
            case Act.RetreatRepair:
                RunFrom(ship, t, view, tun.RetreatDistance);
                return null;
            case Act.FleeJump:
                if (!TryJump(ship, t, view)) RunFrom(ship, t, view, tun.RetreatDistance * 2f);
                return null;
            case Act.CollectTech:
                Collect(ship, t, tun, view);
                return null;
            case Act.CollectLoot:
                CollectLoot(ship, t, view);
                return null;
            case Act.GoToStation:
                GoToStation(ship, t, view, (StationPick)moveParam);
                return null;
            case Act.Mine:
                Mine(ship, t, view);
                return null;
            case Act.Salvage:
                SalvageWreck(ship, t, view);
                return null;
            case Act.ReturnToFleet:
                if (Autonomy.Autonomous.FirstCrewed(ship) is { } home) Follow(ship, t, home);
                else Stop(ship, t);
                return null;
            case null or Act.Kite or Act.CloseIn when target is not null:
                var ac = Attack(ship, t, target, fresh);
                if (move == Act.Kite && HunterOrders.MaxWeaponRange(ship, onlyActive: true) is var range and > 0f)
                {
                    // Same sight cap as the vanilla attack tactics, so the target stays in sight.
                    float sightCap = ship.Sensors.SightRadius - ship.WorldBoundingRadius * 2f - ac.TargetRadius;
                    ac.EdgeToEdgeFollowRadius = Math.Max(0f, Math.Min(range * tun.KiteRangeFraction, sightCap));
                }
                else if (move == Act.CloseIn) ac.EdgeToEdgeFollowRadius = tun.CloseInDistance;
                return ac;
            default:
                Stop(ship, t);
                return null;
        }
    }

    /// <summary>
    /// Spec 415: the vanilla attack rotation options. With a rotation target, the ship's world rotation is the direction to the target
    /// plus Rotation (BaseFollowCommand.GetWorldRotation, FollowAngleRelative). The nose is rotation 0; y points down on screen,
    /// so +90° puts the target on the left side.
    /// </summary>
    static void Face(AttackCommand ac, Side side)
    {
        float offset = side switch
        {
            Side.Nose => 0f,
            Side.BroadsideLeft => MathF.PI / 2f,
            Side.BroadsideRight => -MathF.PI / 2f,
            Side.Stern => MathF.PI,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, null),
        };
        ac.RotationMode = FollowRotationMode.FollowAngleRelative;
        ac.RotationTarget = ac.Target;
        ac.Rotation = new Direction(offset);
    }

    /// <summary>
    /// Spec 414: flies to the nearest tech crate on radar and gives the vanilla crew collect order (as SalvageJob.OnPartSalvaged does
    /// for salvage) for every tech crate within the crew's safe distance of the hull, the limit of the vanilla collect tool.
    /// </summary>
    static void Collect(Ship ship, ShipTactics t, ShipAiTunables tun, View view)
    {
        var nearest = view.NearestCrate ?? throw new InvalidOperationException("CollectTech ran without a crate");
        if (ship.DetWorldBoundingCircle.DistanceTo(nearest.DetWorldLocation) > tun.CollectDistance) MoveTo(ship, t, nearest.DetWorldLocation, tun.CollectDistance * 0.5f);
        else Stop(ship, t);
        foreach (var crate in view.Crates)
            if (ship.DetWorldBoundingCircle.DistanceTo(crate.DetWorldLocation) < GameApp.Rules.Crew.LoseHomeShipWarningDistance) PickUp(ship, crate);
    }

    // ---- Autonomous ship actions (spec 1067 to 1075). AutonomyWorld does the sim work.

    /// <summary>Spec 1068: flies so that the first Funnel passes over the nearest accepted loot, and gives the collect order.</summary>
    static void CollectLoot(Ship ship, ShipTactics t, View view)
    {
        var nearest = view.NearestLoot ?? throw new InvalidOperationException("CollectLoot ran without loot");
        // The nugget drifts; a new order only when the aim point moved more than a quarter tile.
        MoveTo(ship, t, Autonomy.AutonomyWorld.FunnelAim(ship, nearest.DetWorldLocation), ship.Scale.X * 0.25f);
        Autonomy.AutonomyWorld.OrderCollect(ship, view.Loot);
    }

    /// <summary>Spec 1069: flies next to the picked station, DockDistance from its hull, and holds there.</summary>
    static void GoToStation(Ship ship, ShipTactics t, View view, StationPick pick)
    {
        var station = view.Station(pick) ?? throw new InvalidOperationException("GoToStation ran without a station");
        float dock = view.Autonomy.DockDistance;
        Vector2 dir = Vector2.FromAngle(station.Center.DirectionTo(ship.DetWorldCenter));
        var spot = station.Center + dir * (station.Radius + ship.WorldBoundingRadius + dock);
        MoveTo(ship, t, spot, dock * 0.5f);
    }

    /// <summary>
    /// Spec 1070: flies to the nearest asteroid with ore of the mine list and turns the nose to it at MineDistance, then gives salvage
    /// orders for its matching deposit parts. An asteroid in stasis gets a move order to its place; it turns live as the ship comes near.
    /// </summary>
    static void Mine(Ship ship, ShipTactics t, View view)
    {
        var rock = view.Asteroid ?? throw new InvalidOperationException("Mine ran without an asteroid");
        if (rock.Live is not { } live)
        {
            MoveTo(ship, t, rock.Center, view.Autonomy.MineDistance);
            return;
        }
        WorkOn(ship, t, view, live, view.Cargo);
    }

    /// <summary>Spec 1072: like mine, on the nearest wreck on radar, for every part.</summary>
    static void SalvageWreck(Ship ship, ShipTactics t, View view) =>
        WorkOn(ship, t, view, view.Wreck ?? throw new InvalidOperationException("Salvage ran without a wreck"), null);

    static void WorkOn(Ship ship, ShipTactics t, View view, Ship target, Autonomy.CargoSettings? mine)
    {
        var tun = view.Autonomy;
        Vector2 dir = Vector2.FromAngle(target.DetWorldCenter.DirectionTo(ship.DetWorldCenter));
        var spot = target.DetWorldCenter + dir * (target.WorldBoundingRadius + ship.WorldBoundingRadius + tun.MineDistance);
        var face = ship.DetWorldCenter.DirectionTo(target.DetWorldCenter);
        if (Current(ship, t) is not MoveCommand { Location: { } at } || at.DistanceTo(in spot) > tun.MineDistance * 0.5f)
            Issue(ship, t, new MoveCommand(ship, ship.Commands.AllocGlobalCommandID(), spot, face, CollisionAvoidanceFlags.All, isTentative: false));
        if (HunterOrders.EdgeDistance(ship, target) <= tun.MineDistance * 3f) Autonomy.AutonomyWorld.OrderSalvage(ship, target, mine, tun.SalvageJobs);
    }

    /// <summary>Spec 1073, 1074, 1067: the trade row runs at once each AI tick.</summary>
    static void Trade(Run run, Ship ship, ShipTactics t, View view, Act act)
    {
        switch (act)
        {
            case Act.Sell:
                Autonomy.AutonomyWorld.Sell(ship, view.InRange ?? throw new InvalidOperationException("Sell ran out of range"), view.Cargo);
                break;
            case Act.Buy:
                Autonomy.AutonomyWorld.Buy(ship, view.InRange ?? throw new InvalidOperationException("Buy ran out of range"), view.Cargo, run.T.Autonomy.BuyReserve);
                break;
            case Act.Unload:
                Autonomy.AutonomyWorld.Unload(ship, view.CrewedInRange ?? throw new InvalidOperationException("Unload ran without a crewed ship"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(act), act, "not a trade action");
        }
    }

    /// <summary>Same order as SalvageJob.OnPartSalvaged._CollectNugget: crew jobs for the part of the crate that no crew fetches yet.</summary>
    static void PickUp(Ship ship, Nugget n)
    {
        int left = n.Quantity - ship.Resources.GetAnticipatedPickUp(n, MPValueType.Confirmed);
        int room = TechRoom(ship);
        while (left > 0 && room > 0)
        {
            var sink = ship.Resources.GetBestReceivingSink(n, left, MPValueType.Confirmed, out int capacity);
            if (sink is null) break;
            int amount = Math.Min(left, Math.Min(capacity, room));
            if (amount <= 0) break;
            var job = ship.Resources.ManualResourceTransfer(n, sink, amount, sticky: false, TradeType.None, 0, autoCancel: true, confirmed: true, ship.Jobs.AllocGlobalJobID());
            job.PlayerTimeIssued = App.Clock.Time;
            left -= amount;
            room -= amount;
        }
    }

    static int TechRoom(Ship ship) => ship.Resources.GetRemainingCapacity(Vaults.TechId, includeUntypedStorages: true, includeStored: true,
        includeCarried: true, includeOffShipCarried: false, includeAnticipatedDeliveries: true);

    /// <summary>The tactics' own command if it is still in the queue. The game removes a command when it ends or turns invalid.</summary>
    static Command? Current(Ship ship, ShipTactics t) => t.Issued is { } id ? ship.Commands.GetConfirmedCommand(id) : null;

    static void Issue(Ship ship, ShipTactics t, Command command)
    {
        ship.Commands.EnqueueCommand(command, append: false, confirmed: true);
        t.Issued = command.UniqueID;
    }

    static void Stop(Ship ship, ShipTactics t)
    {
        if (Current(ship, t) is { } c) ship.Commands.RemoveCommand(c);
        t.Issued = null;
    }

    static AttackCommand Attack(Ship ship, ShipTactics t, Ship target, bool fresh)
    {
        if (!fresh && Current(ship, t) is AttackCommand ac && ac.Target == target) return ac;
        var command = new AttackCommand(ship, ship.Commands.AllocGlobalCommandID(), target, CollisionAvoidanceFlags.All, isTentative: false, isConfirmed: true);
        Issue(ship, t, command);
        return command;
    }

    static void MoveTo(Ship ship, ShipTactics t, Vector2 location, float tolerance)
    {
        // The game keeps the last move in the queue after it arrives, so the ship holds the spot against drift.
        if (Current(ship, t) is MoveCommand { Location: { } at } && at.DistanceTo(in location) <= tolerance) return;
        Issue(ship, t, new MoveCommand(ship, ship.Commands.AllocGlobalCommandID(), location, null, CollisionAvoidanceFlags.All, isTentative: false));
    }

    static void Follow(Ship ship, ShipTactics t, Ship who)
    {
        if (Current(ship, t) is FollowCommand f && f.Target == who) return;
        Issue(ship, t, new FollowCommand(ship, ship.Commands.AllocGlobalCommandID(), who, CollisionAvoidanceFlags.All, isTentative: false));
    }

    /// <summary>Runs from the nearest enemy until it is distance away, edge to edge, then stops. Player crews repair on their own.</summary>
    static void RunFrom(Ship ship, ShipTactics t, View view, float distance)
    {
        if (view.NearestEnemy is not { } threat || HunterOrders.EdgeDistance(ship, threat) >= distance)
        {
            Stop(ship, t);
            return;
        }
        Vector2 away = Vector2.FromAngle(threat.DetWorldCenter.DirectionTo(ship.DetWorldCenter));
        var spot = threat.DetWorldCenter + away * (distance * 1.25f + threat.WorldBoundingRadius + ship.WorldBoundingRadius);
        MoveTo(ship, t, spot, distance * 0.25f);
    }

    /// <summary>
    /// Flee and jump: a local FTL jump to the friendly ship that is furthest from the nearest enemy, when the vanilla jump rules allow it
    /// (FTL drive, fuel, distance, no nebula block). False when no jump is possible; the ship then flees on its thrusters.
    /// </summary>
    static bool TryJump(Ship ship, ShipTactics t, View view)
    {
        if (Current(ship, t) is FtlLocalJumpCommand) return true;
        if (!ship.Ftl.HasFtlDrives || view.NearestEnemy is not { } threat) return false;
        Ship? best = null;
        float bestDistance = -1f;
        foreach (var ally in view.Allies)
        {
            if (!ally.IsUsableFtlPointFor(ship.Metadata.PlayerIndex)) continue;
            float d = ally.DetWorldCenter.DistanceTo(threat.DetWorldCenter);
            if (d > bestDistance) { best = ally; bestDistance = d; }
        }
        if (best is null) return false;
        var jump = new FtlLocalJumpCommand(ship, ship.Commands.AllocGlobalCommandID(), best.UniqueID);
        if (!jump.IsValid()) return false;
        Issue(ship, t, jump);
        return true;
    }

    // ---- Weapons (spec 404).

    static readonly HashSet<int> NoGroups = [];

    /// <summary>Part control groups 1 to 10 of each part (vanilla keys 1 to 0 select them).</summary>
    static Dictionary<Part, HashSet<int>> PartGroups(Ship ship)
    {
        var map = new Dictionary<Part, HashSet<int>>();
        var found = new List<Part>();
        for (int g = 1; g <= ShipAiModel.MaxGroup; g++)
        {
            found.Clear();
            ship.Metadata.GetPartsInControlGroup(g - 1, found);
            foreach (var part in found)
            {
                if (!map.TryGetValue(part, out var set)) map[part] = set = [];
                set.Add(g);
            }
        }
        return map;
    }

    static HashSet<int> GroupsOf(Weapon weapon, Dictionary<Part, HashSet<int>> groups) =>
        weapon.Part is { } p && groups.TryGetValue(p, out var set) ? set : NoGroups;

    static bool Holds(Act act, Weapon weapon) => act == Act.HoldFire || (act == Act.PointDefenseOnly && !weapon.Rules.IsDefensiveOnly);

    /// <summary>The weapons that may not fire now. FirePermissionPatch builds it again after a load.</summary>
    internal static HashSet<Weapon> BuildHeld(Ship ship, ShipTactics t)
    {
        var tun = Run.Current?.T.ShipAi ?? throw new InvalidOperationException("ship tactics need a run");
        var groups = PartGroups(ship);
        var rows = t.Rows;
        var matched = t.MatchedRows;
        var held = new HashSet<Weapon>();
        foreach (var weapon in ship.Weapons)
            if (Holds(ShipAiModel.WeaponAct(rows, matched, GroupsOf(weapon, groups), IsDisruptor(weapon, tun)), weapon)) held.Add(weapon);
        return held;
    }

    /// <summary>
    /// Spec 404, 414. Each weapon follows its weapons row. Disruptors aim at the nearest enemy that calls for help, else at the target.
    /// </summary>
    static void Weapons(Ship ship, ShipTactics t, ShipAiTunables tun, IReadOnlyList<TacticRow> rows, int beforeMask, int mask, Ship? target, Ship? caller)
    {
        var groups = PartGroups(ship);
        bool[] before = Bits(beforeMask, rows.Count), now = Bits(mask, rows.Count);
        var held = new HashSet<Weapon>();
        var picks = new Dictionary<(Act, Ship), Part?>();
        foreach (var weapon in ship.Weapons)
        {
            var g = GroupsOf(weapon, groups);
            bool disruptor = IsDisruptor(weapon, tun);
            var act = ShipAiModel.WeaponAct(rows, now, g, disruptor);
            if (Holds(act, weapon)) held.Add(weapon);
            Ship? aimAt = act == Act.FireDisruptors ? caller ?? target : target;
            // Point defense keeps its own targets: it shoots missiles and mines, not parts.
            if (ShipAiModel.AimsAtParts(act) && aimAt is not null && !weapon.Rules.IsDefensiveOnly) Aim(ship, weapon, aimAt, act, picks);
            else if (ShipAiModel.AimsAtParts(ShipAiModel.WeaponAct(rows, before, g, disruptor))) ClearAim(ship, weapon);
        }
        t.Held = held;
    }

    static string[]? _disruptorSource;
    static string[]? _disruptorAmmoSource;
    static HashSet<ID<PartRules>> _disruptorParts = [];
    static HashSet<ID<ResourceRules>> _disruptorAmmo = [];

    /// <summary>Spec 414: a weapon of a DisruptorParts part, or of a part that stores DisruptorAmmo now (its active missile type).</summary>
    static bool IsDisruptor(Weapon weapon, ShipAiTunables tun)
    {
        if (weapon.Part is not { } part) return false;
        if (!ReferenceEquals(_disruptorSource, tun.DisruptorParts) || !ReferenceEquals(_disruptorAmmoSource, tun.DisruptorAmmo))
        {
            _disruptorParts = tun.DisruptorParts.Select(id => new ID<PartRules>(id)).ToHashSet();
            _disruptorAmmo = tun.DisruptorAmmo.Select(id => new ID<ResourceRules>(id)).ToHashSet();
            (_disruptorSource, _disruptorAmmoSource) = (tun.DisruptorParts, tun.DisruptorAmmo);
        }
        if (_disruptorParts.Contains(part.Rules.ID)) return true;
        foreach (var c in part.Components)
            if (c is BaseResourceStorage rs && _disruptorAmmo.Contains(rs.ResourceType)) return true;
        return false;
    }

    static bool[] Bits(int mask, int count) => Enumerable.Range(0, count).Select(i => (mask & (1 << i)) != 0).ToArray();

    /// <summary>Same assignment as AIValueTargeterModule.TargetWeapons. A target that the player gave the weapon stays.</summary>
    static void Aim(Ship ship, Weapon weapon, Ship target, Act act, Dictionary<(Act, Ship), Part?> picks)
    {
        var current = weapon.ExplicitTargets.CurConfirmedTarget;
        if (current is not null && current.UniqueID.PlayerID is not null) return;
        // The current part stays while it lives, so the guns do not jump between parts as the ships turn.
        if (current?.Target is Part p && p.Ship == target && Fits(p.Rules, act)) return;
        if (!picks.TryGetValue((act, target), out var part)) picks[(act, target)] = part = PickPart(ship, target, act);
        if (part is null || weapon.Rules.IsShipWideExplicitTargetingSuppressed(ship, part)) return;
        var wt = new WeaponTarget(weapon, part, ship.Weapons.AllocGlobalTargetID()) { PlayerTimeGiven = App.Clock.Time };
        weapon.ExplicitTargets.EnqueueTarget(wt, append: false, confirmed: true);
    }

    /// <summary>Removes a part target on another ship that the sim gave (no player ID). Targets that the player set stay.</summary>
    static void ClearAim(Ship ship, Weapon weapon)
    {
        if (weapon.ExplicitTargets.CurConfirmedTarget is { } c && c.UniqueID.PlayerID is null && c.Target is Part p && p.Ship != ship)
            weapon.ExplicitTargets.RemoveTarget(c);
    }

    static bool Fits(PartRules rules, Act act) => act switch
    {
        Act.TargetWeapons => KindsOf(rules).Contains(PartKind.Weapon),
        Act.TargetEngines => KindsOf(rules).Contains(PartKind.Thruster),
        _ => true,
    };

    /// <summary>Focus: the part that the vanilla AI values most (AIValueFactor × cost). Weapons, engines: the nearest such part.</summary>
    static Part? PickPart(Ship ship, Ship target, Act act)
    {
        Vector2 from = ship.DetWorldCenter;
        IEnumerable<Part?> candidates = act switch
        {
            Act.FocusTarget or Act.FireDisruptors => [target.Parts.MaxBy(p => p.Rules.AIValueFactor * p.Rules.Cost)],
            Act.TargetWeapons => target.Weapons.Select(w => w.Part),
            Act.TargetEngines => target.Thrusters.Select(th => th.Part),
            _ => throw new ArgumentOutOfRangeException(nameof(act), act, null),
        };
        return candidates.OfType<Part>().Where(p => p.Ship == target).MinBy(p => from.DistanceTo(p.DetWorldCenter));
    }

    // ---- Part kinds, from the part rules (cached: rules do not change while the game runs).

    static readonly ConcurrentDictionary<PartRules, HashSet<PartKind>> Kinds = new();

    internal static HashSet<PartKind> KindsOf(PartRules rules) => Kinds.GetOrAdd(rules, r =>
    {
        var set = new HashSet<PartKind>();
        if (r.ID == Vaults.VaultId) set.Add(PartKind.Vault);
        AddKinds(r.Components, set);
        return set;
    });

    static void AddKinds(IEnumerable<PartComponentRules> components, HashSet<PartKind> set)
    {
        foreach (var c in components)
        {
            switch (c)
            {
                case WeaponRules: set.Add(PartKind.Weapon); break;
                case ThrusterRules: set.Add(PartKind.Thruster); break;
                case ArcShieldRules: set.Add(PartKind.Shield); break;
                case PartToggledComponentsRules toggled: AddKinds(toggled.Components, set); break;
            }
        }
    }

    static IReadOnlySet<ID<ResourceRules>>? _ammoFor;
    static ShipRules? _ammoRules;

    /// <summary>Ammo: the supplies that parts burn (Ux LowSupplies.Watched), without battery, FTL fuel and stolen tech.</summary>
    static IReadOnlySet<ID<ResourceRules>> AmmoIds(ShipRules rules)
    {
        if (rules == _ammoRules && _ammoFor is not null) return _ammoFor;
        if (!GameApp.Rules.HasResourceRules(Battery)) throw new InvalidOperationException($"resource '{Battery}' is not in the game rules");
        var set = new HashSet<ID<ResourceRules>>(Ux.LowSupplies.Watched(rules));
        set.Remove(Battery);
        set.Remove(GameApp.Rules.FtlFuelResource);
        set.Remove(Vaults.TechId);
        (_ammoRules, _ammoFor) = (rules, set);
        return set;
    }

    static float? Fraction(Ship ship, IEnumerable<ID<ResourceRules>> ids)
    {
        long have = 0, cap = 0;
        foreach (var id in ids)
        {
            have += ship.Resources.GetResourceTotal(id);
            cap += ship.Resources.GetCapacityTotal(id, includeImplicit: true, includeUntyped: false);
        }
        return cap > 0 ? (float)have / cap : null;
    }

    static Ship? AttackTarget(Ship s) => (s.Commands.CurConfirmedCommand as IAttackCommand)?.Target;

    static bool HasVault(Ship s) => Vaults.VaultParts(s).Any();

    /// <summary>What one ship sees in one tick: enemies on its team's radar, enemies with an attack order, and friendly human ships.</summary>
    sealed class View
    {
        readonly Run _run;
        readonly Ship _self;
        readonly List<Ship> _enemies = [];
        readonly List<Ship> _attackers = [];
        public readonly List<Ship> Allies = [];
        /// <summary>Tech crates that this ship's team sees, in nugget order.</summary>
        public readonly List<Nugget> Crates = [];
        readonly Dictionary<Act, Ship?> _targets = [];
        /// <summary>The conditions that the rows use: costly facts (loot, stations, asteroids, wrecks) are only read for them.</summary>
        readonly HashSet<Cond> _uses;
        public readonly Autonomy.CargoSettings Cargo;
        public Autonomy.AutonomyTunables Autonomy => _run.T.Autonomy;

        public View(Run run, Ship self, ShipTactics t)
        {
            _run = run;
            _self = self;
            Cargo = t.Cargo;
            _uses = t.Rows.SelectMany(r => r.Terms).Select(term => term.Cond).ToHashSet();
            var sim = HunterOrders.Sim(self);
            for (int i = 0; i < sim.Nuggets.Count; i++)
            {
                var n = sim.Nuggets[i];
                if (n.ResourceType == Vaults.TechId && n.IsWithinSightOfTeam(self.Metadata.Team)) Crates.Add(n);
            }
            var ships = sim.Ships;
            for (int i = 0; i < ships.Count; i++)
            {
                var other = ships[i];
                if (other == self || other.IsJunk || !other.IsSignificant) continue;
                if (self.IsEnemiesWith(other))
                {
                    if (other.IsWithinRadarOfTeam(self.Metadata.Team)) _enemies.Add(other);
                    if (AttackTarget(other) is not null) _attackers.Add(other);
                }
                else if (other.IsHumanPlayerShip) Allies.Add(other);
            }
        }

        float Distance(Ship other) => HunterOrders.EdgeDistance(_self, other);

        public Ship? NearestEnemy => _enemies.MinBy(Distance);

        public Ship? NearestAlly => Allies.MinBy(Distance);

        float CrateDistance(Nugget n) => _self.DetWorldBoundingCircle.DistanceTo(n.DetWorldLocation);

        public Nugget? NearestCrate => Crates.MinBy(CrateDistance);

        /// <summary>Spec 413: an enemy on radar that holds tech it took from a crate (Tech.HunterHeld).</summary>
        bool HoldsTech(Ship enemy) => _run.State.Tech.HunterHeld.Any(h => h.Ship == enemy.UniqueID && h.Units > 0);

        /// <summary>Spec 413: an enemy on radar whose call for help runs (Ai.CallStrategy).</summary>
        static bool Calls(Ship enemy) => enemy.AI?.GetStrategyModule<CallStrategy>() is { Calling: true };

        public Ship? Caller => _enemies.Where(Calls).MinBy(Distance);

        /// <summary>The first ship of this ship's player (lowest ID). Null when that is this ship. Spec 1071: an autonomous ship follows the first crewed ship.</summary>
        public Ship? Leader
        {
            get
            {
                if (HotCargo.Autonomy.Autonomous.IsAutonomous(_self)) return HotCargo.Autonomy.Autonomous.FirstCrewed(_self);
                var leader = Allies.Where(a => a.Metadata.PlayerIndex == _self.Metadata.PlayerIndex).Append(_self).MinBy(s => s.UniqueID);
                return leader == _self ? null : leader;
            }
        }

        public ShipFacts Facts()
        {
            long shield = 0, shieldMax = 0;
            foreach (var part in _self.Parts)
                foreach (var c in part.Components)
                    if (c is ArcShield { ResourceStorage: { } rs }) { shield += rs.Resources; shieldMax += rs.MaxResources; }
            var lost = new HashSet<PartKind>();
            foreach (var dp in _self.DestroyedParts) lost.UnionWith(KindsOf(dp.Rules));
            var classes = new HashSet<ShipClass>();
            foreach (var e in _enemies)
                classes.Add(e.Thrusters.Count == 0 ? ShipClass.Station : e.BaseBlueprintCost >= _self.BaseBlueprintCost ? ShipClass.Large : ShipClass.Small);
            bool vaultHit = lost.Contains(PartKind.Vault) || Vaults.VaultParts(_self).Any(p => p.Health < p.Rules.MaxHealth);
            return new ShipFacts(
                Health: HunterOrders.HealthFraction(_self),
                Shields: shieldMax > 0 ? (float)shield / shieldMax : null,
                LostParts: lost,
                NearestEnemy: NearestEnemy is { } n ? Distance(n) : float.PositiveInfinity,
                TargetedByEnemy: _attackers.Any(a => AttackTarget(a) == _self),
                AllyUnderAttack: _attackers.Any(a => AttackTarget(a) is { } v && v != _self && Allies.Contains(v)),
                Ammo: Fraction(_self, AmmoIds(_self.Rules)),
                Battery: Fraction(_self, [Battery]),
                EnemyClasses: classes,
                Enemies: _enemies.Count,
                CarryingTech: _self.Resources.GetResourceTotal(Vaults.TechId) > 0,
                VaultDamaged: vaultHit,
                HeatStage: _run.State.StagesStarted,
                HuntersInSystem: HunterSpawner.Hunters(HunterOrders.Sim(_self)).Any(),
                StrikeIn: _run.SecondsToStrikeWave is { } s ? (float)s : null,
                LossTimerRunning: LossTimer.IsRunning(_run),
                NearestCrate: NearestCrate is { } crate ? CrateDistance(crate) : float.PositiveInfinity,
                EnemyHoldsTech: _enemies.Any(HoldsTech),
                EnemyCalling: Caller is not null,
                Cargo: HotCargo.Autonomy.AutonomyWorld.CargoFill(_self),
                CargoToSell: _uses.Contains(Cond.CargoToSell) && HotCargo.Autonomy.AutonomyWorld.HasCargoToSell(_self, Cargo),
                NearestLoot: _uses.Contains(Cond.LootWithin) && NearestLoot is { } loot ? LootDistance(loot) : float.PositiveInfinity,
                StationInRange: _uses.Contains(Cond.StationInRange) && InRange is not null,
                NearestAsteroid: _uses.Contains(Cond.AsteroidWithin) && Asteroid is { } rock ? EdgeTo(rock.Center, rock.Radius) : float.PositiveInfinity,
                NearestWreck: _uses.Contains(Cond.WreckWithin) && Wreck is { } wreck ? Distance(wreck) : float.PositiveInfinity,
                NearestFleet: _uses.Contains(Cond.FleetBeyond) && NearestCrewed is { } home ? Distance(home) : float.PositiveInfinity);
        }

        // ---- Autonomous ship facts (spec 1066). Each one is read once per tick, when a row needs it.

        float EdgeTo(Vector2 center, float radius) => Math.Max(0f, _self.DetWorldBoundingCircle.DistanceTo(center) - radius);

        float LootDistance(Nugget n) => _self.DetWorldBoundingCircle.DistanceTo(n.DetWorldLocation);

        List<Nugget>? _loot;
        public List<Nugget> Loot => _loot ??= HotCargo.Autonomy.AutonomyWorld.AcceptedLoot(_self, Cargo);
        public Nugget? NearestLoot => Loot.MinBy(LootDistance);

        readonly Dictionary<StationPick, HotCargo.Autonomy.StationRef?> _stations = [];
        public HotCargo.Autonomy.StationRef? Station(StationPick pick)
        {
            if (!_stations.TryGetValue(pick, out var s)) _stations[pick] = s = HotCargo.Autonomy.AutonomyWorld.PickStation(_self, pick, Cargo);
            return s;
        }

        bool _inRangeRead;
        Ship? _inRange;
        public Ship? InRange
        {
            get
            {
                if (!_inRangeRead) (_inRange, _inRangeRead) = (HotCargo.Autonomy.AutonomyWorld.StationInRange(_self), true);
                return _inRange;
            }
        }

        bool _asteroidRead;
        HotCargo.Autonomy.AsteroidRef? _asteroid;
        public HotCargo.Autonomy.AsteroidRef? Asteroid
        {
            get
            {
                if (!_asteroidRead) (_asteroid, _asteroidRead) = (HotCargo.Autonomy.AutonomyWorld.NearestAsteroid(_self, Cargo), true);
                return _asteroid;
            }
        }

        bool _wreckRead;
        Ship? _wreck;
        public Ship? Wreck
        {
            get
            {
                if (!_wreckRead) (_wreck, _wreckRead) = (HotCargo.Autonomy.AutonomyWorld.NearestWreck(_self), true);
                return _wreck;
            }
        }

        /// <summary>The owner's nearest crewed ship in this system.</summary>
        public Ship? NearestCrewed => Allies.Where(a => a.Metadata.PlayerIndex == _self.Metadata.PlayerIndex && HotCargo.Autonomy.Autonomous.IsCrewed(a))
            .OrderBy(Distance).ThenBy(a => a.UniqueID).FirstOrDefault();

        public Ship? CrewedInRange => HotCargo.Autonomy.AutonomyWorld.CrewedInRange(_self);

        bool CargoRoom => HotCargo.Autonomy.AutonomyWorld.CargoFill(_self) is { } fill && fill < 0.999f;

        public bool CanRun(TacticRow row) => ShipAiModel.KindOf(row.Act) switch
        {
            ActKind.Target => TargetFor(row.Act) is not null,
            // A facing row turns an attack: it needs an enemy.
            ActKind.Facing => _enemies.Count > 0,
            ActKind.Move => row.Act switch
            {
                Act.Kite or Act.CloseIn => _enemies.Count > 0,
                Act.RetreatToAlly => NearestAlly is not null,
                Act.FollowLeader => Leader is not null,
                Act.Guard => GuardTarget((GuardWho)row.ActParam) is not null,
                Act.CollectTech => Crates.Count > 0 && TechRoom(_self) > 0,
                Act.CollectLoot => Loot.Count > 0 && CargoRoom,
                Act.GoToStation => Station((StationPick)row.ActParam) is not null,
                Act.Mine => Asteroid is not null && CargoRoom,
                Act.Salvage => Wreck is not null,
                _ => true,
            },
            ActKind.Trade => row.Act switch
            {
                Act.Sell => InRange is not null && HotCargo.Autonomy.AutonomyWorld.HasCargoToSell(_self, Cargo),
                Act.Buy => InRange is not null && Cargo.Buy.Length > 0 && CargoRoom,
                Act.Unload => CrewedInRange is not null && HotCargo.Autonomy.AutonomyWorld.CargoFill(_self) is > 0f,
                _ => throw new ArgumentOutOfRangeException(nameof(row), row.Act, "not a trade action"),
            },
            _ => true,
        };

        public Ship? TargetFor(Act act)
        {
            if (_targets.TryGetValue(act, out var cached)) return cached;
            // OrderBy is stable, so ties keep the sim order and every machine picks the same ship.
            Ship? pick = act switch
            {
                Act.TargetNearest => NearestEnemy,
                Act.TargetWeakest => _enemies.OrderBy(HunterOrders.HealthFraction).ThenBy(Distance).FirstOrDefault(),
                Act.TargetStrongest => _enemies.OrderByDescending(e => e.BaseBlueprintCost).ThenBy(Distance).FirstOrDefault(),
                Act.TargetAllyAttacker => _enemies.Where(e => AttackTarget(e) is { } v && v != _self && Allies.Contains(v)).MinBy(Distance),
                Act.TargetVaultAttacker => _enemies.Where(e => AttackTarget(e) is { } v && (v == _self || Allies.Contains(v)) && HasVault(v)).MinBy(Distance),
                Act.TargetTechHolder => _enemies.Where(HoldsTech).MinBy(Distance),
                Act.TargetCaller => Caller,
                _ => throw new ArgumentOutOfRangeException(nameof(act), act, "not a target action"),
            };
            _targets[act] = pick;
            return pick;
        }

        public Ship? GuardTarget(GuardWho who) => who switch
        {
            GuardWho.Flagship => Leader,
            GuardWho.VaultShip => Allies.Where(HasVault).MinBy(Distance),
            GuardWho.WeakestAlly => Allies.OrderBy(HunterOrders.HealthFraction).ThenBy(Distance).FirstOrDefault(),
            _ => throw new ArgumentOutOfRangeException(nameof(who), who, null),
        };

        public Ship? AttackerOf(Ship guarded) => _enemies.Where(e => AttackTarget(e) == guarded).MinBy(Distance);
    }
}

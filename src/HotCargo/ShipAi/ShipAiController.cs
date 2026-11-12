using System.Collections.Concurrent;
using Cosmoteer;
using Cosmoteer.Data;
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
            Errors.Guard("ship tactics", () => Think(ship, t, tun));
        }
    }

    /// <summary>The tactics input (spec 409). Clears the old decision, so the new rows decide from scratch at the next tick.</summary>
    internal static void Set(Ship ship, string text, bool auto, SimRoot sim)
    {
        ShipAiModel.Decode(text);
        var t = ShipTactics.For(ship);
        Release(ship, t);
        t.SetRows(text);
        t.Auto = auto;
        t.NextTick = sim.LogicalTime;
        Log.Info($"ship tactics: {ship.Metadata.PlainShipName} auto={auto} rows={t.Rows.Count} {text}");
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

    static void Think(Ship ship, ShipTactics t, ShipAiTunables tun)
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
        var view = new View(ship);
        var d = ShipAiModel.Evaluate(rows, view.Facts(), view.CanRun);
        Ship? target = d.TargetRow >= 0 ? view.TargetFor(rows[d.TargetRow].Act) : null;
        Act? move = d.MoveRow >= 0 ? rows[d.MoveRow].Act : null;
        Ship? guarded = move == Act.Guard ? view.GuardTarget((GuardWho)rows[d.MoveRow].ActParam) : null;
        // A guard with no target row attacks whoever attacks the guarded ship.
        if (target is null && guarded is not null) target = view.AttackerOf(guarded);
        Act? before = t.MoveRow >= 0 && t.MoveRow < rows.Count ? rows[t.MoveRow].Act : null;
        Move(ship, t, tun, view, move, target, guarded, fresh: before != move);
        int mask = 0;
        for (int i = 0; i < d.Matched.Length; i++) if (d.Matched[i]) mask |= 1 << i;
        Weapons(ship, t, rows, t.Matched, mask, target);
        if (mask != t.Matched || d.TargetRow != t.TargetRow || d.MoveRow != t.MoveRow || target?.UniqueID != t.Target)
            Trace(ship, tun, $"target row {d.TargetRow} ({target?.Metadata.PlainShipName ?? "-"}) move row {d.MoveRow} ({move?.ToString() ?? "-"}) matched {Convert.ToString(mask, 2)}");
        t.TargetRow = d.TargetRow;
        t.MoveRow = d.MoveRow;
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
            foreach (var weapon in ship.Weapons)
                if (ShipAiModel.AimsAtParts(ShipAiModel.WeaponAct(rows, matched, GroupsOf(weapon, groups)))) ClearAim(ship, weapon);
        }
        t.Matched = 0;
        t.TargetRow = t.MoveRow = -1;
        t.Target = null;
        t.Anchor = null;
        t.Held = null;
        t.Waiting = false;
    }

    // ---- Movement and target: one vanilla command at a time.

    static void Move(Ship ship, ShipTactics t, ShipAiTunables tun, View view, Act? move, Ship? target, Ship? guarded, bool fresh)
    {
        if (move != Act.HoldPosition) t.Anchor = null;
        switch (move)
        {
            case Act.HoldPosition:
                t.Anchor ??= ship.DetWorldCenter;
                MoveTo(ship, t, t.Anchor.Value, tun.HoldTolerance);
                break;
            case Act.RetreatToAlly:
                Follow(ship, t, view.NearestAlly ?? throw new InvalidOperationException("RetreatToAlly ran without an ally"));
                break;
            case Act.FollowLeader:
                Follow(ship, t, view.Leader ?? throw new InvalidOperationException("FollowLeader ran without a leader"));
                break;
            case Act.Guard when target is not null:
                Attack(ship, t, target, fresh);
                break;
            case Act.Guard:
                Follow(ship, t, guarded ?? throw new InvalidOperationException("Guard ran without a ship to guard"));
                break;
            case Act.RetreatRepair:
                RunFrom(ship, t, view, tun.RetreatDistance);
                break;
            case Act.FleeJump:
                if (!TryJump(ship, t, view)) RunFrom(ship, t, view, tun.RetreatDistance * 2f);
                break;
            case null or Act.Kite or Act.CloseIn when target is not null:
                var ac = Attack(ship, t, target, fresh);
                if (move == Act.Kite && HunterOrders.MaxWeaponRange(ship, onlyActive: true) is var range and > 0f)
                {
                    // Same sight cap as the vanilla attack tactics, so the target stays in sight.
                    float sightCap = ship.Sensors.SightRadius - ship.WorldBoundingRadius * 2f - ac.TargetRadius;
                    ac.EdgeToEdgeFollowRadius = Math.Max(0f, Math.Min(range * tun.KiteRangeFraction, sightCap));
                }
                else if (move == Act.CloseIn) ac.EdgeToEdgeFollowRadius = tun.CloseInDistance;
                break;
            default:
                Stop(ship, t);
                break;
        }
    }

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
        var groups = PartGroups(ship);
        var rows = t.Rows;
        var matched = t.MatchedRows;
        var held = new HashSet<Weapon>();
        foreach (var weapon in ship.Weapons)
            if (Holds(ShipAiModel.WeaponAct(rows, matched, GroupsOf(weapon, groups)), weapon)) held.Add(weapon);
        return held;
    }

    static void Weapons(Ship ship, ShipTactics t, IReadOnlyList<TacticRow> rows, int beforeMask, int mask, Ship? target)
    {
        var groups = PartGroups(ship);
        bool[] before = Bits(beforeMask, rows.Count), now = Bits(mask, rows.Count);
        var held = new HashSet<Weapon>();
        var picks = new Dictionary<Act, Part?>();
        foreach (var weapon in ship.Weapons)
        {
            var g = GroupsOf(weapon, groups);
            var act = ShipAiModel.WeaponAct(rows, now, g);
            if (Holds(act, weapon)) held.Add(weapon);
            // Point defense keeps its own targets: it shoots missiles and mines, not parts.
            if (ShipAiModel.AimsAtParts(act) && target is not null && !weapon.Rules.IsDefensiveOnly) Aim(ship, weapon, target, act, picks);
            else if (ShipAiModel.AimsAtParts(ShipAiModel.WeaponAct(rows, before, g))) ClearAim(ship, weapon);
        }
        t.Held = held;
    }

    static bool[] Bits(int mask, int count) => Enumerable.Range(0, count).Select(i => (mask & (1 << i)) != 0).ToArray();

    /// <summary>Same assignment as AIValueTargeterModule.TargetWeapons. A target that the player gave the weapon stays.</summary>
    static void Aim(Ship ship, Weapon weapon, Ship target, Act act, Dictionary<Act, Part?> picks)
    {
        var current = weapon.ExplicitTargets.CurConfirmedTarget;
        if (current is not null && current.UniqueID.PlayerID is not null) return;
        // The current part stays while it lives, so the guns do not jump between parts as the ships turn.
        if (current?.Target is Part p && p.Ship == target && Fits(p.Rules, act)) return;
        if (!picks.TryGetValue(act, out var part)) picks[act] = part = PickPart(ship, target, act);
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
            Act.FocusTarget => [target.Parts.MaxBy(p => p.Rules.AIValueFactor * p.Rules.Cost)],
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
        readonly Ship _self;
        readonly List<Ship> _enemies = [];
        readonly List<Ship> _attackers = [];
        public readonly List<Ship> Allies = [];
        readonly Dictionary<Act, Ship?> _targets = [];

        public View(Ship self)
        {
            _self = self;
            var ships = HunterOrders.Sim(self).Ships;
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

        /// <summary>The first ship of this ship's player (lowest ID). Null when that is this ship.</summary>
        public Ship? Leader
        {
            get
            {
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
                VaultDamaged: vaultHit);
        }

        public bool CanRun(TacticRow row) => ShipAiModel.KindOf(row.Act) switch
        {
            ActKind.Target => TargetFor(row.Act) is not null,
            ActKind.Move => row.Act switch
            {
                Act.Kite or Act.CloseIn => _enemies.Count > 0,
                Act.RetreatToAlly => NearestAlly is not null,
                Act.FollowLeader => Leader is not null,
                Act.Guard => GuardTarget((GuardWho)row.ActParam) is not null,
                _ => true,
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

using Cosmoteer;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Commands;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;
using Halfling.Timing;
using HotCargo.Ai;
using HotCargo.Core;
using HotCargo.Hunters;
using static HotCargo.Ai.PersonalityModel;
using NVector2 = System.Numerics.Vector2;

namespace HotCargo.Tactics;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcCover")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class CoverStrategyRules : AIStrategyModuleRules
{
    /// <summary>A move is renewed only when its destination moved further than this.</summary>
    [Serialize(Optional = true), PortableHash] public float MoveTolerance = 60f;

    public override AIStrategyModule CreateModule(AIParameters? parameters) => new CoverStrategy(this);
}

/// <summary>
/// Spec 366. A ship with the Cover trait fights from behind rocks. While its target is within CoverEngageRange, it fights for
/// CoverPeekSeconds, then hides behind the nearest rock that blocks weapon fire (asteroids) for CoverHideSeconds, and repeats.
/// The hiding spot is on the far side of the rock from the target, so the rock blocks the shots between them.
/// Tell: the ship ducks behind an asteroid and comes out again to shoot. No label: the plan stays hidden (spec 368).
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class CoverStrategy(CoverStrategyRules rules) : AIStrategyModule
{
    [Serialize(Optional = true)] int _phase;
    [Serialize(Optional = true)] Time _phaseStart;
    [Serialize(Alias = "Target", Optional = true)] Ship? _target;
    [Serialize(Alias = "Rock", Optional = true)] Ship? _rock;
    bool _noRockLogged;

    CoverPhase Phase { get => (CoverPhase)_phase; set => _phase = (int)value; }

    /// <summary>True while the ship moves to or sits in its hiding spot. For the label and the autotest fields.</summary>
    public bool Hiding => Phase == CoverPhase.Hide && _rock is not null && _target is not null;
    public Ship? Rock => _rock;
    public Ship? Target => _target;

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        _phase.GetIntegrityHash(iHash);
        _phaseStart.GetIntegrityHash(iHash);
        (_target?.UniqueID ?? default).GetIntegrityHash(iHash);
        (_rock?.UniqueID ?? default).GetIntegrityHash(iHash);
    }

    public override void OnEnteringStasis(Ship ship) => (_target, _rock, _phase) = (null, null, (int)CoverPhase.Peek);

    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        Time now = HunterOrders.Sim(ship).LogicalTime;
        if (TacticGate.Active(Tactic.Cover, ship) is not { } t) return Reset(now);
        // In the hide phase the ship has a move command, so the target comes from the saved state.
        var target = (ship.Commands.CurConfirmedCommand as AttackCommand)?.Target ?? _target ?? AiGate.NearestHumanEnemy(ship);
        if (target is null || !target.IsLiveWith(ship) || !ship.IsEnemiesWith(target) || !target.IsWithinRadarOfTeam(ship.Metadata.Team)
            || HunterOrders.EdgeDistance(ship, target) > t.CoverEngageRange)
            return Reset(now);
        _target = target;
        var next = NextCoverPhase(Phase, (now - _phaseStart).Seconds, t.CoverPeekSeconds, t.CoverHideSeconds);
        if (next != Phase)
        {
            Phase = next;
            _phaseStart = now;
            if (next == CoverPhase.Peek) TacticsLog.Event(ship, Tactic.Cover, "peeks out to fight");
        }
        if (Phase == CoverPhase.Peek) return false;

        var rock = _rock is { } r && r.IsLiveWith(ship) && IsRock(r) && Usable(ship, target, r, t) ? r : FindRock(ship, target, t);
        if (rock is null)
        {
            if (!_noRockLogged) TacticsLog.Event(ship, Tactic.Cover, "no rock in reach");
            _noRockLogged = true;
            _rock = null;
            return false;
        }
        _noRockLogged = false;
        if (rock != _rock) TacticsLog.Event(ship, Tactic.Cover, $"hides behind a rock (radius {rock.WorldBoundingRadius:0}) from {target.Metadata.PlainShipName}");
        _rock = rock;
        PersonalityStrategy.MoveGuarded(ship, Spot(ship, target, rock, t), rules.MoveTolerance, target, t);
        return true;
    }

    public override AIStrategyModule Clone(AIParameters? parameters) =>
        new CoverStrategy(rules) { _phase = _phase, _phaseStart = _phaseStart, _target = _target, _rock = _rock };

    bool Reset(Time now)
    {
        (_target, _rock, _phase, _phaseStart) = (null, null, (int)CoverPhase.Peek, now);
        return false;
    }

    /// <summary>Spec 366. True when the line between the ship and the target passes through the rock. Reads sim state only.</summary>
    public static bool Covered(Ship ship, Ship target, Ship rock) =>
        SegmentHitsCircle(N(ship.DetWorldCenter), N(target.DetWorldCenter), N(rock.DetWorldCenter), rock.WorldBoundingRadius);

    /// <summary>A rock blocks weapon fire: a junk ship whose rules block line of sight (asteroids and megaroids).</summary>
    public static bool IsRock(Ship ship) => ship.IsJunk && ship.Rules.JunkBlocksWeaponLOS;

    internal static Vector2 Spot(Ship ship, Ship target, Ship rock, TacticsTunables t)
    {
        var spot = CoverSpot(N(target.DetWorldCenter), N(rock.DetWorldCenter), rock.WorldBoundingRadius, ship.WorldBoundingRadius, t.CoverGap);
        return new Vector2(spot.X, spot.Y);
    }

    /// <summary>The rock is big enough and not next to the target, where hiding behind it would mean sitting on the target.</summary>
    internal static bool Usable(Ship ship, Ship target, Ship rock, TacticsTunables t) =>
        rock.WorldBoundingRadius >= ship.WorldBoundingRadius * t.CoverMinRockFactor
        && rock.DetWorldCenter.DistanceTo(target.DetWorldCenter) > rock.WorldBoundingRadius + target.WorldBoundingRadius + ship.WorldBoundingRadius
        && rock.DetWorldCenter.DistanceTo(ship.DetWorldCenter) <= t.CoverSearchRange;

    /// <summary>
    /// The usable rock with the nearest hiding spot. The tree query order is not part of the contract, so the candidates are
    /// sorted by ID first and ties go to the lowest ID.
    /// </summary>
    internal static Ship? FindRock(Ship ship, Ship target, TacticsTunables t)
    {
        var found = new List<Ship>();
        HunterOrders.Sim(ship).Ships.DetFindIntersectingWith(new Circle(ship.DetWorldCenter, t.CoverSearchRange), found, approximate: true, junk: true,
            condition: s => IsRock(s) && Usable(ship, target, s, t));
        found.Sort((a, b) => a.UniqueID.CompareTo(b.UniqueID));
        Ship? best = null;
        float bestDistance = float.MaxValue;
        foreach (var rock in found)
        {
            float d = Spot(ship, target, rock, t).DistanceSquaredTo(ship.DetWorldCenter);
            if (d < bestDistance) { best = rock; bestDistance = d; }
        }
        return best;
    }

    static NVector2 N(Vector2 v) => new(v.X, v.Y);
}

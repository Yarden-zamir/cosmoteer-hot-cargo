using Cosmoteer;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;
using Halfling.Timing;
using HotCargo.Core;
using HotCargo.Hunters;

namespace HotCargo.Tactics;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcBait")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class BaitStrategyRules : AIStrategyModuleRules
{
    /// <summary>A move is renewed only when its destination moved further than this.</summary>
    [Serialize(Optional = true), PortableHash] public float MoveTolerance = 100f;

    public override AIStrategyModule CreateModule(AIParameters? parameters) => new BaitStrategy(this);
}

/// <summary>
/// Spec 48. In a group of two or more allies, the most agile ship with the bait tactic (thrust per mass) is the bait.
/// It flies close to the nearest player ship, then runs back to the middle of its group, and repeats.
/// Tell: one small ship dives in alone and turns back at once. A player that chases it meets the group.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class BaitStrategy(BaitStrategyRules rules) : AIStrategyModule
{
    [Serialize(Optional = true)] int _phase;
    [Serialize(Optional = true)] Time _phaseStart;
    bool _isBait;

    BaitPhase Phase { get => (BaitPhase)_phase; set => _phase = (int)value; }

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        _phase.GetIntegrityHash(iHash);
        _phaseStart.GetIntegrityHash(iHash);
    }

    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        var active = TacticGate.Active(Tactic.Bait, ship);
        var group = active is { } on ? Group(ship, on) : [];
        bool isBait = active is { } a && group.Count >= 2 && Bait(group, a) == ship;
        if (isBait != _isBait) TacticsLog.Event(ship, Tactic.Bait, isBait ? "chosen as bait" : "no longer bait");
        _isBait = isBait;
        var target = Ai.AiGate.NearestHumanEnemy(ship);
        if (!isBait || active is not { } t || target is null)
        {
            Phase = BaitPhase.Approach;
            _phaseStart = HunterOrders.Sim(ship).LogicalTime;
            return false;
        }
        double phaseSeconds = (HunterOrders.Sim(ship).LogicalTime - _phaseStart).Seconds;
        float edge = HunterOrders.EdgeDistance(ship, target);
        var next = TacticsModel.NextBaitPhase(Phase, phaseSeconds, edge, t.BaitCloseDistance, t.BaitApproachSeconds, t.BaitPullBackSeconds);
        if (next != Phase)
        {
            Phase = next;
            _phaseStart = HunterOrders.Sim(ship).LogicalTime;
            TacticsLog.Event(ship, Tactic.Bait, next == BaitPhase.Approach ? "approach" : $"pull back at {edge:0}");
        }
        if (Phase == BaitPhase.Approach)
        {
            Vector2 toShip = Vector2.FromAngle(target.DetWorldCenter.DirectionTo(ship.DetWorldCenter));
            float stop = target.WorldBoundingRadius + ship.WorldBoundingRadius + t.BaitCloseDistance;
            Ai.PersonalityStrategy.MoveGuarded(ship, target.DetWorldCenter + toShip * stop, rules.MoveTolerance, target, t);
        }
        else
        {
            Vector2 center = Vector2.Zero;
            int others = 0;
            foreach (var s in group)
                if (s != ship) { center += s.DetWorldCenter; others++; }
            Ai.PersonalityStrategy.MoveGuarded(ship, center / others, rules.MoveTolerance, target, t);
        }
        return true;
    }

    public override AIStrategyModule Clone(AIParameters? parameters) => new BaitStrategy(rules) { _phase = _phase, _phaseStart = _phaseStart };

    /// <summary>The ship, then its allies within RegroupSearchRange in the stable order of the sim ship list.</summary>
    static List<Ship> Group(Ship ship, TacticsTunables t) =>
        Ai.AiGate.Allies(ship).Where(a => a.DetWorldCenter.DistanceTo(ship.DetWorldCenter) <= t.RegroupSearchRange).Prepend(ship).ToList();

    /// <summary>The most agile group member with the bait tactic on (thrust per mass). Ties go to the lowest ID, so every client picks the same ship.</summary>
    static Ship? Bait(List<Ship> group, TacticsTunables t) =>
        group.Where(s => TacticGate.Active(Tactic.Bait, s) is not null)
            .OrderByDescending(s => Ai.PersonalityStrategy.Profile(s, t).Acceleration).ThenBy(s => s.UniqueID).FirstOrDefault();
}

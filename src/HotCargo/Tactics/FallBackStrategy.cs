using Cosmoteer.Data;
using Cosmoteer;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.AI.StrategyModules;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;
using Halfling.Timing;
using HotCargo.Hunters;

namespace HotCargo.Tactics;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcFallBack")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class FallBackStrategyRules : AIStrategyModuleRules
{
    [Serialize(Optional = true), PortableHash] public int RepairChecksPerTick = 2;
    [Serialize(Optional = true), PortableHash] public HashSet<ID<SimObjectSpawner>>? AvoidableDoodadTags;
    [Serialize(Optional = true), PortableHash] public float AvoidableDoodadBuffer;

    public override AIStrategyModule CreateModule(AIParameters? parameters) => new FallBackStrategy(this);
}

/// <summary>
/// Spec 47. A badly damaged ship turns away, flies clear of the players and repairs, then rejoins the hunt.
/// It must get clear because AI crews cannot build near enemies that fire (CombatDifficulty.EnemyCombatNoBuildRadius).
/// Tell: the ship turns its back and runs, then sits still with repair work on its hull.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class FallBackStrategy : AIStrategyModule
{
    readonly FallBackStrategyRules _rules;
    readonly AISelfRepairModule _repair;
    [Serialize(Optional = true)] bool _active;
    [Serialize(Optional = true)] Time _since;
    [Serialize(Optional = true)] Time _cooldownUntil;

    public FallBackStrategy(FallBackStrategyRules rules)
    {
        _rules = rules;
        // Vanilla repair logic, used only while falling back. Partial work lets crews repair with the resources on board.
        _repair = new AISelfRepairModule(new AISelfRepairModuleRules
        {
            RepairChecksPerTick = rules.RepairChecksPerTick,
            AllowPartialRepair = true,
            AllowPartialRebuild = true,
            AvoidableDoodadTags = rules.AvoidableDoodadTags,
            AvoidableDoodadBuffer = rules.AvoidableDoodadBuffer,
        });
    }

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        _active.GetIntegrityHash(iHash);
        _since.GetIntegrityHash(iHash);
    }

    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        if (TacticGate.Active(Tactic.FallBack) is not { } t)
        {
            _active = false;
            return false;
        }
        Time now = HunterOrders.Sim(ship).LogicalTime;
        float health = HunterOrders.HealthFraction(ship);
        bool wasActive = _active;
        _active = TacticsModel.FallBackActive(_active, health, t.FallBackHealthFraction, t.FallBackRecoverFraction,
            timedOut: now - _since >= t.FallBackMaxSeconds, coolingDown: now < _cooldownUntil);
        if (_active && !wasActive)
        {
            _since = now;
            TacticsLog.Event(ship, Tactic.FallBack, $"start at health {health:0.00}");
        }
        if (!_active)
        {
            if (wasActive)
            {
                _cooldownUntil = now + t.FallBackMaxSeconds;
                TacticsLog.Event(ship, Tactic.FallBack, $"return at health {health:0.00}");
            }
            return false;
        }
        var threat = HunterSpawner.HumanShips(HunterOrders.Sim(ship)).MinBy(h => h.DetWorldCenter.DistanceSquaredTo(ship.DetWorldCenter));
        if (threat is not null && HunterOrders.EdgeDistance(ship, threat) < t.FallBackDistance)
        {
            Vector2 away = Vector2.FromAngle(threat.DetWorldCenter.DirectionTo(ship.DetWorldCenter));
            HunterOrders.MoveTo(ship, threat.DetWorldCenter + away * (t.FallBackDistance * 1.25f + threat.WorldBoundingRadius), t.FallBackDistance * 0.25f);
            return true;
        }
        _repair.OnAITick(ship, ai);
        return true;
    }

    public override AIStrategyModule Clone(AIParameters? parameters) =>
        new FallBackStrategy(_rules) { _active = _active, _since = _since, _cooldownUntil = _cooldownUntil };
}

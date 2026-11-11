using Cosmoteer;
using Cosmoteer.Data;
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
using HotCargo.Tactics;

namespace HotCargo.Ai;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcFlee")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class FleeStrategyRules : AIStrategyModuleRules
{
    /// <summary>True in the hunter AI. A fleeing hunter jumps out of the system; a fleeing vanilla ship repairs and comes back.</summary>
    [Serialize(Optional = true), PortableHash] public bool ForHunters;
    [Serialize(Optional = true), PortableHash] public int RepairChecksPerTick = 2;
    [Serialize(Optional = true), PortableHash] public HashSet<ID<SimObjectSpawner>>? AvoidableDoodadTags;
    [Serialize(Optional = true), PortableHash] public float AvoidableDoodadBuffer;

    public override AIStrategyModule CreateModule(AIParameters? parameters) => new FleeStrategy(this);
}

/// <summary>
/// Spec 193-195. A badly damaged ship may decide to run (one roll each time it drops below the flee health),
/// and a ship without offensive weapons always runs. It keeps away from the nearest enemy.
/// A hunter jumps out of the system after FleeJumpSeconds. A vanilla ship repairs when clear and fights again once recovered.
/// Tell: the ship turns its back, the label shows FLEEING and the jump countdown, and the ship jumps out with the FTL effect.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class FleeStrategy : AIStrategyModule
{
    readonly FleeStrategyRules _rules;
    readonly AISelfRepairModule _repair;
    [Serialize(Optional = true)] bool _fleeing;
    [Serialize(Optional = true)] bool _rolled;
    [Serialize(Optional = true)] Time _since;
    [Serialize(Optional = true)] Time? _removeAt;

    public FleeStrategy(FleeStrategyRules rules)
    {
        _rules = rules;
        _repair = new AISelfRepairModule(new AISelfRepairModuleRules
        {
            RepairChecksPerTick = rules.RepairChecksPerTick,
            AllowPartialRepair = true,
            AllowPartialRebuild = true,
            AvoidableDoodadTags = rules.AvoidableDoodadTags,
            AvoidableDoodadBuffer = rules.AvoidableDoodadBuffer,
        });
    }

    public bool Fleeing => _fleeing;
    public bool JumpsOut => _rules.ForHunters;

    /// <summary>Seconds until a fleeing hunter jumps out, or null. For the overlay label.</summary>
    public double? SecondsToJump(Ship ship, TacticsTunables t) =>
        _fleeing && _rules.ForHunters ? Math.Max(0, t.FleeJumpSeconds - (HunterOrders.Sim(ship).LogicalTime - _since).Seconds) : null;

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        _fleeing.GetIntegrityHash(iHash);
        _rolled.GetIntegrityHash(iHash);
        _since.GetIntegrityHash(iHash);
        (_removeAt ?? Time.Zero).GetIntegrityHash(iHash);
    }

    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        var sim = HunterOrders.Sim(ship);
        Time now = sim.LogicalTime;
        if (_removeAt is { } removeAt)
        {
            // Same removal as AIExitSectorModule: the FTL effect plays, then the ship leaves the sim.
            if (now >= removeAt) sim.EnqueueDeterministic(ship.UniqueID, () => sim.Ships.Remove(ship, dispose: true, handleLostShip: false));
            return true;
        }
        if (AiGate.Active(Behavior.Flee, _rules.ForHunters) is not { } t || AiGate.Surrendered(ship))
        {
            _fleeing = false;
            return false;
        }
        float health = HunterOrders.HealthFraction(ship);
        bool hasWeapons = AiGate.HasOffensiveWeapons(ship);
        bool wasRolled = _rolled;
        var next = AiModel.NextFlee(new FleeState(_fleeing, _rolled), hasWeapons, health, t.FleeHealthFraction, t.FleeRecoverFraction,
            t.FleeChance, canStop: !_rules.ForHunters, () => ship.Rand.Single());
        if (next.Rolled && !wasRolled && !next.Fleeing) TacticsLog.Event(ship, AiArea.FleeStay, $"stays at health {health:0.00}");
        if (next.Fleeing != _fleeing)
        {
            if (next.Fleeing)
            {
                _since = now;
                TacticsLog.Event(ship, AiArea.Flee, $"starts at health {health:0.00} weapons={hasWeapons}");
                AiText.Chat(ship, "FleeFmt");
            }
            else TacticsLog.Event(ship, AiArea.FleeEnd, $"fights again at health {health:0.00}");
        }
        (_fleeing, _rolled) = (next.Fleeing, next.Rolled);
        if (!_fleeing) return false;

        if (_rules.ForHunters && (now - _since).Seconds >= t.FleeJumpSeconds)
        {
            _ = ship.Renderer.AnimateFtlJumpOut(ship.WorldFlightDirection);
            _removeAt = now + ship.Rules.FtlEffects.TotalDuration;
            if (Core.Run.Current is { } run) HunterSpawner.ForgetHeldFuel(run, ship);
            TacticsLog.Event(ship, AiArea.FleeJump, "jumps out of the system");
            AiText.Chat(ship, "JumpedOutFmt");
            return true;
        }
        var threat = AiGate.NearestEnemy(ship);
        if (threat is not null && HunterOrders.EdgeDistance(ship, threat) < t.FleeDistance)
        {
            Vector2 away = Vector2.FromAngle(threat.DetWorldCenter.DirectionTo(ship.DetWorldCenter));
            HunterOrders.MoveTo(ship, threat.DetWorldCenter + away * (t.FleeDistance * 1.25f + threat.WorldBoundingRadius), t.FleeDistance * 0.25f);
            return true;
        }
        // Clear of enemies. AI crews cannot build near enemies that fire, so a vanilla ship repairs only here.
        if (!_rules.ForHunters) _repair.OnAITick(ship, ai);
        return true;
    }

    public override AIStrategyModule Clone(AIParameters? parameters) =>
        new FleeStrategy(_rules) { _fleeing = _fleeing, _rolled = _rolled, _since = _since, _removeAt = _removeAt };
}

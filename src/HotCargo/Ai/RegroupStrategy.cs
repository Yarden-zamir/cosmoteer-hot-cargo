using Cosmoteer;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Commands;
using Halfling.PortableHashing;
using Halfling.Serialization;
using Halfling.Timing;
using HotCargo.Hunters;
using HotCargo.Tactics;

namespace HotCargo.Ai;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcRegroup")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class RegroupStrategyRules : AIStrategyModuleRules
{
    [Serialize(Optional = true), PortableHash] public bool ForHunters;

    public override AIStrategyModule CreateModule(AIParameters? parameters) => new RegroupStrategy(this);
}

/// <summary>
/// Spec 201. A ship that is weaker than its target does not charge alone: it flies to its nearest ally that is still far,
/// until enough allies are close or its time runs out. Then it attacks, and it does not regroup again for the same time.
/// Tell: the ship turns away from the players toward its friends, and the label shows REGROUPING.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class RegroupStrategy(RegroupStrategyRules rules) : AIStrategyModule
{
    [Serialize(Optional = true)] bool _active;
    [Serialize(Optional = true)] Time _since;
    [Serialize(Optional = true)] Time _cooldownUntil;

    public bool Active => _active;

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        _active.GetIntegrityHash(iHash);
        _since.GetIntegrityHash(iHash);
        _cooldownUntil.GetIntegrityHash(iHash);
    }

    public override void OnEnteringStasis(Ship ship) => _active = false;

    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        Time now = HunterOrders.Sim(ship).LogicalTime;
        if (AiGate.Active(Behavior.Regroup, rules.ForHunters) is not { } t || AiGate.Surrendered(ship)) return End(ship, now, null);
        var target = (ship.Commands.CurConfirmedCommand as AttackCommand)?.Target ?? AiGate.NearestHumanEnemy(ship);
        if (target is null || !ship.IsEnemiesWith(target)) return End(ship, now, t);
        var allies = AiGate.Allies(ship).Where(a => a.DetWorldCenter.DistanceTo(ship.DetWorldCenter) <= t.RegroupSearchRange).ToList();
        int near = allies.Count(a => a.DetWorldCenter.DistanceTo(ship.DetWorldCenter) <= t.RegroupRadius);
        bool timedOut = _active && (now - _since).Seconds >= t.RegroupMaxSeconds;
        bool coolingDown = !_active && now < _cooldownUntil;
        // Firepower estimates cost the most, so they run only when the ally counts allow a regroup.
        bool want = allies.Count > near && near + 1 < Math.Min(t.RegroupMinGroup, allies.Count + 1) && !timedOut && !coolingDown
                    && AiModel.ShouldRegroup(ship.Weapons.GetTotalFirePower(), target.Weapons.GetTotalFirePower(), t.RegroupFirepowerRatio,
                        near, allies.Count, t.RegroupMinGroup, timedOut, coolingDown);
        if (!want) return End(ship, now, t);
        if (!_active)
        {
            _active = true;
            _since = now;
            TacticsLog.Event(ship, AiArea.Regroup, $"waits for allies before attacking {target.Metadata.PlainShipName} ({near} of {allies.Count} close)");
        }
        var join = allies.Where(a => a.DetWorldCenter.DistanceTo(ship.DetWorldCenter) > t.RegroupRadius)
            .MinBy(a => a.DetWorldCenter.DistanceSquaredTo(ship.DetWorldCenter))!;
        HunterOrders.MoveTo(ship, join.DetWorldCenter, t.RegroupRadius * 0.5f);
        return true;
    }

    bool End(Ship ship, Time now, TacticsTunables? t)
    {
        if (!_active) return false;
        _active = false;
        if (t is not null) _cooldownUntil = now + t.RegroupMaxSeconds;
        TacticsLog.Event(ship, AiArea.RegroupEnd, "attacks");
        return false;
    }

    public override AIStrategyModule Clone(AIParameters? parameters) =>
        new RegroupStrategy(rules) { _active = _active, _since = _since, _cooldownUntil = _cooldownUntil };
}

using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Commands;
using Halfling.PortableHashing;
using Halfling.Serialization;
using HotCargo.Hunters;

namespace HotCargo.Tactics;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcKite")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class KiteTacticRules : AITacticsModuleRules
{
    [Serialize(Optional = true), PortableHash] public int AttackRotationsToCheck = 8;
    [Serialize(Optional = true), PortableHash] public float AttackRotationSwitchThresholdFactor = 1.25f;

    public override AITacticsModule CreateModule(AIParameters? parameters) => new KiteTactic(this);
}

/// <summary>
/// Spec 44. A ship that outranges its target holds just inside its own range and outside the target's range.
/// Tell: the ship keeps a fixed distance and backs off when the target comes closer.
/// Limit: ranges are compared edge to edge of the bounding circles, so weapons deep inside a big ship
/// reach a little less far than the hold distance assumes. Revisit if kiting ships often fall short of firing.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class KiteTactic(KiteTacticRules rules) : AITacticsModule
{
    bool _kiting;

    public override bool UpdateAttackAngle(AttackCommand ac, ShipAI ai) => false;

    public override bool UpdateAttackRadiusAndRotation(AttackCommand ac, ShipAI ai)
    {
        float ownRange = 0f, targetRange = 0f;
        float? hold = null;
        if (TacticGate.Active(Tactic.Kite, ac.Ship) is { } t)
        {
            ownRange = HunterOrders.MaxWeaponRange(ac.Ship, onlyActive: true);
            targetRange = HunterOrders.MaxWeaponRange(ac.Target, onlyActive: false);
            hold = TacticsModel.KiteEdgeDistance(ownRange, targetRange, t.KiteRangeFraction);
        }
        if (hold is not { } edge)
        {
            if (_kiting) TacticsLog.Event(ac.Ship, Tactic.Kite, "stop");
            _kiting = false;
            return false;
        }
        // Same sight cap as the vanilla default module, so the target stays in sight.
        float sightCap = ac.Ship.Sensors.SightRadius - ac.Ship.WorldBoundingRadius * 2f - ac.TargetRadius;
        ac.EdgeToEdgeFollowRadius = Math.Min(edge, sightCap);
        ac.SetBestAttackRotation(useIdealRange: false, onlyWithResources: true, rules.AttackRotationsToCheck, rules.AttackRotationSwitchThresholdFactor);
        if (!_kiting) TacticsLog.Event(ac.Ship, Tactic.Kite, $"hold {ac.EdgeToEdgeFollowRadius:0} from {ac.Target.Metadata.PlainShipName} (range {ownRange:0} vs {targetRange:0})");
        _kiting = true;
        return true;
    }

    public override AITacticsModule Clone(AIParameters? parameters) => new KiteTactic(rules);
}

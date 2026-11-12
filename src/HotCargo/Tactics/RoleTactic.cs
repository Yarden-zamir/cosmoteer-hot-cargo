using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Commands;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;
using HotCargo.Ai;
using HotCargo.Hunters;

namespace HotCargo.Tactics;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcRole")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class RoleTacticRules : AITacticsModuleRules
{
    [Serialize(Optional = true), PortableHash] public int AttackRotationsToCheck = 8;
    [Serialize(Optional = true), PortableHash] public float AttackRotationSwitchThresholdFactor = 1.25f;

    public override AITacticsModule CreateModule(AIParameters? parameters) => new RoleTactic(this);
}

/// <summary>
/// Spec 365 and 367. Moves a ship by its team role (HcTeam) or its Brawl trait. A tank or a brawler closes to BrawlRangeFraction
/// of its range. A sniper holds at KiteRangeFraction of its range, even when the target outranges it. A flanker attacks from
/// TeamFlankDegrees left or right of the tank's side. A line ship and a ship without a role fall through to the next module.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class RoleTactic(RoleTacticRules rules) : AITacticsModule
{
    public override bool UpdateAttackAngle(AttackCommand ac, ShipAI ai)
    {
        if (Team(ac) is not { } team || Core.Run.Current is not { } run) return false;
        float sign = team.Role switch { TeamRole.FlankLeft => 1f, TeamRole.FlankRight => -1f, _ => 0f };
        if (sign == 0f || team.Tank is not { } tank || tank == ac.Ship) return false;
        Direction tankSide = ac.Target.DetWorldCenter.DirectionTo(tank.DetWorldCenter);
        ac.UseWorldRelativeFollowAngle = true;
        ac.FollowAngle = tankSide + sign * MathF.PI / 180f * run.T.Tactics.TeamFlankDegrees;
        return true;
    }

    public override bool UpdateAttackRadiusAndRotation(AttackCommand ac, ShipAI ai)
    {
        if (Core.Run.Current is not { } run) return false;
        var t = run.T.Tactics;
        var role = Team(ac)?.Role ?? TeamRole.None;
        float range = HunterOrders.MaxWeaponRange(ac.Ship, onlyActive: true);
        float? edge = role switch
        {
            TeamRole.Tank => range * t.BrawlRangeFraction,
            TeamRole.Sniper => range * t.KiteRangeFraction,
            TeamRole.None when TacticGate.Active(Tactic.Brawl, ac.Ship) is not null => range * t.BrawlRangeFraction,
            _ => null,
        };
        if (edge is not { } e || range <= 0f) return false;
        // Same sight cap as the vanilla default module, so the target stays in sight.
        float sightCap = ac.Ship.Sensors.SightRadius - ac.Ship.WorldBoundingRadius * 2f - ac.TargetRadius;
        ac.EdgeToEdgeFollowRadius = Math.Max(0f, Math.Min(e, sightCap));
        ac.SetBestAttackRotation(useIdealRange: false, onlyWithResources: true, rules.AttackRotationsToCheck, rules.AttackRotationSwitchThresholdFactor);
        return true;
    }

    public override AITacticsModule Clone(AIParameters? parameters) => new RoleTactic(rules);

    /// <summary>The team module of the ship when it is in a team on this attack's target.</summary>
    static TeamStrategy? Team(AttackCommand ac) =>
        ac.Ship.AI?.GetStrategyModule<TeamStrategy>() is { Role: not TeamRole.None } team && team.Target == ac.Target ? team : null;
}

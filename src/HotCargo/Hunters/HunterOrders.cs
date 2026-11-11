using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Halfling.Geometry;

namespace HotCargo.Hunters;

/// <summary>Command helpers for hunter AI modules. Each one enqueues a new command only when the current one differs.</summary>
public static class HunterOrders
{
    /// <summary>Same command shape as AIAggroEnemiesModule.Attack.</summary>
    public static void Attack(Ship ship, Ship target)
    {
        if (ship.Commands.CurConfirmedCommand is AttackCommand ac && ac.Target == target) return;
        ship.Commands.EnqueueCommand(
            new AttackCommand(ship, ship.Commands.AllocGlobalCommandID(), target, CollisionAvoidanceFlags.All, isTentative: false, isConfirmed: true),
            append: false, confirmed: true);
    }

    /// <summary>Moves to a location. The current move stays if its destination is within tolerance of the new one.</summary>
    public static void MoveTo(Ship ship, Vector2 location, float tolerance)
    {
        if (ship.Commands.CurConfirmedCommand is MoveCommand { Location: { } current } && current.DistanceTo(in location) <= tolerance) return;
        ship.Commands.EnqueueCommand(
            new MoveCommand(ship, ship.Commands.AllocGlobalCommandID(), location, null, CollisionAvoidanceFlags.All, isTentative: false),
            append: false, confirmed: true);
    }

    /// <summary>Ship health as the vanilla AI measures it (AIDefaultAttackRadiusAndRotationModule): built value over blueprint value.</summary>
    public static float HealthFraction(Ship ship)
    {
        int full = ship.BaseBlueprintCost;
        return full <= 0 ? 1f : (float)ship.BasePhysicalValue / full;
    }

    /// <summary>Longest effective range of the ship's weapons, as AttackCommand.GetMaxWeaponRange computes it. 0 without weapons.</summary>
    public static float MaxWeaponRange(Ship ship)
    {
        float max = 0f;
        foreach (var weapon in ship.Weapons)
            if (weapon.ShouldEstimateFirepower)
                max = Math.Max(max, weapon.GetEffectiveRange(Cosmoteer.Ships.Parts.Weapons.WeaponCoverageMode.Confirmed));
        return max;
    }

    public static Cosmoteer.Simulation.SimRoot Sim(Ship ship) => ship.Sim ?? throw new InvalidOperationException($"ship {ship.Metadata.PlainShipName} is not in a simulation");

    /// <summary>Distance between the bounding circles of two ships. 0 when they overlap.</summary>
    public static float EdgeDistance(Ship a, Ship b) =>
        Math.Max(0f, a.DetWorldCenter.DistanceTo(b.DetWorldCenter) - a.WorldBoundingRadius - b.WorldBoundingRadius);
}

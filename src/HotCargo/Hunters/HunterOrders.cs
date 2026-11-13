using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Halfling.Geometry;

namespace HotCargo.Hunters;

/// <summary>Command helpers for hunter AI modules. Each one enqueues a new command only when the current one differs.</summary>
public static class HunterOrders
{
    /// <summary>
    /// Runs first in the hunter chase module (HcHunt) of each live hunter. True means another area gave the ship its orders this AI tick
    /// (the Pursuit: a reporting scout or a blockade ship, spec 911 and 916) and the chase waits. AI tick: sim state only.
    /// </summary>
    public static Func<Ship, bool> Override = _ => false;

    /// <summary>
    /// A hunter in stasis moves to this point instead of the nearest human ship, when another area returns one (the Pursuit, spec 911).
    /// Arguments: sim, the ship ID of the stasis spawner. Sim state only.
    /// </summary>
    public static Func<Cosmoteer.Simulation.SimRoot, Cosmoteer.Game.ObjectID, Vector2?> StasisGoal = (_, _) => null;

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
        if (ship.Commands.CurConfirmedCommand is MoveCommand { Location: { } current, ForceDestinationRotation: false } && current.DistanceTo(in location) <= tolerance) return;
        ship.Commands.EnqueueCommand(
            new MoveCommand(ship, ship.Commands.AllocGlobalCommandID(), location, null, CollisionAvoidanceFlags.All, isTentative: false),
            append: false, confirmed: true);
    }

    /// <summary>
    /// Moves to a location and keeps one ship-local direction (radians, 0 = the ship's +X cell axis) toward the target, as
    /// AIAggroEnemiesModule.TacticalRetreat does: a strafing move (forced rotation) with the target as its rotation target.
    /// The current move stays if it already strafes toward the same target within tolerance.
    /// </summary>
    public static void MoveFacing(Ship ship, Vector2 location, float tolerance, Ship target, float localAngle)
    {
        Direction rotation = ship.DetWorldCenter.DirectionTo(target.DetWorldCenter) - localAngle;
        if (ship.Commands.CurConfirmedCommand is MoveCommand { Location: { } current, ForceDestinationRotation: true } move
            && current.DistanceTo(in location) <= tolerance && move.RotationTarget == target)
        {
            move.SetWorldRotation(rotation);
            return;
        }
        var command = new MoveCommand(ship, ship.Commands.AllocGlobalCommandID(), location, null, CollisionAvoidanceFlags.All, isTentative: false,
            forceDestinationRotation: true) { RotationTarget = target };
        command.SetWorldRotation(rotation);
        ship.Commands.EnqueueCommand(command, append: false, confirmed: true);
    }

    /// <summary>
    /// Ship health: the cost of each part times its health fraction, over the blueprint cost. Destroyed parts count as 0.
    /// The vanilla value (BasePhysicalValue) charges only the repair cost of damage, so a ship with every part at 20 % health still reads about 0.8.
    /// </summary>
    public static float HealthFraction(Ship ship)
    {
        int full = ship.BaseBlueprintCost;
        if (full <= 0) return 1f;
        float built = ship.Doors.TotalMoneyValue;
        foreach (var part in ship.Parts)
            built += part.Rules.Cost * (float)part.Health / Math.Max(1, part.Rules.MaxHealth);
        return Math.Clamp(built / full, 0f, 1f);
    }

    /// <summary>
    /// Longest effective range of the ship's weapons, as AttackCommand.GetMaxWeaponRange computes it. 0 without weapons.
    /// With onlyActive, weapons that the vanilla AI leaves out of firepower estimates (toggled off, suppressed) do not count.
    /// Use onlyActive = false for a target: its player can turn those weapons on at any time.
    /// </summary>
    public static float MaxWeaponRange(Ship ship, bool onlyActive)
    {
        float max = 0f;
        foreach (var weapon in ship.Weapons)
            if (onlyActive ? weapon.ShouldEstimateFirepower : !weapon.Rules.IsDefensiveOnly)
                max = Math.Max(max, weapon.GetEffectiveRange(Cosmoteer.Ships.Parts.Weapons.WeaponCoverageMode.Confirmed));
        return max;
    }

    public static Cosmoteer.Simulation.SimRoot Sim(Ship ship) => ship.Sim ?? throw new InvalidOperationException($"ship {ship.Metadata.PlainShipName} is not in a simulation");

    /// <summary>Distance between the bounding circles of two ships. 0 when they overlap.</summary>
    public static float EdgeDistance(Ship a, Ship b) =>
        Math.Max(0f, a.DetWorldCenter.DistanceTo(b.DetWorldCenter) - a.WorldBoundingRadius - b.WorldBoundingRadius);
}

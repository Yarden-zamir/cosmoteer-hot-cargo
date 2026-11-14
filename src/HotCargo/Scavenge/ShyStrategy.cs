using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Commands;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;
using HotCargo.Core;
using HotCargo.Hunters;

namespace HotCargo.Scavenge;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcShy")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class ShyStrategyRules : AIStrategyModuleRules
{
    public override AIStrategyModule CreateModule(AIParameters? parameters) => new ShyStrategy();
}

/// <summary>
/// Spec 1344. A scavenger runs from enemies that come its way, unless it is clearly stronger than all of them. It flies straight away from
/// the nearest threat and stops once no threat comes its way and the nearest one is SafeDistance away. Tell: the ship turns and runs.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class ShyStrategy : AIStrategyModule
{
    [Serialize(Optional = true)] bool _fleeing;

    public bool Fleeing => _fleeing;

    public override void GetIntegrityHash(IntegrityHash iHash) => iHash.AddHash(_fleeing ? 1 : 0);

    public override void OnEnteringStasis(Ship ship) => _fleeing = false;

    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        Ship? threat = null;
        bool next = Run.Current is { } run && run.T.Scavenge.Enabled && ship.Commands.CanExecuteCommands && Step(run, ship, _fleeing, out threat);
        if (next != _fleeing)
            Log.Info($"scavengers: {ship.Metadata.PlainShipName} " + (next ? $"flees from {threat?.Metadata.PlainShipName} (player {threat?.Metadata.PlayerIndex})" : "stops fleeing"));
        _fleeing = next;
        return next;
    }

    /// <summary>Decides and, when fleeing, moves. Threats: significant enemies within FleeRange (edge distance), in sim order.</summary>
    static bool Step(Run run, Ship ship, bool fleeing, out Ship? threat)
    {
        var t = run.T.Scavenge;
        threat = null;
        var ships = HunterOrders.Sim(ship).Ships;
        Ship? nearest = null;
        float nearestDistance = float.PositiveInfinity, firepower = 0f;
        bool approaching = false;
        for (int i = 0; i < ships.Count; i++)
        {
            var other = ships[i];
            if (other == ship || !other.IsSignificant || other.IsJunk || !ship.IsEnemiesWith(other)) continue;
            float d = HunterOrders.EdgeDistance(ship, other);
            if (d > t.FleeRange) continue;
            firepower += other.Weapons.GetTotalFirePower();
            if (d < nearestDistance) (nearest, nearestDistance) = (other, d);
            var v = other.Physics.Body?.LinearVelocity ?? Vector2.Zero;
            var to = ship.DetWorldCenter - other.DetWorldCenter;
            if (ScavengeModel.Approaching(v.X, v.Y, to.X, to.Y, t.ApproachSpeed) || (other.Commands.CurConfirmedCommand as IAttackCommand)?.Target == ship)
                approaching = true;
        }
        if (!ScavengeModel.Flees(fleeing, ship.Weapons.GetTotalFirePower(), firepower, t.StrongerRatio, approaching, nearestDistance, t.SafeDistance)) return false;
        threat = nearest!;
        Vector2 away = Vector2.FromAngle(threat.DetWorldCenter.DirectionTo(ship.DetWorldCenter));
        HunterOrders.MoveTo(ship, threat.DetWorldCenter + away * (t.SafeDistance * 1.25f + threat.WorldBoundingRadius + ship.WorldBoundingRadius), t.SafeDistance * 0.25f);
        return true;
    }

    public override AIStrategyModule Clone(AIParameters? parameters) => new ShyStrategy { _fleeing = _fleeing };
}

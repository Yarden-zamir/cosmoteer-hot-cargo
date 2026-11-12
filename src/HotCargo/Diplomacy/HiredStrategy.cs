using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;
using HotCargo.Ai;
using HotCargo.Core;
using HotCargo.Hunters;

namespace HotCargo.Diplomacy;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcHired")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class HiredStrategyRules : AIStrategyModuleRules
{
    public override AIStrategyModule CreateModule(AIParameters? parameters) => new HiredStrategy();
}

/// <summary>
/// Spec 580 to 583. A hired ship fights the enemies of its employer near the employer's nearest ship, and follows it when no enemy is near.
/// Below its flee health it gives up: it runs from the nearest enemy until clear, then the contract ends and the vanilla modules take over.
/// The hire lives in the run state, so this module keeps no state of its own. Ships that nobody hired fall through to the vanilla modules.
/// </summary>
internal sealed class HiredStrategy : AIStrategyModule
{
    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        if (Run.Current is not { } run || ship.Sim is not { } sim || run.Game.Sim != sim) return false;
        var hire = run.State.Diplomacy.Hires.FirstOrDefault(h => h.Ship == ship.UniqueID);
        if (hire is null) return false;
        var t = run.T.Diplomacy;
        var employer = sim.Ships.HumanPlayerSignificantShips.Where(h => h.Commands.CanExecuteCrewCommandsFrom(hire.Player))
            .OrderBy(h => h.DetWorldCenter.DistanceSquaredTo(ship.DetWorldCenter)).ThenBy(h => h.UniqueID).FirstOrDefault();
        if (employer is null) return false;
        float health = HunterOrders.HealthFraction(ship);
        if (!hire.Fleeing && health < (hire.Merc ? t.MercFleeHealth : t.HiredFleeHealth))
        {
            hire.Fleeing = true;
            Ux.UxKit.ChatLine(run.Game, DipText.Format("HireFleeFmt", ship.Metadata.XmlShipName));
            Log.Info($"diplomacy: hired {ship.Metadata.PlainShipName} runs at health {health:0.00}");
        }
        if (hire.Fleeing)
        {
            var threat = AiGate.NearestEnemy(ship);
            if (threat is not null && HunterOrders.EdgeDistance(ship, threat) < t.HiredFleeDistance)
            {
                Vector2 away = Vector2.FromAngle(threat.DetWorldCenter.DirectionTo(ship.DetWorldCenter));
                HunterOrders.MoveTo(ship, threat.DetWorldCenter + away * (t.HiredFleeDistance * 1.25f + threat.WorldBoundingRadius), t.HiredFleeDistance * 0.25f);
                return true;
            }
            run.State.Diplomacy.Hires.Remove(hire);
            Log.Info($"diplomacy: hired {ship.Metadata.PlainShipName} is clear and quits");
            return false;
        }
        if (Target(sim, ship, employer, hire.Player, t.HelpRadius) is { } target)
        {
            HunterOrders.Attack(ship, target);
            return true;
        }
        if (ship.DetWorldCenter.DistanceTo(employer.DetWorldCenter) > t.FollowDistance + employer.WorldBoundingRadius + ship.WorldBoundingRadius)
            HunterOrders.MoveTo(ship, employer.DetWorldCenter, t.FollowDistance * 0.5f);
        return true;
    }

    /// <summary>The enemy of the employer nearest to the employer's ship, within the help radius, that this ship can fight.</summary>
    static Ship? Target(SimRoot sim, Ship ship, Ship employer, int player, float radius)
    {
        Ship? best = null;
        float bestD = radius * radius;
        foreach (var s in sim.Ships)
        {
            if (!s.IsSignificant || s.IsJunk || s.IsHumanPlayerShip || !ship.IsEnemiesWith(s) || !sim.Mode.AreEnemies(s.Metadata.PlayerIndex, player)) continue;
            if (!s.CanBeAttackedBy(ship)) continue;
            float d = s.DetWorldCenter.DistanceSquaredTo(employer.DetWorldCenter);
            if (d <= bestD) { best = s; bestD = d; }
        }
        return best;
    }

    public override AIStrategyModule Clone(AIParameters? parameters) => new HiredStrategy();
}

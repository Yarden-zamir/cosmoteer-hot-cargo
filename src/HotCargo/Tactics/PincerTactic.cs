using Cosmoteer;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Commands;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;
using Halfling.Timing;
using HotCargo.Hunters;

namespace HotCargo.Tactics;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcPincer")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class PincerTacticRules : AITacticsModuleRules
{
    /// <summary>The group leader turns the whole formation by a small random angle this often, so the flanks keep moving.</summary>
    [Serialize, PortableHash] public Range<Time> TurnInterval;
    [Serialize(Optional = true), PortableHash] public Range<Angle> TurnDelta = new(-(float)Angle.FromDegrees(30f), Angle.FromDegrees(30f));

    public override AITacticsModule CreateModule(AIParameters? parameters) => new PincerTactic(this);
}

/// <summary>
/// Spec 45. Hunters that attack the same target split around it. The ship with the lowest ID leads.
/// The others take sides that alternate left and right of the leader, PincerAngleDegrees apart.
/// Tell: the group spreads out before it closes in, and ships come from opposite sides.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class PincerTactic(PincerTacticRules rules) : AITacticsModule
{
    [Serialize(Optional = true)] Time _nextTurn;
    int _lastSlot = -1;

    public override void GetIntegrityHash(IntegrityHash iHash) => _nextTurn.GetIntegrityHash(iHash);

    public override bool UpdateAttackAngle(AttackCommand ac, ShipAI ai)
    {
        if (TacticGate.Active(Tactic.Pincer, ac.Ship) is not { } t) return false;
        var group = Attackers(ac);
        if (group.Count < 2) { _lastSlot = -1; return false; }
        int slot = group.IndexOf(ac.Ship);
        if (slot < 0) throw new InvalidOperationException("pincer group lost its own ship");
        Direction angle;
        if (slot == 0)
        {
            bool keep = ac.UseWorldRelativeFollowAngle && ac.FollowAngle.HasValue && HunterOrders.Sim(ac.Ship).LogicalTime < _nextTurn;
            angle = keep ? ac.FollowAngle!.Value : CurrentSide(ac) + (float)ac.Ship.Rand.Angle(rules.TurnDelta);
            if (!keep) _nextTurn = HunterOrders.Sim(ac.Ship).LogicalTime + ac.Ship.Rand.Time(rules.TurnInterval);
        }
        else
        {
            var leader = (AttackCommand)group[0].Commands.CurConfirmedCommand!;
            Direction baseAngle = leader.UseWorldRelativeFollowAngle && leader.FollowAngle is { } f ? f : CurrentSide(leader);
            angle = baseAngle + MathF.PI / 180f * TacticsModel.PincerOffsetDegrees(slot, group.Count, t.PincerAngleDegrees);
        }
        ac.UseWorldRelativeFollowAngle = true;
        ac.FollowAngle = angle;
        if (slot != _lastSlot) TacticsLog.Event(ac.Ship, Tactic.Pincer, $"slot {slot} of {group.Count} on {ac.Target.Metadata.PlainShipName}");
        _lastSlot = slot;
        return true;
    }

    public override bool UpdateAttackRadiusAndRotation(AttackCommand ac, ShipAI ai) => false;

    public override AITacticsModule Clone(AIParameters? parameters) => new PincerTactic(rules) { _nextTurn = _nextTurn };

    static Direction CurrentSide(AttackCommand ac) => ac.Target.DetWorldCenter.DirectionTo(ac.Ship.DetWorldCenter);

    /// <summary>Allied ships with the pincer on that attack the same target, ordered by ID so that every client agrees on the slots.</summary>
    static List<Ship> Attackers(AttackCommand ac)
    {
        var group = HunterOrders.Sim(ac.Ship).Ships.GetShipsForPlayer(ac.Ship.Metadata.PlayerIndex)
            .Where(s => s.Commands.CurConfirmedCommand is AttackCommand other && other.Target == ac.Target && other.IsValid()
                        && TacticGate.Active(Tactic.Pincer, s) is not null)
            .ToList();
        group.Sort((a, b) => a.UniqueID.CompareTo(b.UniqueID));
        return group;
    }
}

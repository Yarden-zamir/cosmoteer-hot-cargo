using Cosmoteer;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Commands;
using Halfling.PortableHashing;
using Halfling.Serialization;
using HotCargo.Ai;
using HotCargo.Hunters;

namespace HotCargo.Tactics;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcTeam")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class TeamStrategyRules : AIStrategyModuleRules
{
    public override AIStrategyModule CreateModule(AIParameters? parameters) => new TeamStrategy(this);
}

/// <summary>
/// Spec 364-365. Allies with the Team trait within TeamRadius fight as one team. The member with the lowest ID leads; every member
/// attacks the leader's target. Each member gets a role from its build: tank, sniper, flanker or line. HcRole moves the ship by
/// its role. Tell: a team turns on one of your ships together, the tank closes in, the sniper hangs back and the flankers swing wide.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class TeamStrategy(TeamStrategyRules rules) : AIStrategyModule
{
    [Serialize(Optional = true)] int _role;
    [Serialize(Alias = "Tank", Optional = true)] Ship? _tank;
    [Serialize(Alias = "Target", Optional = true)] Ship? _target;
    [Serialize(Alias = "Leader", Optional = true)] Ship? _leader;
    [Serialize(Optional = true)] int _size;

    public TeamRole Role => (TeamRole)_role;
    /// <summary>The team's tank. Flankers take their side from it.</summary>
    public Ship? Tank => _tank;
    public Ship? Target => _target;
    public Ship? Leader => _leader;
    public int Size => _size;

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        _role.GetIntegrityHash(iHash);
        _size.GetIntegrityHash(iHash);
        (_tank?.UniqueID ?? default).GetIntegrityHash(iHash);
        (_target?.UniqueID ?? default).GetIntegrityHash(iHash);
        (_leader?.UniqueID ?? default).GetIntegrityHash(iHash);
    }

    public override void OnEnteringStasis(Ship ship) => Clear(ship);

    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        if (TacticGate.Active(Tactic.Team, ship) is not { } t) return Clear(ship);
        var members = Members(ship, t);
        if (members.Count < 2) return Clear(ship);
        var leader = members[0];
        var target = TeamTarget(leader);
        if (target is null || !ship.IsEnemiesWith(target) || !target.IsWithinRadarOfTeam(ship.Metadata.Team)) return Clear(ship);
        var roles = PersonalityModel.AssignRoles(members.Select(m => PersonalityStrategy.Scores(m, t)).ToList());
        var role = roles[members.IndexOf(ship)];
        var tank = members[Array.IndexOf(roles, TeamRole.Tank)];
        if (role != Role || target != _target || members.Count != _size)
            TacticsLog.Event(ship, Tactic.Team, $"{role} in a team of {members.Count} led by {leader.Metadata.PlainShipName} on {target.Metadata.PlainShipName}");
        (_role, _tank, _target, _leader, _size) = ((int)role, tank, target, leader, members.Count);
        HunterOrders.Attack(ship, target);
        return true;
    }

    public override AIStrategyModule Clone(AIParameters? parameters) =>
        new TeamStrategy(rules) { _role = _role, _tank = _tank, _target = _target, _leader = _leader, _size = _size };

    bool Clear(Ship ship)
    {
        if (_role != (int)TeamRole.None) TacticsLog.Event(ship, Tactic.Team, "leaves the team");
        (_role, _tank, _target, _leader, _size) = ((int)TeamRole.None, null, null, null, 0);
        return false;
    }

    /// <summary>The ship and its allies with the Team trait within TeamRadius, ordered by ID so that every client agrees.</summary>
    static List<Ship> Members(Ship ship, TacticsTunables t)
    {
        var members = AiGate.Allies(ship)
            .Where(a => a.DetWorldCenter.DistanceTo(ship.DetWorldCenter) <= t.TeamRadius && TacticGate.Active(Tactic.Team, a) is not null)
            .Prepend(ship).ToList();
        members.Sort((a, b) => a.UniqueID.CompareTo(b.UniqueID));
        return members;
    }

    /// <summary>
    /// The leader's current attack target, or the nearest enemy human ship on its radar when it has none.
    /// The leader keeps its target until the target dies or leaves radar: the whole team focuses one ship.
    /// </summary>
    static Ship? TeamTarget(Ship leader)
    {
        if (leader.Commands.CurConfirmedCommand is AttackCommand ac && ac.IsValid() && leader.IsEnemiesWith(ac.Target)
            && ac.Target.IsWithinRadarOfTeam(leader.Metadata.Team))
            return ac.Target;
        return AiGate.NearestHumanEnemy(leader);
    }
}

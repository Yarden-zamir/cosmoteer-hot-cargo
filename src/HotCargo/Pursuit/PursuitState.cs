using Cosmoteer.Game;
using Halfling.Geometry;
using Halfling.Serialization;

namespace HotCargo.Pursuit;

/// <summary>Saved Pursuit state (spec 940). The pacer history lasts the run; the rest belongs to the current system visit unless noted.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class PursuitState
{
    /// <summary>Spec 905: every big event start of the run, in start order.</summary>
    [Serialize(Optional = true)] public List<PacedStartState> Started = new();
    /// <summary>Spec 910: scouts that fly to report.</summary>
    [Serialize(Optional = true)] public List<ScoutReport> Scouts = new();
    /// <summary>Spec 916: the relay of the current blockade (its destination node), or null.</summary>
    [Serialize(Optional = true)] public ObjectID? BlockadeRelay;
    /// <summary>Spec 916: blockade ships that still park. A ship that chases leaves the list.</summary>
    [Serialize(Optional = true)] public List<ObjectID> Blockaders = new();
    /// <summary>Spec 921: run game seconds when the current outage ends. It lasts across jumps. 0 or less: no outage.</summary>
    [Serialize(Optional = true)] public double OutageUntil;
    /// <summary>Spec 920: run game seconds when the rolled natural outage of this system starts, or -1.</summary>
    [Serialize(Optional = true)] public double OutageDueAt = -1;
    /// <summary>Spec 930: run game seconds when the hacker of this system comes, or -1.</summary>
    [Serialize(Optional = true)] public double HackerDueAt = -1;
    /// <summary>Spec 930: the hacker's proxy ship while it is in this system.</summary>
    [Serialize(Optional = true)] public ObjectID? Hacker;
    /// <summary>Spec 932: run game seconds when the hacker leaves.</summary>
    [Serialize(Optional = true)] public double HackerLeaveAt;
    [Serialize(Optional = true)] public bool HackerHailed;
    /// <summary>Spec 932: the hacker sold the outage or the players said no; he leaves soon.</summary>
    [Serialize(Optional = true)] public bool HackerDone;
    /// <summary>Ships that jump out: removed at the end of their FTL effect (scouts that reported, the hacker).</summary>
    [Serialize(Optional = true)] public List<Removal> Removals = new();
}

[ReflectiveSerialization(Explicit = true)]
public sealed class PacedStartState
{
    [Serialize] public string Kind = "";
    [Serialize] public double At;
    [Serialize(Optional = true)] public int Sector;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class ScoutReport
{
    [Serialize] public ObjectID Ship;
    /// <summary>The relay that the scout flies to, by its destination node.</summary>
    [Serialize] public ObjectID Relay;
    [Serialize] public Vector2 RelayAt;
    [Serialize] public float StartDistance;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class Removal
{
    [Serialize] public ObjectID Ship;
    /// <summary>Run game seconds.</summary>
    [Serialize] public double At;
}

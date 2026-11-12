using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Relays;

/// <summary>Relay lockdown state in the run save (spec 950 to 969).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class RelaysState
{
    /// <summary>Spec 951: the node whose neighbours were rolled last. The roll runs once for each node that the players are in.</summary>
    [Serialize(Optional = true)] public ObjectID? RolledFor;
    /// <summary>Spec 951: nodes that rolled already this run, with or without a lockdown. A node rolls once.</summary>
    [Serialize(Optional = true)] public HashSet<ObjectID> Rolled = new();
    /// <summary>Spec 951, 953: the marked node and its kind. At most one, shown on the galaxy map before the players jump in.</summary>
    [Serialize(Optional = true)] public ObjectID? MarkedNode;
    [Serialize(Optional = true)] public LockKind MarkedKind;
    /// <summary>Spec 951: lockdowns that the players entered this run, and the sector index of the last one.</summary>
    [Serialize(Optional = true)] public int Started;
    [Serialize(Optional = true)] public int? LastSector;
    /// <summary>Spec 954: the lockdown of the node that the players are in, or null.</summary>
    [Serialize(Optional = true)] public Lockdown? Active;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class Lockdown
{
    [Serialize] public ObjectID Node;
    [Serialize] public LockKind Kind;
    /// <summary>Spec 954: the relays let relay jumps through again (fee paid, bribe taken, guards gone, or the closed time passed).</summary>
    [Serialize(Optional = true)] public bool Open;
    /// <summary>Game seconds of the run at arrival.</summary>
    [Serialize(Optional = true)] public double Since;
    /// <summary>Spec 957: the pirate guards. Empty until they spawn in the first tick of the visit.</summary>
    [Serialize(Optional = true)] public List<ObjectID> Guards = new();
    [Serialize(Optional = true)] public bool GuardsSpawned;
    /// <summary>Spec 959: a bribe failed, so the pirates take no further offer.</summary>
    [Serialize(Optional = true)] public bool BribeFailed;
}

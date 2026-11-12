using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Factions;

/// <summary>Factions area state in the run save. The faction roles come from the galaxy seed, so they need no state.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class FactionsState
{
    /// <summary>Spec 650: the next jump to another system is blind. Any player sets it with BlindJumpInput.</summary>
    [Serialize(Optional = true)] public bool BlindJump;
    /// <summary>Spec 651: game seconds that each human ship spins up with the blind switch on and all drives on, by ship ID.</summary>
    [Serialize(Optional = true)] public Dictionary<ObjectID, float> SpinSeconds = new();
    /// <summary>Spec 651: the spin seconds at which the drives of each ship were full (its vanilla spin-up time), by ship ID.</summary>
    [Serialize(Optional = true)] public Dictionary<ObjectID, float> FullAtSeconds = new();
}

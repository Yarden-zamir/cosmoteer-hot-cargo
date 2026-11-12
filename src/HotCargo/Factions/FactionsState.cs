using Halfling.Serialization;

namespace HotCargo.Factions;

/// <summary>Factions area state in the run save. The faction roles come from the galaxy seed, so they need no state.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class FactionsState
{
    /// <summary>Spec 650: the next jump to another system is blind. Any player sets it with BlindJumpInput.</summary>
    [Serialize(Optional = true)] public bool BlindJump;
}

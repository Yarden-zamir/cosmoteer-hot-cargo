using Halfling.Serialization;
using HotCargo.Chains;

namespace HotCargo.Core;

// Chains's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public ChainsTunables Chains = new();
}

public sealed partial class RunState
{
    /// <summary>Chains area state: the chain of each station, its stage, flags and story.</summary>
    [Serialize(Optional = true)] public Chains.ChainsState Chains = new();
}

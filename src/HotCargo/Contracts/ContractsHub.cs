using Halfling.Serialization;
using HotCargo.Contracts;

namespace HotCargo.Core;

// Contracts's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public ContractsTunables Contracts = new();
}

public sealed partial class RunState
{
    /// <summary>Contract economy state: resolved terms of offers and open contracts (spec 100 to 108).</summary>
    [Serialize(Optional = true)] public Contracts.ContractsState Contracts = new();
}

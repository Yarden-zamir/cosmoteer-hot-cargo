using Halfling.Serialization;
using HotCargo.Balance;

namespace HotCargo.Core;

// Balance's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public BalanceTunables Balance = new();
}

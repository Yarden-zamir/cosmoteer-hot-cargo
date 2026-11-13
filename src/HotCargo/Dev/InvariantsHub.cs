using Halfling.Serialization;
using HotCargo.Dev;

namespace HotCargo.Core;

// The invariant checker's part of the shared Tunables. It adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public InvariantsTunables Invariants = new();
}

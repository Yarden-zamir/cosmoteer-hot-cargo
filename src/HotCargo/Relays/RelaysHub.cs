using Halfling.Serialization;
using HotCargo.Relays;

namespace HotCargo.Core;

// Relays's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public RelaysTunables Relays = new();
}

public sealed partial class RunState
{
    /// <summary>Relay lockdowns (spec 950 to 969).</summary>
    [Serialize(Optional = true)] public RelaysState Relays = new();
}

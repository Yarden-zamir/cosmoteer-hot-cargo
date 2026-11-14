using Halfling.Serialization;
using HotCargo.Friendly;

namespace HotCargo.Core;

// Friendly's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public FriendlyTunables Friendly = new();
}

public sealed partial class RunState
{
    /// <summary>Friendly area state: apprentices and the other friendly ships that follow the players.</summary>
    [Serialize(Optional = true)] public FriendlyState Friendly = new();
}

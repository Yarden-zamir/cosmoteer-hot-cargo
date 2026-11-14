using Halfling.Serialization;
using HotCargo.Scavenge;

namespace HotCargo.Core;

// Scavenge's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public ScavengeTunables Scavenge = new();
}

public sealed partial class RunState
{
    /// <summary>Scavenging ages (spec 1332).</summary>
    [Serialize(Optional = true)] public ScavengeState Scavenge = new();
}

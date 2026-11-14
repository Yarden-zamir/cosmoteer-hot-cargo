using Halfling.Serialization;
using HotCargo.Debris;

namespace HotCargo.Core;

// Debris's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public DebrisTunables Debris = new();
}

public sealed partial class RunState
{
    /// <summary>Debris decay (spec 1240 to 1246).</summary>
    [Serialize(Optional = true)] public DebrisState Debris = new();
}

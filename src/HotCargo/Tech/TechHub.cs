using Halfling.Serialization;
using HotCargo.Tech;

namespace HotCargo.Core;

// Tech's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public TechTunables Tech = new();
}

public sealed partial class RunState
{
    /// <summary>Tech area state: the loss timer (spec 443).</summary>
    [Serialize(Optional = true)] public Tech.TechState Tech = new();
}

using Halfling.Serialization;
using HotCargo.Hunters;

namespace HotCargo.Core;

// Hunters's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public HunterTunables Hunters = new();
}

public sealed partial class RunState
{
    /// <summary>Hunters area state: hunter groups that wait to arrive in the current system (spec 191).</summary>
    [Serialize(Optional = true)] public Hunters.HunterState Hunters = new();
}

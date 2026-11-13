using Halfling.Serialization;
using HotCargo.Pursuit;

namespace HotCargo.Core;

// The Pursuit's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public PursuitTunables Pursuit = new();
}

public sealed partial class RunState
{
    [Serialize(Optional = true)] public PursuitState Pursuit = new();
}

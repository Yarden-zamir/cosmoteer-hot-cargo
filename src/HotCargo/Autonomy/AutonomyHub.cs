using Halfling.Serialization;
using HotCargo.Autonomy;

namespace HotCargo.Core;

// Autonomy's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public AutonomyTunables Autonomy = new();
}

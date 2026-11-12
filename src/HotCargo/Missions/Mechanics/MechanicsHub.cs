using Halfling.Serialization;
using HotCargo.Missions.Mechanics;

namespace HotCargo.Core;

// Mechanics's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public MechanicsTunables Mechanics = new();
}

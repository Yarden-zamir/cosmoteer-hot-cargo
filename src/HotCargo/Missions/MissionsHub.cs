using Halfling.Serialization;
using HotCargo.Missions;

namespace HotCargo.Core;

// Missions's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public MissionTunables Missions = new();
}

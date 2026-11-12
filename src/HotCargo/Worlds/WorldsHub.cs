using Halfling.Serialization;
using HotCargo.Worlds;

namespace HotCargo.Core;

// Worlds's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public WorldsTunables Worlds = new();
}

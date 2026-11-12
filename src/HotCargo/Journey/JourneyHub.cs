using Halfling.Serialization;
using HotCargo.Journey;

namespace HotCargo.Core;

// Journey's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public JourneyTunables Journey = new();
}

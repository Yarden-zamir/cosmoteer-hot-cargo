using Halfling.Serialization;
using HotCargo.Events;

namespace HotCargo.Core;

// Events's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public EventsTunables Events = new();
}

public sealed partial class RunState
{
    /// <summary>Events area state: seen events, flags and the pending event page.</summary>
    [Serialize(Optional = true)] public Events.EventsState Events = new();
}

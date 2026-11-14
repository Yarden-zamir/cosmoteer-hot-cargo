using Halfling.Serialization;
using HotCargo.StartJobs;

namespace HotCargo.Core;

// StartJobs's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public StartJobsTunables StartJobs = new();
}

public sealed partial class RunState
{
    /// <summary>Spec 1200: the waiting start job choice and the chosen job.</summary>
    [Serialize(Optional = true)] public StartJobsState StartJobs = new();
}

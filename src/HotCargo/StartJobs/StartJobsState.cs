using Halfling.Serialization;

namespace HotCargo.StartJobs;

/// <summary>Spec 1200: saved with the run. A save from before start jobs has neither a waiting choice nor a job.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class StartJobsState
{
    /// <summary>True from the start of a new run until the players pick a job.</summary>
    [Serialize(Optional = true)] public bool Pending;
    /// <summary>The ID of the chosen job, or empty.</summary>
    [Serialize(Optional = true)] public string Job = "";
}

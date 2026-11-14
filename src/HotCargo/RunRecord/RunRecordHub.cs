using Halfling.Serialization;
using HotCargo.RunRecord;

namespace HotCargo.Core;

// RunRecord's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class RunState
{
    /// <summary>Spec 1223: the numbers of the run that only the record needs.</summary>
    [Serialize(Optional = true)] public RunRecordState RunRecord = new();
}

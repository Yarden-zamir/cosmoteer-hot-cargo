using Halfling.Serialization;

namespace HotCargo.RunRecord;

/// <summary>Spec 1223: saved with the run.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class RunRecordState
{
    /// <summary>The highest heat of the run. The sim tick raises it.</summary>
    [Serialize(Optional = true)] public float PeakHeat;
}

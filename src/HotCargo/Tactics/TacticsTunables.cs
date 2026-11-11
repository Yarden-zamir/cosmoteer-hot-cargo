using Halfling.Serialization;

namespace HotCargo.Tactics;

[ReflectiveSerialization(Explicit = true)]
public sealed class TacticsTunables
{
    [Serialize(Optional = true)] public bool KiteEnabled = true;
    [Serialize(Optional = true)] public float KiteRangeFraction = 0.9f;
    [Serialize(Optional = true)] public bool PincerEnabled = true;
    [Serialize(Optional = true)] public float PincerAngleDegrees = 120f;
    [Serialize(Optional = true)] public bool DisableEnabled = true;
    [Serialize(Optional = true)] public string DisableMinStage = "Hunt";
    [Serialize(Optional = true)] public bool FallBackEnabled = true;
    [Serialize(Optional = true)] public float FallBackHealthFraction = 0.35f;
    [Serialize(Optional = true)] public bool BaitEnabled = true;
    [Serialize(Optional = true)] public string BaitMinStage = "Strike";
}

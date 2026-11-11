using Halfling.Serialization;

namespace HotCargo.Tactics;

/// <summary>
/// Spec 43-49. Each tactic has an on/off switch and a minimum stage: a Heat.Stages name, or "" for no minimum.
/// A tactic works only while the current system has started its minimum stage.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class TacticsTunables
{
    [Serialize(Optional = true)] public bool KiteEnabled = true;
    [Serialize(Optional = true)] public string KiteMinStage = "";
    /// <summary>A kiting ship holds its nearest edge at this fraction of its longest weapon range from the target's edge.</summary>
    [Serialize(Optional = true)] public float KiteRangeFraction = 0.9f;

    [Serialize(Optional = true)] public bool PincerEnabled = true;
    [Serialize(Optional = true)] public string PincerMinStage = "";
    /// <summary>Angle between the attack sides of two hunters on the same target. Capped at 360 / group size.</summary>
    [Serialize(Optional = true)] public float PincerAngleDegrees = 120f;

    [Serialize(Optional = true)] public bool DisableEnabled = true;
    [Serialize(Optional = true)] public string DisableMinStage = "Hunt";
    /// <summary>The current weapon or thruster target stays until another one is this many times closer.</summary>
    [Serialize(Optional = true)] public float DisableSwitchFactor = 1.5f;

    [Serialize(Optional = true)] public bool FallBackEnabled = true;
    [Serialize(Optional = true)] public string FallBackMinStage = "";
    /// <summary>A ship falls back below this health fraction (built value over blueprint value).</summary>
    [Serialize(Optional = true)] public float FallBackHealthFraction = 0.35f;
    /// <summary>A ship returns at this health fraction, or after FallBackMaxSeconds.</summary>
    [Serialize(Optional = true)] public float FallBackRecoverFraction = 0.6f;
    [Serialize(Optional = true)] public float FallBackMaxSeconds = 45f;
    /// <summary>Distance that a ship flies away from the nearest player ship before it repairs.</summary>
    [Serialize(Optional = true)] public float FallBackDistance = 1500f;

    [Serialize(Optional = true)] public bool BaitEnabled = true;
    [Serialize(Optional = true)] public string BaitMinStage = "Strike";
    /// <summary>The bait closes to this edge-to-edge distance from the target.</summary>
    [Serialize(Optional = true)] public float BaitCloseDistance = 150f;
    /// <summary>The bait pulls back after this time, even if it did not get close.</summary>
    [Serialize(Optional = true)] public float BaitApproachSeconds = 20f;
    /// <summary>The bait stays near the group this long, then comes close again.</summary>
    [Serialize(Optional = true)] public float BaitPullBackSeconds = 12f;

    /// <summary>Writes each tactic decision to the Hot Cargo log. For tests and tuning.</summary>
    [Serialize(Optional = true)] public bool DebugLog;
}

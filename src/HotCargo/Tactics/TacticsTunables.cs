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

    // Enemy AI behaviors (spec 193-202). "Enabled" switches the behavior for hunters (HotCargoHunter AI),
    // "OnVanilla" for the vanilla combat AIs (Normal, Easy, Wanderer, StationGuard, Police: pirates, bounties, faction ships).

    [Serialize(Optional = true)] public bool FleeEnabled = true;
    [Serialize(Optional = true)] public bool FleeOnVanilla = true;
    /// <summary>A ship below this health fraction rolls FleeChance once to flee. A ship without offensive weapons always flees.</summary>
    [Serialize(Optional = true)] public float FleeHealthFraction = 0.25f;
    [Serialize(Optional = true)] public float FleeChance = 0.5f;
    /// <summary>A fleeing ship keeps at least this edge-to-edge distance from the nearest enemy.</summary>
    [Serialize(Optional = true)] public float FleeDistance = 1500f;
    /// <summary>A fleeing hunter jumps out of the system this long after it starts to flee.</summary>
    [Serialize(Optional = true)] public float FleeJumpSeconds = 25f;
    /// <summary>A fleeing vanilla ship repairs when clear and fights again at this health fraction, if it has weapons.</summary>
    [Serialize(Optional = true)] public float FleeRecoverFraction = 0.6f;

    [Serialize(Optional = true)] public bool CallEnabled = true;
    [Serialize(Optional = true)] public bool CallOnVanilla = true;
    /// <summary>A call for help takes this long. One disruptor, EMP or ion beam hit stops it.</summary>
    [Serialize(Optional = true)] public float CallSeconds = 6f;
    /// <summary>Allies within this distance of the caller hear the call.</summary>
    [Serialize(Optional = true)] public float CallRange = 6000f;
    /// <summary>A ship that called, was stopped or answered a call does not call again for this long.</summary>
    [Serialize(Optional = true)] public float CallCooldownSeconds = 60f;
    /// <summary>An ally that answered a call hunts the caller's target for this long.</summary>
    [Serialize(Optional = true)] public float CallAssistSeconds = 60f;
    /// <summary>A ship that loses this health fraction between two AI checks took damage, and calls.</summary>
    [Serialize(Optional = true)] public float CallDamageTrigger = 0.02f;
    /// <summary>Nebula types that block calls: a caller or listener inside one at CallStormStrength or more cannot call or hear.</summary>
    [Serialize(Optional = true)] public string[] CallBlockedInNebulas = ["ion_storm", "electron"];
    [Serialize(Optional = true)] public float CallStormStrength = 0.5f;
    /// <summary>Part IDs whose damage stops a call. Any hit that drains battery power (disruptors, EMP missiles) stops a call too.</summary>
    [Serialize(Optional = true)] public string[] CallInterruptParts = ["cosmoteer.ion_beam_emitter", "cosmoteer.ion_beam_prism"];

    [Serialize(Optional = true)] public bool FocusEnabled = true;
    [Serialize(Optional = true)] public bool FocusOnVanilla = true;
    /// <summary>Attack value of a target × (1 + FocusWeakFactor × its lost health fraction). Higher focuses fire on weakened ships more.</summary>
    [Serialize(Optional = true)] public float FocusWeakFactor = 1.5f;

    [Serialize(Optional = true)] public bool RegroupEnabled = true;
    [Serialize(Optional = true)] public bool RegroupOnVanilla = true;
    /// <summary>A ship with less than this fraction of its target's firepower waits for allies before it attacks.</summary>
    [Serialize(Optional = true)] public float RegroupFirepowerRatio = 0.75f;
    /// <summary>Allies within this distance count as close.</summary>
    [Serialize(Optional = true)] public float RegroupRadius = 1200f;
    /// <summary>Only allies within this distance count as cover. Allies further away do not make the ship wait.</summary>
    [Serialize(Optional = true)] public float RegroupSearchRange = 5000f;
    /// <summary>The ship attacks once this many ships (itself included) are close, or all its allies are.</summary>
    [Serialize(Optional = true)] public int RegroupMinGroup = 2;
    /// <summary>The ship attacks anyway after this long, and does not regroup again for the same time.</summary>
    [Serialize(Optional = true)] public float RegroupMaxSeconds = 25f;

    /// <summary>Called from TunablesFile.Validate (spec 53).</summary>
    public void Validate(Action<bool, string> require)
    {
        require(FleeHealthFraction is >= 0 and <= 1 && FleeRecoverFraction is >= 0 and <= 1, "Tactics.FleeHealthFraction and FleeRecoverFraction must be 0 to 1");
        require(FleeChance is >= 0 and <= 1, "Tactics.FleeChance must be 0 to 1");
        require(CallSeconds > 0 && CallRange > 0, "Tactics.CallSeconds and CallRange must be above 0");
        require(CallStormStrength is > 0 and <= 1, "Tactics.CallStormStrength must be above 0 and at most 1");
        require(RegroupMinGroup >= 1, "Tactics.RegroupMinGroup must be 1 or more");
    }
}

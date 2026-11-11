namespace HotCargo.Tactics;

public enum BaitPhase { Approach, PullBack }

/// <summary>Pure tactic decisions on plain numbers. No game types.</summary>
public static class TacticsModel
{
    /// <summary>
    /// Spec 44. Edge-to-edge distance at which a ship kites its target, or null when kiting gives no range advantage:
    /// the hold distance must be outside the target's longest weapon range.
    /// </summary>
    public static float? KiteEdgeDistance(float ownRange, float targetRange, float rangeFraction)
    {
        float hold = ownRange * rangeFraction;
        return hold > targetRange && hold > 0f ? hold : null;
    }

    /// <summary>
    /// Spec 45. Attack side of a group member, in degrees from the side of the group leader (slot 0).
    /// Members alternate left and right of the leader. The step is the pincer angle, capped so that the group fits in 360 degrees.
    /// </summary>
    public static float PincerOffsetDegrees(int slot, int groupSize, float pincerAngleDegrees)
    {
        if (groupSize < 1 || slot < 0 || slot >= groupSize) throw new ArgumentOutOfRangeException(nameof(slot), $"slot {slot} of {groupSize}");
        float step = Math.Min(pincerAngleDegrees, 360f / groupSize);
        int ring = (slot + 1) / 2;
        return slot % 2 == 1 ? ring * step : -ring * step;
    }

    /// <summary>
    /// Spec 47. Whether a ship falls back after this check. It starts below the trigger health and
    /// stops at the recover health or when its time runs out. The gap between the two values stops flicker.
    /// After a stop, a cooldown keeps a ship that could not repair from running away again at once.
    /// </summary>
    public static bool FallBackActive(bool active, float health, float triggerHealth, float recoverHealth, bool timedOut, bool coolingDown) =>
        active ? health < recoverHealth && !timedOut : health < triggerHealth && !coolingDown;

    /// <summary>
    /// Spec 48. The bait closes in until it is near the target or its approach time runs out,
    /// then pulls back toward its group for a fixed time, then closes in again.
    /// "Near" allows 25 % over the close distance, because the follow command stops a little short.
    /// </summary>
    public static BaitPhase NextBaitPhase(BaitPhase phase, double phaseSeconds, float edgeDistance,
        float closeDistance, float approachSeconds, float pullBackSeconds) => phase switch
    {
        BaitPhase.Approach when edgeDistance <= closeDistance * 1.25f || phaseSeconds >= approachSeconds => BaitPhase.PullBack,
        BaitPhase.PullBack when phaseSeconds >= pullBackSeconds => BaitPhase.Approach,
        _ => phase,
    };
}

namespace HotCargo.Scavenge;

/// <summary>What a scavenger does next (spec 1336).</summary>
public enum ScavengeStep { Idle, Collect, Sell }

/// <summary>The side of another player index, as the scavenger relations see it (spec 1340).</summary>
public enum Side { Human, Faction, Neutral, Other }

/// <summary>Scavenging rules on plain numbers (spec 1330 to 1359). No game types, so the unit tests cover it.</summary>
public static class ScavengeModel
{
    /// <summary>Spec 1331: a wreck or loose resource counts once the cooldown passed since the age check first saw it.</summary>
    public static bool Ripe(double firstSeen, double now, double cooldown) => now - firstSeen >= cooldown;

    /// <summary>
    /// Spec 1336: a full scavenger sells (or, when it cannot sell, does nothing). Otherwise it collects while there is a target, and sells
    /// what it holds when there is none. fill: the storage bay fill, 0 to 1.
    /// </summary>
    public static ScavengeStep Next(float fill, bool hasTarget, bool canSell, float fullFraction)
    {
        if (fill is < 0f or > 1.0001f) throw new ArgumentOutOfRangeException(nameof(fill), fill, "a fill is 0 to 1");
        if (fill >= fullFraction) return canSell ? ScavengeStep.Sell : ScavengeStep.Idle;
        if (hasTarget) return ScavengeStep.Collect;
        return fill > 0f && canSell ? ScavengeStep.Sell : ScavengeStep.Idle;
    }

    /// <summary>
    /// Spec 1340, 1341: whether the scavengers and a side are enemies. Players only after they provoked the scavengers; vanilla factions and
    /// neutral ships never. Null: the vanilla rule decides (pirates, hunters and other hostile sides are enemies of everyone).
    /// </summary>
    public static bool? Enemies(Side other, bool provoked) => other switch
    {
        Side.Human => provoked,
        Side.Faction or Side.Neutral => false,
        Side.Other => null,
        _ => throw new ArgumentOutOfRangeException(nameof(other), other, null),
    };

    /// <summary>Spec 1344: a threat comes the scavenger's way: its velocity toward the scavenger is speed or more. (dx, dy): from the threat to the scavenger.</summary>
    public static bool Approaching(float vx, float vy, float dx, float dy, float speed)
    {
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1e-3f) return true;
        return (vx * dx + vy * dy) / length >= speed;
    }

    /// <summary>
    /// Spec 1344: a scavenger flees while a threat comes its way, unless it is clearly stronger (own firepower at least ratio times the
    /// firepower of all threats). Once fleeing, it keeps fleeing until the nearest threat is safe away.
    /// </summary>
    public static bool Flees(bool fleeing, float ownFirepower, float threatFirepower, float ratio, bool approaching, float nearestThreat, float safeDistance)
    {
        if (float.IsPositiveInfinity(nearestThreat)) return false;
        // An unarmed threat (firepower 0) never makes it flee.
        if (ownFirepower >= threatFirepower * ratio) return false;
        if (approaching) return true;
        return fleeing && nearestThreat < safeDistance;
    }
}

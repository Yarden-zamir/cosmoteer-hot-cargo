namespace HotCargo.Scavenge;

/// <summary>What a scavenger does next (spec 1336).</summary>
public enum ScavengeStep { Idle, Collect, Sell }

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
}

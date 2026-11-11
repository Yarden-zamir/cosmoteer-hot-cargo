namespace HotCargo.Tech;

/// <summary>Pure tech math on plain numbers. No game types.</summary>
public static class TechModel
{
    /// <summary>Spec requirement 19, with game time rounded down to whole price steps.</summary>
    public static long Value(float baseValue, float growthPerSector, float growthPerGameHour, float priceStepMinutes, int sectorIndex, double gameSeconds)
    {
        double stepSeconds = Math.Max(1.0, priceStepMinutes * 60.0);
        double steppedHours = Math.Floor(gameSeconds / stepSeconds) * stepSeconds / 3600.0;
        double factor = 1.0 + growthPerSector * Math.Max(0, sectorIndex - 1) + growthPerGameHour * steppedHours;
        return (long)Math.Round(baseValue * factor);
    }

    /// <summary>
    /// Vault damage to tech loss: each time the damage a vault took since its last loss reaches
    /// damagePerUnit, one unit is lost. Returns the units lost and the damage left over.
    /// </summary>
    public static (int UnitsLost, float Remainder) UnitsLostToDamage(float accumulatedDamage, float damagePerUnit)
    {
        if (damagePerUnit <= 0) throw new ArgumentOutOfRangeException(nameof(damagePerUnit), "must be above 0");
        int lost = (int)(accumulatedDamage / damagePerUnit);
        return (lost, accumulatedDamage - lost * damagePerUnit);
    }
}

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
    /// Spec 65: units lost when vault health drops from oldHealth to newHealth.
    /// One unit is lost for each step of damagePerUnit below max health that the drop crosses.
    /// It needs no saved state, so it gives the same result after a load and on a client that joins late.
    /// A repair raises health above the steps again, so later damage costs tech again, but the repair brings nothing back.
    /// </summary>
    public static int UnitsLostToDamage(int maxHealth, int oldHealth, int newHealth, float damagePerUnit)
    {
        if (damagePerUnit <= 0) throw new ArgumentOutOfRangeException(nameof(damagePerUnit), "must be above 0");
        if (newHealth >= oldHealth) return 0;
        int Steps(int health) => (int)Math.Floor((maxHealth - health) / (double)damagePerUnit);
        return Steps(newHealth) - Steps(oldHealth);
    }
}

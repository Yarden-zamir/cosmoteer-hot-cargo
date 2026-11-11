namespace HotCargo.Hunters;

/// <summary>Pure wave math on plain numbers (spec 190-192). No game types.</summary>
public static class WaveModel
{
    /// <summary>Run progress from 0 in sector 1 to 1 in the last sector. A one-sector run counts as the last sector.</summary>
    public static float Progress(int sectorIndex, int runSectorCount) =>
        runSectorCount <= 1 ? 1f : Math.Clamp((sectorIndex - 1f) / (runSectorCount - 1f), 0f, 1f);

    /// <summary>
    /// Ships in a stage response: ShipCount × EarlyScale in sector 1, rising linearly to ShipCount in the last sector.
    /// Never below 1, so a stage always sends someone.
    /// </summary>
    public static int ShipCount(int shipCount, float earlyScale, float progress)
    {
        float scale = earlyScale + (1f - earlyScale) * progress;
        return Math.Max(1, (int)MathF.Round(shipCount * scale, MidpointRounding.AwayFromZero));
    }

    /// <summary>Tier offset: TierOffset − EarlyTierReduction in sector 1, rising to TierOffset in the last sector.</summary>
    public static int TierOffset(int tierOffset, int earlyTierReduction, float progress) =>
        tierOffset - (int)MathF.Round(earlyTierReduction * (1f - progress), MidpointRounding.AwayFromZero);

    /// <summary>
    /// Splits the ships of a stage into groups that arrive one after another. The first groups get the extra ships.
    /// Never more groups than ships. Example: 5 ships in 3 waves = [2, 2, 1].
    /// </summary>
    public static int[] SplitWaves(int ships, int waves)
    {
        if (ships < 0) throw new ArgumentOutOfRangeException(nameof(ships), ships, "ship count must not be negative");
        if (ships == 0) return [];
        int groups = Math.Clamp(waves, 1, ships);
        var result = new int[groups];
        for (int i = 0; i < groups; i++) result[i] = ships / groups + (i < ships % groups ? 1 : 0);
        return result;
    }
}

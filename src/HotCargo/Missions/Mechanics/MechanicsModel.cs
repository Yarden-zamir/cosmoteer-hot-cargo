namespace HotCargo.Missions.Mechanics;

/// <summary>Pure math of the mechanics missions. No game types, so unit tests run on the Mac without the game.</summary>
public static class MechanicsModel
{
    /// <summary>
    /// Spec 132: ships in the next hold-out wave. Base + perTier × tier, rounded, then capped so that the live wave ships stay at maxLive or below.
    /// </summary>
    public static int WaveShipCount(float shipsBase, float shipsPerTier, int tier, int liveShips, int maxLive)
    {
        int wanted = Math.Max(0, (int)MathF.Round(shipsBase + shipsPerTier * tier, MidpointRounding.AwayFromZero));
        return Math.Clamp(wanted, 0, Math.Max(0, maxLive - liveShips));
    }

    /// <summary>Spec 132: tier of each wave ship. Base + perTier × tier, rounded, in 1..maxTier.</summary>
    public static int WaveShipTier(float tierBase, float tierPerTier, int tier, int maxTier) =>
        Math.Clamp((int)MathF.Round(tierBase + tierPerTier * tier, MidpointRounding.AwayFromZero), 1, Math.Max(1, maxTier));

    /// <summary>Hold-out waves that are due after the guarded clock moves from before to after. The first wave is at firstWave, then one every interval.</summary>
    public static int WavesDue(double before, double after, double firstWave, double interval)
    {
        if (interval <= 0) throw new ArgumentOutOfRangeException(nameof(interval), interval, "must be above 0");
        static int Count(double t, double first, double step) => t < first ? 0 : 1 + (int)Math.Floor((t - first) / step);
        return Math.Max(0, Count(after, firstWave, interval) - Count(before, firstWave, interval));
    }

    /// <summary>Wave intervals in a hold of total seconds: total ÷ interval, rounded up, at least 1.</summary>
    public static int Intervals(double total, double interval)
    {
        if (interval <= 0) throw new ArgumentOutOfRangeException(nameof(interval), interval, "must be above 0");
        return Math.Max(1, (int)Math.Ceiling(total / interval));
    }

    /// <summary>
    /// Spec 141: the share of the pay that leaving early keeps. One share for each whole wave interval survived, out of Intervals(total, interval).
    /// </summary>
    public static float EarnedFraction(double guarded, double total, double interval) =>
        Math.Clamp((float)Math.Floor(Math.Max(0, guarded) / interval) / Intervals(total, interval), 0f, 1f);

    /// <summary>Gap between two circles, 0 when they touch or overlap.</summary>
    public static float Gap(float centerDistance, float radiusA, float radiusB) => Math.Max(0f, centerDistance - radiusA - radiusB);

    /// <summary>Whole seconds left on a countdown, rounded up, never below 0.</summary>
    public static int SecondsLeft(double total, double elapsed) => Math.Max(0, (int)Math.Ceiling(total - elapsed));
}

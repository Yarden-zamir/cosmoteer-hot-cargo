namespace HotCargo.Tech;

/// <summary>Pure tech math on plain numbers. No game types.</summary>
public static class TechModel
{
    /// <summary>
    /// Spec 19: value of one unit = ship value × (base fraction + fraction per game hour × hours + fraction per 100 notoriety × notoriety / 100).
    /// Ship value is the highest ship value of the peak tier. Game time and notoriety only grow, and the peak tier never drops,
    /// so the value only goes up. Time and notoriety are rounded down to whole steps, so that a sale in flight sees one price.
    /// The result is rounded to a multiple of 100.
    /// </summary>
    public static long Value(int shipValue, float baseFraction, float fractionPerGameHour, float fractionPer100Notoriety,
        float priceStepMinutes, float notorietyStep, double gameSeconds, float notoriety)
    {
        if (shipValue <= 0) throw new ArgumentOutOfRangeException(nameof(shipValue), "must be above 0");
        double stepSeconds = Math.Max(1.0, priceStepMinutes * 60.0);
        double steppedHours = Math.Floor(Math.Max(0, gameSeconds) / stepSeconds) * stepSeconds / 3600.0;
        double step = Math.Max(1.0, notorietyStep);
        double steppedNotoriety = Math.Floor(Math.Max(0, notoriety) / step) * step;
        double fraction = baseFraction + fractionPerGameHour * steppedHours + fractionPer100Notoriety * steppedNotoriety / 100.0;
        return (long)Math.Round(shipValue * Math.Max(0, fraction) / 100.0) * 100;
    }
}

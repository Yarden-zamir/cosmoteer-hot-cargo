namespace HotCargo.Pursuit;

/// <summary>Why the pacer refuses a big event (spec 905). Ok means it may start.</summary>
public enum PaceVerdict { Ok, RunLimit, KindGap, GlobalGap }

/// <summary>One big event that started: its kind and the run game seconds of its start.</summary>
public readonly record struct PaceStart(string Kind, double At);

/// <summary>Pure math of the Pursuit (spec 900 to 949). No game types, so the unit tests run on the Mac.</summary>
public static class PursuitModel
{
    public static float Lerp(float early, float late, float progress) => early + (late - early) * Math.Clamp(progress, 0f, 1f);

    /// <summary>
    /// Spec 902: the fleet factor on hunter strength. A fleet worth the reference value gives 1. Each doubling adds perDoubling,
    /// each halving takes it off, clamped to 1 ± max. A fleet without value gives 1 − max.
    /// </summary>
    public static float FleetFactor(double fleetValue, double referenceValue, float perDoubling, float max)
    {
        if (!(referenceValue > 0)) throw new ArgumentOutOfRangeException(nameof(referenceValue), referenceValue, "reference value must be above 0");
        if (fleetValue <= 0) return 1f - max;
        float step = perDoubling * (float)Math.Log2(fleetValue / referenceValue);
        return 1f + Math.Clamp(step, -max, max);
    }

    /// <summary>
    /// Spec 901: count × factor. The whole part always spawns, the fractional part is the chance of one more ship.
    /// A count of 1 or more never drops to 0, so a stage always sends someone. <paramref name="roll"/> returns [0, 1) and runs only for a fraction.
    /// </summary>
    public static int ScaleCount(int count, float factor, Func<double> roll)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (!(factor > 0)) throw new ArgumentOutOfRangeException(nameof(factor), factor, "factor must be above 0");
        double exact = count * (double)factor;
        double whole = Math.Floor(exact + 1e-9);
        double fraction = exact - whole;
        int n = (int)whole + (fraction > 1e-6 && roll() < fraction ? 1 : 0);
        return count > 0 ? Math.Max(1, n) : 0;
    }

    /// <summary>
    /// Spec 905: may a big event of this kind start now? It needs fewer than maxPerRun starts of the kind, kindGap seconds since the
    /// last start of the kind and globalGap seconds since the last start of any kind.
    /// </summary>
    public static PaceVerdict MayStart(IReadOnlyList<PaceStart> history, string kind, double now, int maxPerRun, double kindGap, double globalGap)
    {
        int count = 0;
        double lastKind = double.NegativeInfinity, lastAny = double.NegativeInfinity;
        foreach (var s in history)
        {
            lastAny = Math.Max(lastAny, s.At);
            if (s.Kind != kind) continue;
            count++;
            lastKind = Math.Max(lastKind, s.At);
        }
        if (count >= maxPerRun) return PaceVerdict.RunLimit;
        if (now - lastKind < kindGap) return PaceVerdict.KindGap;
        if (now - lastAny < globalGap) return PaceVerdict.GlobalGap;
        return PaceVerdict.Ok;
    }

    /// <summary>Spec 920: a natural outage can roll in sectors minSector up to the sector before the last one (the Buyer).</summary>
    public static bool OutageSector(int sector, int runSectors, int minSector) => sector >= minSector && sector < runSectors;

    /// <summary>
    /// Expected events with one roll of chance p in each of n tries, capped at max. Shows that the outage default gives about 2 a run
    /// (spec 920): 6 tries at 0.5 with cap 2 give 1.88.
    /// </summary>
    public static double ExpectedCapped(int tries, double p, int max)
    {
        double expected = 0;
        for (int k = 0; k <= tries; k++)
            expected += Math.Min(k, max) * Binomial(tries, k) * Math.Pow(p, k) * Math.Pow(1 - p, tries - k);
        return expected;
    }

    static double Binomial(int n, int k)
    {
        double r = 1;
        for (int i = 1; i <= k; i++) r = r * (n - k + i) / i;
        return r;
    }

    /// <summary>Spec 911: how far the scout got, 0 at its start distance to the relay, 1 at the relay.</summary>
    public static float ReportProgress(float startDistance, float distance) =>
        startDistance <= 0 ? 1f : Math.Clamp(1f - distance / startDistance, 0f, 1f);

    /// <summary>A text bar of width cells: filled cells "#", empty cells "-". Example: 0.5 with width 10 = "#####-----".</summary>
    public static string Bar(float fraction, int width)
    {
        int filled = (int)MathF.Round(Math.Clamp(fraction, 0f, 1f) * width, MidpointRounding.AwayFromZero);
        return new string('#', filled) + new string('-', width - filled);
    }

    /// <summary>Seconds as m:ss, rounded up, never below 0:00. Example: 221.2 = "3:42".</summary>
    public static string Clock(double seconds)
    {
        int s = Math.Max(0, (int)Math.Ceiling(seconds));
        return $"{s / 60}:{s % 60:00}";
    }

    /// <summary>Spec 931: the hacker price, share × the ship value maximum of the tier, rounded to roundTo (at least 1).</summary>
    public static int HackerPrice(int tierValueMaximum, float share, int roundTo)
    {
        int step = Math.Max(1, roundTo);
        return (int)Math.Max(step, Math.Round(tierValueMaximum * (double)share / step, MidpointRounding.AwayFromZero) * step);
    }
}

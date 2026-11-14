namespace HotCargo.Scaling;

/// <summary>Pure math of the co-op enemy scaling (spec 330 to 336). No game types, so it is testable on the Mac.</summary>
public static class ScalingModel
{
    public const float DefaultSetting = 1.5f;
    public const float MinSetting = 1f;
    public const float MaxSetting = 2.5f;
    /// <summary>The slider moves in tenths.</summary>
    public const int StepsPerUnit = 10;
    public const float SettingStep = 1f / StepsPerUnit;

    /// <summary>A slider or save value as a clean step value (1.1f is 1.10000002f), so counts do not round up by float noise.</summary>
    public static double Snap(float setting)
    {
        if (!(setting >= MinSetting && setting <= MaxSetting))
            throw new ArgumentOutOfRangeException(nameof(setting), setting, $"enemy scaling setting must be {MinSetting} to {MaxSetting}");
        return Math.Round(setting * (double)StepsPerUnit) / StepsPerUnit;
    }

    /// <summary>
    /// Spec 331: the enemy multiplier for a player count. The setting is the multiplier with 2 players, and each extra player
    /// adds the same step: 1 + (setting − 1) × (players − 1). Solo (and a game without human players) is 1.
    /// </summary>
    public static double Factor(float setting, int players)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(players);
        double s = Snap(setting);
        return players <= 1 ? 1 : 1 + (s - 1) * (players - 1);
    }

    /// <summary>
    /// Spec 333: count × factor. The whole part always spawns; the fractional part is the chance of one more ship.
    /// <paramref name="roll"/> returns a number in [0, 1). It runs only when there is a fractional part, so factor 1 uses no random.
    /// </summary>
    public static int Scale(int count, double factor, Func<double> roll)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (!(factor >= 1)) throw new ArgumentOutOfRangeException(nameof(factor), factor, "enemy factor must be at least 1");
        double exact = count * factor;
        double whole = Math.Floor(exact + 1e-9);
        double fraction = exact - whole;
        return (int)whole + (fraction > 1e-6 && roll() < fraction ? 1 : 0);
    }

    /// <summary>
    /// Spec 332: a scaled group places factor × ships in the same area, so its spacing from its own tag shrinks by √factor.
    /// Without this, the game stops a group at the first ship that finds no place (SimObjectSpawner.DoSpawn), and a full
    /// system caps the count.
    /// </summary>
    public static float Spacing(float distance, double factor)
    {
        if (!(distance >= 0)) throw new ArgumentOutOfRangeException(nameof(distance), distance, "spacing must be 0 or more");
        if (!(factor >= 1)) throw new ArgumentOutOfRangeException(nameof(factor), factor, "enemy factor must be at least 1");
        return (float)(distance / Math.Sqrt(factor));
    }

    /// <summary>
    /// Spec 335: a cap that the game checks again every tick (police) rounds half up, without a roll, so it stays the same between ticks.
    /// </summary>
    public static int Cap(int count, double factor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (!(factor >= 1)) throw new ArgumentOutOfRangeException(nameof(factor), factor, "enemy factor must be at least 1");
        return (int)Math.Floor(count * factor + 0.5 + 1e-9);
    }
}

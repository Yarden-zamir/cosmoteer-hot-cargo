namespace HotCargo.Missions;

/// <summary>Pure mission math on plain numbers. No game types, so unit tests run on the Mac without the game.</summary>
public static class MissionModel
{
    /// <summary>Spec requirement 39: chance that a shady mission is a trap, clamped to 0..1.</summary>
    public static float TrapChance(float chanceBase, float chancePerHeat, float heat) =>
        Math.Clamp(chanceBase + chancePerHeat * heat, 0f, 1f);

    /// <summary>A roll in [0, 1) below the chance is a trap. Chance 0 is never a trap, chance 1 is always a trap.</summary>
    public static bool IsTrap(float roll01, float chance)
    {
        if (roll01 is < 0f or >= 1f) throw new ArgumentOutOfRangeException(nameof(roll01), roll01, "must be in [0, 1)");
        return roll01 < chance;
    }

    /// <summary>Scales a reward amount and rounds it to the nearest multiple. A positive amount never scales below one multiple.</summary>
    public static int ScaleReward(int amount, float factor, int roundToMultiple)
    {
        if (roundToMultiple < 1) throw new ArgumentOutOfRangeException(nameof(roundToMultiple), roundToMultiple, "must be 1 or more");
        if (factor < 0f) throw new ArgumentOutOfRangeException(nameof(factor), factor, "must not be negative");
        int scaled = (int)Math.Round(amount * (double)factor / roundToMultiple, MidpointRounding.AwayFromZero) * roundToMultiple;
        return amount > 0 && factor > 0f ? Math.Max(roundToMultiple, scaled) : scaled;
    }
}

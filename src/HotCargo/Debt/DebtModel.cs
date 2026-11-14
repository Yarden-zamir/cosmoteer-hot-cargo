namespace HotCargo.Debt;

/// <summary>Spec 1406: the stage of a debt by its ratio. The integer values are saved.</summary>
public enum DebtStage { Calm = 0, Firm = 1, Hostile = 2 }

/// <summary>The kinds of debt choices (spec 1413). The integer values travel in shared choice options.</summary>
public enum DebtChoice { Loan = 1, Pay = 2, NotNow = 3, Refinance = 4, HelpPay = 5, HelpTab = 6, HelpClaim = 7 }

/// <summary>The pay slider of a page in hundreds of credits (spec 1405).</summary>
public readonly record struct PayRange(int Min, int Max, int Step);

/// <summary>Pure math of the debt (spec 1400 to 1419). No game types.</summary>
public static class DebtModel
{
    /// <summary>Spec 1413: options pack the kind and an amount in hundreds of credits, so amounts stay below this.</summary>
    public const int Span = 100_000_000;

    /// <summary>Spec 1404: one interest step, rounded up to whole credits.</summary>
    public static long Interest(long balance, float rate) =>
        // decimal: (decimal)0.05f is exactly 0.05, so 10,000 gives 10,500 and not 10,501.
        balance > 0 ? (long)Math.Ceiling(balance * (1m + (decimal)rate)) : 0;

    public static float Ratio(long balance, long principal) =>
        principal > 0 ? (float)balance / principal : throw new ArgumentOutOfRangeException(nameof(principal), principal, "a debt needs a principal above 0");

    /// <summary>Spec 1406.</summary>
    public static DebtStage Stage(float ratio, float firmRatio, float hostileRatio) =>
        ratio >= hostileRatio ? DebtStage.Hostile : ratio >= firmRatio ? DebtStage.Firm : DebtStage.Calm;

    public static int Hundreds(long credits) => (int)Math.Min(int.MaxValue, (credits + 99) / 100);

    /// <summary>
    /// Spec 1405: the slider from one share of the balance to the whole balance, in steps of that share, no higher than the money.
    /// Null when the chooser cannot pay one step. The top value rounds up, so it pays the exact balance (the apply clamps it).
    /// </summary>
    public static PayRange? PayRange(long balance, long money, float stepShare)
    {
        if (balance <= 0) return null;
        int all = Hundreds(balance);
        int step = Math.Clamp(Hundreds((long)Math.Ceiling(balance * (double)stepShare)), 1, all);
        int max = money >= balance ? all : (int)Math.Min(all, money / 100);
        return max < step ? null : new PayRange(step, max, step);
    }

    /// <summary>Spec 1405: the credits that a pay option takes: its hundreds, never more than the balance.</summary>
    public static long PayCredits(int hundreds, long balance) => Math.Min(balance, hundreds * 100L);

    public static int Option(DebtChoice kind, int amount) =>
        amount is >= 0 and < Span ? (int)kind * Span + amount : throw new ArgumentOutOfRangeException(nameof(amount), amount, "amount outside the option span");

    public static (DebtChoice Kind, int Amount) Decode(int option) =>
        Enum.IsDefined((DebtChoice)(option / Span)) ? ((DebtChoice)(option / Span), option % Span)
            : throw new ArgumentOutOfRangeException(nameof(option), option, "no debt choice");

    /// <summary>Spec 1411: the score minus the debt factor × the sum of the balances.</summary>
    public static long Score(long score, long debt, float factor) => score - (long)Math.Ceiling(debt * (double)factor);

    /// <summary>Spec 1420: a pay-off reward of this many credits takes what it can off the balance; the rest pays as money.</summary>
    public static (long Off, long Money) PayoffSplit(long amount, long balance) =>
        amount >= 0 && balance >= 0 ? (Math.Min(amount, balance), amount - Math.Min(amount, balance))
            : throw new ArgumentOutOfRangeException(nameof(amount), (amount, balance), "a pay-off needs amounts of 0 or more");

    /// <summary>Spec 1432: the helpers' share of the damage on the players' enemies. 0 when nobody hit anything.</summary>
    public static float Share(long helperDamage, long playerDamage) =>
        helperDamage < 0 || playerDamage < 0 ? throw new ArgumentOutOfRangeException(nameof(helperDamage), (helperDamage, playerDamage), "damage below 0")
        : helperDamage + playerDamage == 0 ? 0f : (float)((double)helperDamage / (helperDamage + playerDamage));

    /// <summary>Spec 1434: the price in event money units.</summary>
    public static float HelpPriceUnits(float share, float pricePerShare) => share * pricePerShare;

    /// <summary>Spec 1435: "we didn't ask for help" works when the helpers did less than this share.</summary>
    public static bool ClaimWorks(float share, float claimMaxShare) => share < claimMaxShare;

    public static int Percent(float share) => (int)MathF.Round(share * 100f);

    /// <summary>Spec 1402: the loan offer roll of a station, from a value in [0, 1).</summary>
    public static bool Offers(float roll, float chance) => roll < chance;
}

namespace HotCargo.Relays;

/// <summary>The kinds of relay lockdown (spec 952). The integer values are saved.</summary>
public enum LockKind
{
    None = 0,
    /// <summary>The system's faction closes the relays.</summary>
    Closed = 1,
    /// <summary>The system's faction lets ships through for a fee.</summary>
    Fee = 2,
    /// <summary>Pirates hold the relays.</summary>
    Pirates = 3,
}

/// <summary>Pure math of relay lockdowns (spec 951 to 958). No game types.</summary>
public static class RelaysModel
{
    /// <summary>
    /// Spec 952: the kind for a roll in [0, 1). A system without a controlling faction can only get pirates.
    /// Returns None when every allowed weight is 0.
    /// </summary>
    public static LockKind PickKind(float roll, float closed, float fee, float pirates, bool hasFaction)
    {
        if (!hasFaction) { closed = 0f; fee = 0f; }
        float total = closed + fee + pirates;
        if (total <= 0f) return LockKind.None;
        float x = roll * total;
        if (x < closed) return LockKind.Closed;
        if (x < closed + fee) return LockKind.Fee;
        return pirates > 0f ? LockKind.Pirates : fee > 0f ? LockKind.Fee : LockKind.Closed;
    }

    /// <summary>Spec 956: the fee in money units at a heat.</summary>
    public static float FeeUnits(float baseUnits, float perHeat, float heat) => Math.Max(0f, baseUnits + perHeat * Math.Max(0f, heat));

    /// <summary>Spec 958: the chance that the pirates take an offer. 0 up to minShare of the price, then a straight line to 1 at the full price.</summary>
    public static float BribeChance(long offer, long price, float minShare)
    {
        if (price <= 0) return 1f;
        float share = (float)offer / price;
        if (share >= 1f) return 1f;
        if (share <= minShare) return 0f;
        return (share - minShare) / (1f - minShare);
    }

    /// <summary>Spec 958: the bribe value of an amount of a resource.</summary>
    public static long ResourceValue(int amount, int buyPrice, float rate) => (long)Math.Floor(Math.Max(0, amount) * (double)Math.Max(0, buyPrice) * rate);

    /// <summary>Spec 955: a closed relay opens after closedSeconds of the visit. 0 means never.</summary>
    public static bool ClosedOpens(double visitSeconds, float closedSeconds) => closedSeconds > 0f && visitSeconds >= closedSeconds;
}

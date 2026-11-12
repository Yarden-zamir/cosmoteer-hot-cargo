namespace HotCargo.Diplomacy;

/// <summary>A pile of tech: one vault at a position, with the units left in it.</summary>
public readonly record struct TechPile(float X, float Y, int Units);

/// <summary>
/// Pure math of hails and deals (spec 560 to 589). No game types, so the unit tests run on the Mac.
/// Every chance comes out clamped to 0..1. Leverage is -1 (the other side is 4 times stronger) to +1 (the players are).
/// </summary>
public static class DiplomacyModel
{
    public static float Clamp01(float v) => float.IsNaN(v) ? throw new ArgumentOutOfRangeException(nameof(v)) : Math.Clamp(v, 0f, 1f);

    /// <summary>log4 of the strength ratio, clamped to -1..1. A side with no strength loses all leverage.</summary>
    public static float Leverage(float players, float other)
    {
        if (players < 0 || other < 0) throw new ArgumentOutOfRangeException(nameof(players));
        if (other == 0) return players == 0 ? 0f : 1f;
        if (players == 0) return -1f;
        return Math.Clamp(MathF.Log(players / other) / MathF.Log(4f), -1f, 1f);
    }

    /// <summary>Share of the stock that the largest pile still holds after a hand-over. 0 when nothing is left.</summary>
    public static float Concentration(IReadOnlyList<TechPile> left, int stock, float pileDistance)
    {
        if (stock <= 0) return 0f;
        int n = left.Count;
        var group = new int[n];
        for (int i = 0; i < n; i++) group[i] = i;
        int Find(int i) { while (group[i] != i) i = group[i] = group[group[i]]; return i; }
        float d2 = pileDistance * pileDistance;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                float dx = left[i].X - left[j].X, dy = left[i].Y - left[j].Y;
                if (dx * dx + dy * dy <= d2) group[Find(i)] = Find(j);
            }
        var sums = new Dictionary<int, int>();
        for (int i = 0; i < n; i++) sums[Find(i)] = sums.GetValueOrDefault(Find(i)) + left[i].Units;
        return sums.Count == 0 ? 0f : Math.Min(1f, sums.Values.Max() / (float)stock);
    }

    /// <summary>Hand over <paramref name="units"/> of <paramref name="stock"/>. All units is the top chance; 1 unit of many the bottom.</summary>
    public static float HandOverChance(int units, int stock, float concentration, float leverage, float min, float max, float visiblePenalty, float perLeverage)
    {
        if (units <= 0 || units > stock) throw new ArgumentOutOfRangeException(nameof(units));
        float share = stock == 1 ? 1f : (units - 1) / (float)(stock - 1);
        return Clamp01(min + (max - min) * share - visiblePenalty * concentration + perLeverage * leverage);
    }

    /// <summary>"That's all we have": works more often for a big share and when the rest is spread out.</summary>
    public static float BluffChance(int units, int stock, float concentration, float baseChance, float perShare, float visiblePenalty)
    {
        if (units <= 0 || units > stock) throw new ArgumentOutOfRangeException(nameof(units));
        return Clamp01(baseChance + perShare * units / stock - visiblePenalty * concentration);
    }

    /// <summary>Pay instead of tech. Alone: full chance. More other hunters than the limit: none. Linear between, times the share paid for.</summary>
    public static float DealChance(int otherHunters, int maxOthers, int units, int stock, float alone, float shareMin)
    {
        if (otherHunters < 0 || maxOthers < 0) throw new ArgumentOutOfRangeException(nameof(otherHunters));
        if (units <= 0 || units > stock) throw new ArgumentOutOfRangeException(nameof(units));
        if (otherHunters > maxOthers) return 0f;
        float crowd = 1f - otherHunters / (float)(maxOthers + 1);
        float share = stock == 1 ? 1f : shareMin + (1f - shareMin) * (units - 1) / (stock - 1);
        return Clamp01(alone * crowd * share);
    }

    /// <summary>Inputs of the players' demand that an enemy surrenders.</summary>
    public readonly record struct SurrenderInputs(float Leverage, float Damage, float Fame, float Reputation, int Allies, bool Wanted, bool Hunter, float Heat);

    public static float SurrenderChance(SurrenderInputs s, float baseChance, float perLeverage, float perDamage, float perFame, float perReputation,
        float perAlly, float wantedPenalty, float hunterPerHeat) =>
        Clamp01(baseChance + perLeverage * s.Leverage + perDamage * s.Damage + perFame * s.Fame + perReputation * s.Reputation
                - perAlly * s.Allies - (s.Wanted ? wantedPenalty : 0f) - (s.Hunter ? hunterPerHeat * s.Heat / 100f : 0f));

    /// <summary>The enemy accepts the players' surrender: likelier when it is stronger (negative leverage) and likes the players.</summary>
    public static float OfferChance(float leverage, float reputation, bool wanted, float baseChance, float perLeverage, float perReputation, float wantedPenalty) =>
        Clamp01(baseChance - perLeverage * leverage + perReputation * reputation - (wanted ? wantedPenalty : 0f));

    public static float HoldChance(float reputation, float heat, bool huntersInSystem, int units, float baseChance, float perReputation, float perHeat,
        float huntersPenalty, float perUnit) =>
        Clamp01(baseChance + perReputation * reputation - perHeat * heat / 100f - (huntersInSystem ? huntersPenalty : 0f) - perUnit * units);

    public static float FeeChance(float heat, float reputation, float baseChance, float perHeat, float perReputation) =>
        Clamp01(baseChance + perHeat * heat / 100f + perReputation * reputation);

    public static float BetrayChance(float heat, int hunters, float reputation, bool feePaid, float baseChance, float perHeat, float perHunter,
        float perReputation, float feePaidChange) =>
        Clamp01(baseChance + perHeat * heat / 100f + perHunter * hunters + perReputation * reputation + (feePaid ? feePaidChange : 0f));

    /// <summary>Help for pay. Never while wanted. The offer counts in fair prices, up to 1.5 of them.</summary>
    public static float HireChance(int offer, int fair, float reputation, float heat, bool wanted, float baseChance, float perOffer, float perReputation, float perHeat)
    {
        if (offer < 0 || fair <= 0) throw new ArgumentOutOfRangeException(nameof(offer));
        if (wanted) return 0f;
        return Clamp01(baseChance + perOffer * Math.Min(1.5f, offer / (float)fair) + perReputation * reputation - perHeat * heat / 100f);
    }

    /// <summary>Reputation as -1..1: reputation over the scale, clamped.</summary>
    public static float ReputationShare(int reputation, float scale) =>
        scale <= 0 ? throw new ArgumentOutOfRangeException(nameof(scale)) : Math.Clamp(reputation / scale, -1f, 1f);

    /// <summary>A roll succeeds when it is below the chance. Chance 0 never succeeds, chance 1 always does.</summary>
    public static bool Succeeds(float roll, float chance) => roll < chance;
}

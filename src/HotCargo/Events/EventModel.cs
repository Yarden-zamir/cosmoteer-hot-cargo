namespace HotCargo.Events;

/// <summary>What an event or a choice needs. Zero or empty means "no need". Money and tech value parts are in final credits.</summary>
public sealed record Need
{
    public int Money { get; init; }
    public int Tech { get; init; }
    public int VaultSpace { get; init; }
    public int Crew { get; init; }
    public float MinHeat { get; init; }
    /// <summary>Heat must stay below this. Zero means no limit.</summary>
    public float MaxHeat { get; init; }
    public int MinSector { get; init; }
    /// <summary>Zero means no limit.</summary>
    public int MaxSector { get; init; }
    public IReadOnlyList<(string Id, int Amount)> Resources { get; init; } = [];
    public IReadOnlyList<string> Parts { get; init; } = [];
    public IReadOnlyList<string> Flags { get; init; } = [];
    public IReadOnlyList<string> NotFlags { get; init; } = [];
    /// <summary>Each flag must be set and at least MinSectors sectors old (sector index now minus the sector where it was set).</summary>
    public IReadOnlyList<(string Flag, int MinSectors)> FlagAges { get; init; } = [];
    /// <summary>Sectors left before the Buyer's sector (RunSectorCount minus the sector index).</summary>
    public int MinSectorsLeft { get; init; }
    /// <summary>-1 means no limit.</summary>
    public int MaxSectorsLeft { get; init; } = -1;
    /// <summary>True: only while a faction in this system wants the players (vanilla wanted status).</summary>
    public bool Wanted { get; init; }
}

/// <summary>The fleet as the conditions see it. Built from sim state, or by hand in unit tests.</summary>
public sealed record Snapshot
{
    public int Money { get; init; }
    public int Tech { get; init; }
    public int VaultSpace { get; init; }
    public int Crew { get; init; }
    public float Heat { get; init; }
    public int Sector { get; init; }
    public IReadOnlyDictionary<string, int> Resources { get; init; } = new Dictionary<string, int>();
    public IReadOnlySet<string> Parts { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> Flags { get; init; } = new HashSet<string>();
    /// <summary>Sector index at which each flag was set.</summary>
    public IReadOnlyDictionary<string, int> FlagSectors { get; init; } = new Dictionary<string, int>();
    public int SectorsLeft { get; init; }
    public bool Wanted { get; init; }
}

/// <summary>A research trade (spec 760): techs for tech units. Value = the sum of the techs' research costs.</summary>
public sealed record ResearchTrade(IReadOnlyList<string> Techs, int Units, long Value);

/// <summary>A research sale (spec 762): copies of researched techs for money above their worth.</summary>
public sealed record ResearchSale(IReadOnlyList<string> Techs, long Worth, int Price);

/// <summary>The first need that the fleet does not meet.</summary>
public enum UnmetKind { Money, Tech, VaultSpace, Crew, MinHeat, MaxHeat, MinSector, MaxSector, Resource, Part, Flag, NotFlag, FlagAge, MinSectorsLeft, MaxSectorsLeft, NotWanted,
    /// <summary>A submit: no option is selected (Amount 0), or the selected options give too few points (Amount = the points needed).</summary>
    Points }

public sealed record Unmet(UnmetKind Kind, string Id = "", float Amount = 0);

/// <summary>Pure event math on plain numbers. No game types, so unit tests run on the Mac without the game.</summary>
public static class EventModel
{
    /// <summary>The first need that the snapshot does not meet, or null when all are met. Checks run in a fixed order.</summary>
    public static Unmet? FirstUnmet(Need need, Snapshot s)
    {
        if (s.Money < need.Money) return new(UnmetKind.Money, Amount: need.Money);
        if (s.Tech < need.Tech) return new(UnmetKind.Tech, Amount: need.Tech);
        if (s.VaultSpace < need.VaultSpace) return new(UnmetKind.VaultSpace, Amount: need.VaultSpace);
        if (s.Crew < need.Crew) return new(UnmetKind.Crew, Amount: need.Crew);
        if (s.Heat < need.MinHeat) return new(UnmetKind.MinHeat, Amount: need.MinHeat);
        if (need.MaxHeat > 0 && s.Heat >= need.MaxHeat) return new(UnmetKind.MaxHeat, Amount: need.MaxHeat);
        if (s.Sector < need.MinSector) return new(UnmetKind.MinSector, Amount: need.MinSector);
        if (need.MaxSector > 0 && s.Sector > need.MaxSector) return new(UnmetKind.MaxSector, Amount: need.MaxSector);
        foreach (var (id, amount) in need.Resources)
            if (s.Resources.GetValueOrDefault(id) < amount) return new(UnmetKind.Resource, id, amount);
        foreach (string part in need.Parts)
            if (!s.Parts.Contains(part)) return new(UnmetKind.Part, part);
        foreach (string flag in need.Flags)
            if (!s.Flags.Contains(flag)) return new(UnmetKind.Flag, flag);
        foreach (string flag in need.NotFlags)
            if (s.Flags.Contains(flag)) return new(UnmetKind.NotFlag, flag);
        foreach (var (flag, minSectors) in need.FlagAges)
            if (!s.Flags.Contains(flag) || !s.FlagSectors.TryGetValue(flag, out int setAt) || s.Sector - setAt < minSectors)
                return new(UnmetKind.FlagAge, flag, minSectors);
        if (s.SectorsLeft < need.MinSectorsLeft) return new(UnmetKind.MinSectorsLeft, Amount: need.MinSectorsLeft);
        if (need.MaxSectorsLeft >= 0 && s.SectorsLeft > need.MaxSectorsLeft) return new(UnmetKind.MaxSectorsLeft, Amount: need.MaxSectorsLeft);
        if (need.Wanted && !s.Wanted) return new(UnmetKind.NotWanted);
        return null;
    }

    /// <summary>A need plus a sure cost: the fleet must be able to pay the cost on top of the need. Resource amounts add up by ID.</summary>
    public static Need Plus(Need need, int money, int tech, int crew, IEnumerable<(string Id, int Amount)> resources)
    {
        var sums = new List<(string Id, int Amount)>(need.Resources);
        foreach (var (id, amount) in resources)
        {
            int i = sums.FindIndex(r => r.Id == id);
            if (i < 0) sums.Add((id, amount));
            else sums[i] = (id, sums[i].Amount + amount);
        }
        return need with { Money = need.Money + money, Tech = need.Tech + tech, Crew = need.Crew + crew, Resources = sums };
    }

    /// <summary>Weight of an outcome or an event after the heat and tech terms. Never below 0.</summary>
    public static float EffectiveWeight(float weight, float perHeat, float perTech, float heat, int tech) =>
        Math.Max(0f, weight + perHeat * heat + perTech * tech);

    /// <summary>
    /// Picks an index with a roll in [0, 1). Each index wins in proportion to its weight.
    /// Returns -1 when all weights are 0 (nothing to pick).
    /// </summary>
    public static int PickWeighted(IReadOnlyList<float> weights, float roll01)
    {
        if (roll01 is < 0f or >= 1f) throw new ArgumentOutOfRangeException(nameof(roll01), roll01, "must be in [0, 1)");
        float total = 0f;
        foreach (float w in weights)
        {
            if (w < 0f || float.IsNaN(w)) throw new ArgumentOutOfRangeException(nameof(weights), w, "weights must be 0 or more");
            total += w;
        }
        if (total <= 0f) return -1;
        float target = roll01 * total;
        int last = -1;
        for (int i = 0; i < weights.Count; i++)
        {
            if (weights[i] <= 0f) continue;
            last = i;
            if (target < weights[i]) return i;
            target -= weights[i];
        }
        // Float rounding can leave a tiny rest after the last positive weight.
        return last;
    }

    /// <summary>
    /// Credits for an amount in event money units: units × the standard mission pay of the tier × the reward factors,
    /// plus techUnits × the tech value. Rounded to the nearest multiple of roundTo, away from zero.
    /// </summary>
    public static int Credits(float units, float standardPay, float rewardFactor, float techUnits, long techValue, int roundTo)
    {
        if (roundTo < 1) throw new ArgumentOutOfRangeException(nameof(roundTo), roundTo, "must be 1 or more");
        double raw = units * (double)standardPay * rewardFactor + techUnits * (double)techValue;
        long rounded = (long)Math.Round(raw / roundTo, MidpointRounding.AwayFromZero) * roundTo;
        return (int)Math.Clamp(rounded, int.MinValue, int.MaxValue);
    }

    /// <summary>Money units of a cost that grows with heat: base + perHeat × heat. Never below 0.</summary>
    public static float HeatScaledUnits(float units, float perHeat, float heat) => Math.Max(0f, units + perHeat * heat);

    /// <summary>The Buyer price multiplier after an advance: the old one × the factor, kept in [0, 1].</summary>
    public static float BuyerPriceAfter(float current, float factor)
    {
        if (factor is <= 0f or > 1f) throw new ArgumentOutOfRangeException(nameof(factor), factor, "must be above 0 and at most 1");
        return Math.Clamp(current * factor, 0f, 1f);
    }

    /// <summary>
    /// Builds a research trade from candidate techs (id, research cost) in the given order (spec 760).
    /// The first candidate that fits maxUnits sets the units: ceil(cost / (techValue × rate)). More candidates join, in order,
    /// while the total stays within units × techValue × rate and there are at most maxTechs. The trade is valid only when the
    /// total is at least units × techValue × minRate, so it always beats buying the research. Returns null when no candidate works.
    /// </summary>
    public static ResearchTrade? BuildResearchTrade(IReadOnlyList<(string Id, long Cost)> candidates, long techValue, float rate, float minRate, int maxUnits, int maxTechs)
    {
        if (techValue <= 0 || rate <= 0f || maxUnits < 1 || maxTechs < 1) return null;
        double perUnit = techValue * (double)rate;
        for (int first = 0; first < candidates.Count; first++)
        {
            var (id, cost) = candidates[first];
            if (cost <= 0 || cost > maxUnits * perUnit) continue;
            int units = Math.Max(1, (int)Math.Ceiling(cost / perUnit));
            double budget = units * perUnit;
            var techs = new List<string> { id };
            long total = cost;
            for (int i = 0; i < candidates.Count && techs.Count < maxTechs; i++)
            {
                if (i == first || candidates[i].Cost <= 0 || total + candidates[i].Cost > budget) continue;
                techs.Add(candidates[i].Id);
                total += candidates[i].Cost;
            }
            if (total >= units * (double)techValue * minRate) return new ResearchTrade(techs, units, total);
        }
        return null;
    }

    /// <summary>
    /// Builds a research sale from researched techs in the given order (spec 762): techs join while the price
    /// (total worth × premium) stays within the cap and there are at most maxTechs. Returns null when not even one fits.
    /// </summary>
    public static ResearchSale? BuildResearchSale(IReadOnlyList<(string Id, long Cost)> researched, float premium, long priceCap, int maxTechs)
    {
        if (premium <= 0f || priceCap <= 0 || maxTechs < 1) return null;
        var techs = new List<string>();
        long total = 0;
        foreach (var (id, cost) in researched)
        {
            if (techs.Count >= maxTechs) break;
            if (cost <= 0 || (total + cost) * (double)premium > priceCap) continue;
            techs.Add(id);
            total += cost;
        }
        return techs.Count == 0 ? null : new ResearchSale(techs, total, (int)Math.Round(total * (double)premium));
    }

    /// <summary>Shuffles a list in place with a roll source (Fisher-Yates). The caller gives a seeded, deterministic source.</summary>
    public static void Shuffle<T>(IList<T> list, Func<int, int> nextBelow)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = nextBelow(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// The wanted-and-hunted warning shows when both are true (spec 470): a faction wants the players, and heat reached the stage.
    /// It fires on the rising edge only: warnedBefore is the saved state from the last tick.
    /// </summary>
    public static (bool Fire, bool WarnedNow) WantedWarning(bool wanted, int stagesStarted, int minStage, bool warnedBefore)
    {
        bool both = wanted && stagesStarted >= Math.Max(1, minStage);
        return (both && !warnedBefore, both);
    }

    // ---- Page options (spec 810 to 812)

    /// <summary>Size of one option unit at a tier and heat: base + perTier × (tier − 1) + perHeat × heat. Never below 0.</summary>
    public static float UnitSize(float unit, float perTier, float perHeat, int tier, float heat) =>
        Math.Max(0f, unit + perTier * Math.Max(0, tier - 1) + perHeat * heat);

    /// <summary>Whole items (crates, crew, resource amount) for units of an option: units × unit size, rounded half away from zero.</summary>
    public static int Whole(float units, float unitSize)
    {
        if (units < 0 || unitSize < 0 || float.IsNaN(units) || float.IsNaN(unitSize)) throw new ArgumentOutOfRangeException(nameof(units), "units and unit size must not be negative");
        return (int)MathF.Round(units * unitSize, MidpointRounding.AwayFromZero);
    }

    /// <summary>A submit's need: needPoints + perHeat × heat.</summary>
    public static float NeedPoints(float needPoints, float perHeat, float heat) => needPoints + perHeat * heat;

    /// <summary>True when the points reach the need. A small tolerance covers float steps (ten steps of 0.1 give 0.99999).</summary>
    public static bool Enough(float points, float need) => points >= need - 1e-4f;

    /// <summary>Chance of an option's risk effects: chance per unit × units, kept in 0..1.</summary>
    public static float RiskChance(float perUnit, float units) => Math.Clamp(perUnit * units, 0f, 1f);

    /// <summary>The offer in percent of a need, for the page's offer line. A need of 0 counts as fully met.</summary>
    public static int OfferPercent(float points, float need) => need <= 0 ? 100 : (int)MathF.Round(100f * points / need);

    /// <summary>True when an event is allowed again: it was never seen, or maxPerRun is 0 (no limit), or it was seen less often than maxPerRun.</summary>
    public static bool UnderRunLimit(int seenCount, int maxPerRun) => maxPerRun <= 0 || seenCount < maxPerRun;

    /// <summary>True when the cooldown since the last event has passed. A negative last time means no event yet.</summary>
    public static bool CooldownOver(double nowSeconds, double lastEventSeconds, float cooldownSeconds) =>
        lastEventSeconds < 0 || nowSeconds - lastEventSeconds >= cooldownSeconds;
}

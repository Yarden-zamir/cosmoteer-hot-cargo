namespace HotCargo.Tactics;

/// <summary>
/// Spec 1305. Where a ship that falls back repairs. None: no plan picked yet.
/// Hook for spec 1312: a Scavenge plan goes between Donor and Station when the scavenge behavior lands.
/// </summary>
public enum RepairPlan { None, Cover, Donor, Station, Away }

/// <summary>What the ship can reach when it picks a plan.</summary>
public readonly record struct RepairOptions(bool HasRock, bool HasDonor, bool HasStation);

/// <summary>Pure repair plan decisions (spec 1305-1309) on plain numbers. No game types.</summary>
public static class RepairModel
{
    /// <summary>Spec 1305: the first plan that applies, in the order cover, donor, station, away.</summary>
    public static RepairPlan PickPlan(bool lacksResources, bool shortOfCrew, RepairOptions o)
    {
        if (!lacksResources && !shortOfCrew && o.HasRock) return RepairPlan.Cover;
        if (lacksResources && o.HasDonor) return RepairPlan.Donor;
        if ((lacksResources || shortOfCrew) && o.HasStation) return RepairPlan.Station;
        return RepairPlan.Away;
    }

    /// <summary>Spec 1305: the ship is short of crew below this fraction of its crew places. A ship without crew places is never short.</summary>
    public static bool ShortOfCrew(int crew, int places, float fraction) => places > 0 && crew < places * fraction;

    /// <summary>Spec 1305: per resource type, the cost minus what the ship can use, for the types where that is above 0.</summary>
    public static Dictionary<string, int> Lacking(IReadOnlyDictionary<string, int> cost, Func<string, int> usable)
    {
        var lacking = new Dictionary<string, int>();
        foreach (var (type, need) in cost.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (need < 0) throw new ArgumentOutOfRangeException(nameof(cost), $"{type}: cost {need}");
            int missing = need - Math.Max(0, usable(type));
            if (missing > 0) lacking[type] = missing;
        }
        return lacking;
    }

    /// <summary>Spec 1307: units that a donor gives: up to its share of the stock, at most what the ship lacks.</summary>
    public static int DonorGives(int lacking, int stock, float share) =>
        Math.Max(0, Math.Min(lacking, (int)MathF.Floor(Math.Max(0, stock) * share)));

    /// <summary>Spec 1309: donor and station trips get the longer time limit.</summary>
    public static float TimeLimit(RepairPlan plan, float maxSeconds, float tripSeconds) =>
        plan is RepairPlan.Donor or RepairPlan.Station ? tripSeconds : maxSeconds;
}

using Cosmoteer;
using Halfling;
using Halfling.ObjectText;
using Halfling.Serialization;

namespace HotCargo.Core;

/// <summary>Values that come from vanilla game data, read once.</summary>
public static class GameData
{
    [ReflectiveSerialization(Explicit = true)]
    sealed class TierFame
    {
        [Serialize] public int Tier;
        [Serialize] public int Fame;
    }

    [ReflectiveSerialization(Explicit = true)]
    sealed class TierMoney
    {
        [Serialize] public int Tier;
        [Serialize] public Halfling.Range<int> Money;
    }

    [ReflectiveSerialization(Explicit = true)]
    sealed class TierReputation
    {
        [Serialize] public int Tier;
        [Serialize] public Halfling.Range<int> Reputation;
    }

    static Dictionary<int, int>? _standardMissionFame;
    static Dictionary<int, int>? _standardMissionReputation;

    /// <summary>Middle of the reputation of a standard mission at a tier, from TIER_TABLES/StandardReputationPerTier, before reward factors.</summary>
    public static int StandardMissionReputation(int tier)
    {
        _standardMissionReputation ??= LoadTable<TierReputation>("StandardReputationPerTier").ToDictionary(r => r.Tier, r => (r.Reputation.Min + r.Reputation.Max) / 2);
        int clamped = Math.Clamp(tier, _standardMissionReputation.Keys.Min(), _standardMissionReputation.Keys.Max());
        return _standardMissionReputation[clamped];
    }
    static Dictionary<int, int>? _standardMissionMoney;

    /// <summary>
    /// Middle of the money range of a standard mission at a tier, from TIER_TABLES/StandardMoneyPerTier, before reward factors.
    /// Same limit as StandardMissionFame: reads the vanilla file.
    /// </summary>
    public static int StandardMissionMoney(int tier)
    {
        _standardMissionMoney ??= LoadTable<TierMoney>("StandardMoneyPerTier").ToDictionary(r => r.Tier, r => (r.Money.Min + r.Money.Max) / 2);
        int clamped = Math.Clamp(tier, _standardMissionMoney.Keys.Min(), _standardMissionMoney.Keys.Max());
        return _standardMissionMoney[clamped];
    }

    /// <summary>
    /// Fame of a standard mission at a tier, from TIER_TABLES/StandardFamePerTier in the vanilla data.
    /// Limit: reads the vanilla file, so a mod that changes the tier tables is not seen. Revisit if Hot Cargo ships with a mod that changes them.
    /// </summary>
    public static int StandardMissionFame(int tier)
    {
        _standardMissionFame ??= LoadStandardMissionFame();
        int clamped = Math.Clamp(tier, _standardMissionFame.Keys.Min(), _standardMissionFame.Keys.Max());
        return _standardMissionFame[clamped];
    }

    static T[] LoadTable<T>(string name)
    {
        string path = (string)(Paths.DataFolder / "modes/career/tier_tables.rules");
        var rows = App.ObjectTextSerializer.ReadFromPath<T[]>(new OTFile(path), name)
            ?? throw new InvalidDataException($"{name} missing in {path}");
        if (rows.Length == 0) throw new InvalidDataException($"{name} is empty in {path}");
        return rows;
    }

    static Dictionary<int, int> LoadStandardMissionFame()
    {
        var map = LoadTable<TierFame>("StandardFamePerTier").ToDictionary(r => r.Tier, r => r.Fame);
        Log.Info($"standard mission fame per tier: {string.Join(", ", map.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"))}");
        return map;
    }
}

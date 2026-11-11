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

    static Dictionary<int, int>? _standardMissionFame;

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

    static Dictionary<int, int> LoadStandardMissionFame()
    {
        string path = (string)(Paths.DataFolder / "modes/career/tier_tables.rules");
        var file = new OTFile(path);
        var rows = App.ObjectTextSerializer.ReadFromPath<TierFame[]>(file, "StandardFamePerTier")
            ?? throw new InvalidDataException($"StandardFamePerTier missing in {path}");
        if (rows.Length == 0) throw new InvalidDataException($"StandardFamePerTier is empty in {path}");
        var map = rows.ToDictionary(r => r.Tier, r => r.Fame);
        Log.Info($"standard mission fame per tier: {string.Join(", ", map.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"))}");
        return map;
    }
}

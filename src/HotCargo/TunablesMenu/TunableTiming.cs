using System.Reflection;
using HotCargo.Core;

namespace HotCargo.TunablesMenu;

/// <summary>When a changed tunable takes effect in a running game (TUNABLES.md, spec 503).</summary>
public enum Applies
{
    /// <summary>Within a second: the code reads the value at each use.</summary>
    Now,
    /// <summary>The next time the thing shows or opens (a comms window, an alert). One already open keeps the old value.</summary>
    NextUse,
    /// <summary>The next ships that spawn. Ships already in space keep the old value.</summary>
    NextSpawn,
    /// <summary>Mission offers and contract terms made after the change. Offers that exist keep their terms.</summary>
    NextOffer,
    /// <summary>The next system that the players enter for the first time.</summary>
    NextSystem,
    /// <summary>New runs only.</summary>
    NewRun,
}

/// <summary>
/// The fields that do not apply at once, keyed by "DeclaringType.Field". Every other field applies Now.
/// Reflection cannot find when the code reads a value, so this list comes from the audit in TUNABLES.md.
/// Limit: it is a second list next to the tunables classes, because the area agents edit those files in parallel.
/// Revisit when the areas are merged: move each entry to an [Applies(...)] attribute on its field.
/// Check() fails on a key that names no field, so a renamed field shows up in the tests.
/// </summary>
public static class TunableTiming
{
    static readonly Dictionary<string, Applies> Exceptions = new()
    {
        ["HeatTunables.HeatCarryOverFraction"] = Applies.NextSystem,
        ["FactionsTunables.RelayWatchedHeatCut"] = Applies.NextSystem,
        ["FactionsTunables.RelayClearHeatCut"] = Applies.NextSystem,
        ["FactionsTunables.BlindHeatCut"] = Applies.NextSystem,
        ["TechTunables.StartingTechUnits"] = Applies.NewRun,
        ["JourneyTunables.RunSectorCount"] = Applies.NextSystem,
        ["HunterTunables.Stages"] = Applies.NextSpawn,
        ["StageResponse.Name"] = Applies.NextSpawn,
        ["StageResponse.ShipCount"] = Applies.NextSpawn,
        ["StageResponse.TierOffset"] = Applies.NextSpawn,
        ["StageResponse.EarlyScale"] = Applies.NextSpawn,
        ["StageResponse.EarlyTierReduction"] = Applies.NextSpawn,
        ["StageResponse.Waves"] = Applies.NextSpawn,
        ["StageResponse.WaveGapSeconds"] = Applies.NextSpawn,
        ["HunterTunables.SpawnDistanceMin"] = Applies.NextSpawn,
        ["HunterTunables.SpawnDistanceMax"] = Applies.NextSpawn,
        ["HunterTunables.WaveArcDegrees"] = Applies.NextSpawn,
        ["HunterTunables.ShipFactions"] = Applies.NextSpawn,
        ["HunterTunables.ShipTags"] = Applies.NextSpawn,
        ["HunterTunables.ShipNamePrefix"] = Applies.NextSpawn,
        ["HunterTunables.MaxLiveHunters"] = Applies.NextSpawn,
        ["HunterTunables.MinFuel"] = Applies.NextSpawn,
        ["HunterTunables.RouteTierFloor"] = Applies.NextSpawn,
        ["DiplomacyTunables.PunishSeconds"] = Applies.NextUse,
        ["MissionTunables.ShadyRewardFactor"] = Applies.NextOffer,
        ["MissionTunables.ShadyTrapSurvivalReward"] = Applies.NextOffer,
        ["MissionTunables.EscortHeatChange"] = Applies.NextOffer,
        ["EventsTunables.ArrivalEventChance"] = Applies.NextSystem,
        ["EventsTunables.ArrivalDelaySeconds"] = Applies.NextSystem,
        ["EventsTunables.StartDelaySeconds"] = Applies.NewRun,
        ["EventsTunables.StationEventChance"] = Applies.NextSystem,
        ["EventsTunables.CourierChance"] = Applies.NextSystem,
        ["EventsTunables.PauseOnEvent"] = Applies.NextUse,
        ["EventsTunables.RelicEnabled"] = Applies.NewRun,
        ["EventsTunables.RelicMinSector"] = Applies.NewRun,
        ["ChainsTunables.StationChance"] = Applies.NextSystem,
        ["ChainsTunables.MaxNewChainsPerSystem"] = Applies.NextSystem,
        ["BalanceTunables.StationFuelPerMillion"] = Applies.NextSpawn,
        ["BalanceTunables.StationFuelMinimum"] = Applies.NextSpawn,
        ["ChainsTunables.LaterDelaySeconds"] = Applies.NextSystem,
        ["ChainsTunables.StoryLines"] = Applies.NextUse,
        ["ContractsTunables.FeeChance"] = Applies.NextOffer,
        ["ContractsTunables.FeeMin"] = Applies.NextOffer,
        ["ContractsTunables.FeeMax"] = Applies.NextOffer,
        ["ContractsTunables.PaidBonusFactor"] = Applies.NextOffer,
        ["ContractsTunables.MoneyRoundTo"] = Applies.NextOffer,
        ["ContractsTunables.RivalPairChance"] = Applies.NextOffer,
        ["ContractsTunables.InformantShare"] = Applies.NextOffer,
        ["ContractsTunables.InformantChanceMin"] = Applies.NextOffer,
        ["ContractsTunables.InformantChanceMax"] = Applies.NextOffer,
        ["ContractsTunables.InformantDelayMin"] = Applies.NextOffer,
        ["ContractsTunables.InformantDelayMax"] = Applies.NextOffer,
        ["ContractsTunables.ExcludedCategories"] = Applies.NextOffer,
        ["ContractsTunables.Metatypes"] = Applies.NextOffer,
        ["MetatypeTerms.Metatype"] = Applies.NextOffer,
        ["MetatypeTerms.FlatHeat"] = Applies.NextOffer,
        ["MetatypeTerms.TrailWeight"] = Applies.NextOffer,
        ["MechanicsTunables.DroneSiegeChance"] = Applies.NextSystem,
        ["MechanicsTunables.PrototypeChance"] = Applies.NextSystem,
        ["MechanicsTunables.SunMiningChance"] = Applies.NextSystem,
        ["MechanicsTunables.TowChance"] = Applies.NextSystem,
        ["MechanicsTunables.CaptureChance"] = Applies.NextSystem,
        ["MechanicsTunables.SurveyChance"] = Applies.NextSystem,
        ["MechanicsTunables.MiningGuardChance"] = Applies.NextSystem,
        ["MechanicsTunables.ListeningPostChance"] = Applies.NextSystem,
        ["HoldOutTunables.ShipsBase"] = Applies.NextSpawn,
        ["HoldOutTunables.ShipsPerTier"] = Applies.NextSpawn,
        ["HoldOutTunables.MaxLiveShips"] = Applies.NextSpawn,
        ["HoldOutTunables.ShipTierBase"] = Applies.NextSpawn,
        ["HoldOutTunables.ShipTierPerTier"] = Applies.NextSpawn,
        ["HoldOutTunables.ShipTierMax"] = Applies.NextSpawn,
        ["HoldOutTunables.ShipTags"] = Applies.NextSpawn,
        ["MechanicsTunables.IntensiveExtraShips"] = Applies.NextSpawn,
        ["MechanicsTunables.IntensiveExtraTier"] = Applies.NextSpawn,
        ["WorldsTunables.PassWaveDistance"] = Applies.NextSpawn,
        ["WorldsTunables.ArrivalLine"] = Applies.NextSystem,
        ["WorldsTunables.ArrivalLineDelaySeconds"] = Applies.NextSystem,
        ["UxTunables.RunIntro"] = Applies.NewRun,
        ["UxTunables.StageAlertSeconds"] = Applies.NextUse,
        ["UxTunables.CommsStayOnMap"] = Applies.NextUse,
        // The filter, the risk sort and the kind icons attach when a mission list opens.
        ["UxTunables.MissionSelect"] = Applies.NextUse,
        ["UxTunables.PauseOnComms"] = Applies.NextUse,
        ["UxTunables.PauseOnCommsMultiplayer"] = Applies.NextUse,
        ["DifficultyRow.HunterShips"] = Applies.NextSpawn,
        ["DifficultyRow.HunterTiers"] = Applies.NextSpawn,
        ["DifficultyRow.ContractFees"] = Applies.NextOffer,
    };

    public static Applies Of(FieldInfo field) =>
        Exceptions.TryGetValue($"{field.DeclaringType!.Name}.{field.Name}", out var a) ? a : Applies.Now;

    /// <summary>Throws when a key names no tunable field. Runs at startup.</summary>
    public static void Check()
    {
        var known = new HashSet<string>();
        void Walk(Type type)
        {
            foreach (var f in TunablePath.Fields(type))
            {
                if (!known.Add($"{type.Name}.{f.Name}")) continue;
                Type inner = TunablePath.ElementType(f.FieldType) ?? f.FieldType;
                if (!TunablePath.IsScalar(inner)) Walk(inner);
            }
        }
        Walk(typeof(Tunables));
        var unknown = Exceptions.Keys.Where(k => !known.Contains(k)).ToList();
        if (unknown.Count > 0) throw new InvalidOperationException($"TunableTiming names no field: {string.Join(", ", unknown)}");
    }
}

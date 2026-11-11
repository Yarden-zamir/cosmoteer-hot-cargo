using Halfling.Serialization;

namespace HotCargo.Tech;

[ReflectiveSerialization(Explicit = true)]
public sealed class TechTunables
{
    [Serialize(Optional = true)] public int StartingTechUnits = 6;
    [Serialize(Optional = true)] public float TechBaseValue = 10000f;
    [Serialize(Optional = true)] public float TechGrowthPerSector = 0.35f;
    [Serialize(Optional = true)] public float TechGrowthPerGameHour = 0.10f;
    /// <summary>The price changes in steps of this many game minutes, so that a sale in flight sees the same price on all clients.</summary>
    [Serialize(Optional = true)] public float PriceStepMinutes = 1f;
    /// <summary>Station buy-back price = sell price × this factor. Must be above 1 to stop a sell-and-buy-back money loop.</summary>
    [Serialize(Optional = true)] public float BuyBackFactor = 1.5f;
    /// <summary>Spec 65: one unit is lost each time a vault takes this fraction of its max health in damage.</summary>
    [Serialize(Optional = true)] public float VaultDamagePerUnitLost = 0.15f;
    /// <summary>Spec 12: the fleet tech count must stay at 0 this long before the run is lost.</summary>
    [Serialize(Optional = true)] public float LossGraceSeconds = 3f;
}

public static class TechTunablesExtensions
{
    public static long Value(this TechTunables t, int sectorIndex, double gameSeconds) =>
        TechModel.Value(t.TechBaseValue, t.TechGrowthPerSector, t.TechGrowthPerGameHour, t.PriceStepMinutes, sectorIndex, gameSeconds);
}

using Halfling.Serialization;

namespace HotCargo.Tech;

[ReflectiveSerialization(Explicit = true)]
public sealed class TechTunables
{
    [Serialize(Optional = true)] public int StartingTechUnits = 6;
    /// <summary>
    /// Spec 19: tech value in fractions of the highest ship value of the peak tier (vanilla TierValueMaximums).
    /// Defaults: 0.25 of a ship at the start, about 0.5 mid-run (1.2 h, 250 notoriety), so 2 units buy a full ship of the tier.
    /// </summary>
    [Serialize(Optional = true)] public float TechShipFraction = 0.25f;
    [Serialize(Optional = true)] public float TechShipFractionPerGameHour = 0.10f;
    [Serialize(Optional = true)] public float TechShipFractionPer100Notoriety = 0.05f;
    /// <summary>The price changes in steps of this many game minutes, so that a sale in flight sees the same price on all clients.</summary>
    [Serialize(Optional = true)] public float PriceStepMinutes = 1f;
    /// <summary>The price changes in steps of this much notoriety, for the same reason.</summary>
    [Serialize(Optional = true)] public float NotorietyStep = 5f;
    /// <summary>Station buy-back price = sell price × this factor. Must be above 1 to stop a sell-and-buy-back money loop.</summary>
    [Serialize(Optional = true)] public float BuyBackFactor = 1.5f;
    /// <summary>Spec 65: one unit is lost each time a vault takes this fraction of its max health in damage.</summary>
    [Serialize(Optional = true)] public float VaultDamagePerUnitLost = 0.15f;
    /// <summary>Spec 12: the fleet tech count must stay at 0 this long before the run is lost.</summary>
    [Serialize(Optional = true)] public float LossGraceSeconds = 3f;
}

public static class TechTunablesExtensions
{
    /// <summary>Spec 19: the tech value of one unit now. Every input is saved sim state, so all clients compute the same value.</summary>
    public static long Value(this TechTunables t, Core.RunState s) =>
        TechModel.Value(Core.GameData.TierValueMaximum(s.PeakTier), t.TechShipFraction, t.TechShipFractionPerGameHour, t.TechShipFractionPer100Notoriety,
            t.PriceStepMinutes, t.NotorietyStep, s.GameSeconds, s.Notoriety);
}

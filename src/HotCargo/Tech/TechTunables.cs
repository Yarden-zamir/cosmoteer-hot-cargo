using Halfling.Serialization;

namespace HotCargo.Tech;

[ReflectiveSerialization(Explicit = true)]
public sealed class TechTunables
{
    [Serialize(Optional = true)] public int StartingTechUnits = 8;
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
    /// <summary>
    /// Spec 65: one unit is lost each time a vault takes this fraction of its max health in damage.
    /// Default 0.5 for a 2-unit vault: the first unit goes at half health, the second when the vault is destroyed.
    /// </summary>
    [Serialize(Optional = true)] public float VaultDamagePerUnitLost = 0.5f;
    /// <summary>Spec 12, 443: game seconds of the loss timer. When the fleet holds no tech, the players have this long to get tech back.</summary>
    [Serialize(Optional = true)] public float LossGraceSeconds = 180f;
    /// <summary>
    /// Spec 448: vaults take this share of heat damage (the "thermal" damage of the heat status, mostly sun heat).
    /// So a dive into a sun's heat zone risks the ship more than the tech.
    /// </summary>
    [Serialize(Optional = true)] public float VaultSunHeatFactor = 0.25f;
    /// <summary>Spec 445: chance that a destroyed vault burns its tech instead of dropping it as crates.</summary>
    [Serialize(Optional = true)] public float VaultTechLossChance = 0.1f;
    /// <summary>Spec 446: chance that a hunter goes for a tech crate it sees. Rolled once for each hunter and crate.</summary>
    [Serialize(Optional = true)] public float HunterGrabChance = 0.8f;
    /// <summary>Spec 446: hunters see tech crates within this distance.</summary>
    [Serialize(Optional = true)] public float HunterGrabRange = 3000f;
    /// <summary>Spec 446: a hunter takes a crate within this distance of its hull.</summary>
    [Serialize(Optional = true)] public float HunterGrabReach = 40f;
    /// <summary>Spec 444: map markers for tech in the system (tech held on ship blips, crates in space).</summary>
    [Serialize(Optional = true)] public bool TechMarkers = true;
}

public static class TechTunablesExtensions
{
    /// <summary>
    /// Spec 19 and 683: the tech value of one unit now, times the difficulty tech factor. Every input is saved sim state or a confirmed
    /// difficulty level, so all clients compute the same value.
    /// </summary>
    public static long TechValue(this Core.Run run)
    {
        var (t, s) = (run.T.Tech, run.State);
        long value = TechModel.Value(Core.GameData.TierValueMaximum(s.PeakTier), t.TechShipFraction, t.TechShipFractionPerGameHour,
            t.TechShipFractionPer100Notoriety, t.PriceStepMinutes, t.NotorietyStep, s.GameSeconds, s.Notoriety);
        return RunModes.Difficulty.Pressure(run).Tech(value);
    }
}

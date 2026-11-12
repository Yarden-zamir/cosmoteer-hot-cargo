using Halfling.Serialization;

namespace HotCargo.Factions;

/// <summary>Faction roles, hot zones, relay and blind jumps, station prices by heat and map markers (spec 640 to 662).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class FactionsTunables
{
    /// <summary>Spec 640: the faction ID of the corporation's ally (monolith, cabal, imperium, fringe, io). Empty picks one from the galaxy seed.</summary>
    [Serialize(Optional = true)] public string AllyFaction = "";
    /// <summary>Spec 640: the faction ID of the corporation's rival. Empty picks one from the galaxy seed. Must differ from the ally.</summary>
    [Serialize(Optional = true)] public string RivalFaction = "";

    /// <summary>Spec 641: time heat in a system that the corporate ally controls (a hot zone).</summary>
    [Serialize(Optional = true)] public float AllyHeatFactor = 1.5f;
    /// <summary>Spec 642: mission money of offers that a hot zone gives.</summary>
    [Serialize(Optional = true)] public float AllyMoneyFactor = 1.3f;
    /// <summary>Spec 642: notoriety gains in a hot zone. Notoriety raises the tech value (spec 19), so the tech gains value faster there.</summary>
    [Serialize(Optional = true)] public float AllyNotorietyFactor = 1.5f;

    /// <summary>Spec 643: reputation with the controlling faction from which it shelters the players (vanilla Friend is 500).</summary>
    [Serialize(Optional = true)] public int ShelterReputation = 500;
    /// <summary>Spec 643: time heat in a system whose controlling faction shelters the players.</summary>
    [Serialize(Optional = true)] public float ShelterHeatFactor = 0.6f;

    /// <summary>Spec 644: stations of the rival faction pay this times the tech value (fences). Buy-back there costs the same factor more.</summary>
    [Serialize(Optional = true)] public float RivalFencePremium = 1.5f;

    /// <summary>Spec 650: share of the heat that a relay jump removes when hunters are near that relay at the jump start (0 to 1).</summary>
    [Serialize(Optional = true)] public float RelayWatchedHeatCut = 0.75f;
    /// <summary>Spec 650: share of the heat that a relay jump removes when no hunter is near that relay at the jump start (0 to 1).</summary>
    [Serialize(Optional = true)] public float RelayClearHeatCut = 0.85f;
    /// <summary>Spec 650: share of the heat that a blind jump removes (0 to 1).</summary>
    [Serialize(Optional = true)] public float BlindHeatCut = 0.9f;
    /// <summary>Spec 650: a hunter within this distance of the relay at the jump start makes the relay watched.</summary>
    [Serialize(Optional = true)] public float RelayWatchRadius = 3500f;
    /// <summary>Spec 651: fuel of a blind jump, times the fuel of a relay jump to the same system.</summary>
    [Serialize(Optional = true)] public float BlindFuelFactor = 1.5f;
    /// <summary>Spec 651: spin-up time of a blind jump, times the vanilla spin-up time of the ship.</summary>
    [Serialize(Optional = true)] public float BlindSpinUpFactor = 2f;
    /// <summary>Spec 651: chance for each jumping ship to take damage when it arrives from a blind jump (0 to 1).</summary>
    [Serialize(Optional = true)] public float BlindDamageChance = 0.35f;
    /// <summary>Spec 651: parts that take damage on a ship that the blind jump damages.</summary>
    [Serialize(Optional = true)] public int BlindDamageParts = 3;
    /// <summary>Spec 651: damage to each of those parts, as a share of its maximum health. A part keeps at least 1 health.</summary>
    [Serialize(Optional = true)] public float BlindDamageFraction = 0.4f;

    /// <summary>Spec 655: heat from which station goods and services cost more.</summary>
    [Serialize(Optional = true)] public float MarkupStartHeat = 75f;
    /// <summary>Spec 655: price factor at heat 100. It rises in a straight line from MarkupStartHeat.</summary>
    [Serialize(Optional = true)] public float MarkupMaxFactor = 2f;
    /// <summary>Spec 655, 656: prices use heat in whole steps of this size, so a trade in flight sees one price.</summary>
    [Serialize(Optional = true)] public float PriceHeatStep = 5f;
    /// <summary>Spec 656: crew hire price factor at heat 100. It rises in a straight line from heat 0.</summary>
    [Serialize(Optional = true)] public float CrewPriceAtMaxHeat = 2f;
    /// <summary>Spec 657: heat from which stations can refuse to talk.</summary>
    [Serialize(Optional = true)] public float RefuseHeat = 90f;
    /// <summary>Spec 657: chance that a station refuses, rolled once for each station and sector.</summary>
    [Serialize(Optional = true)] public float RefuseChance = 0.5f;

    /// <summary>Spec 660: the galaxy map marks the targets of events and contracts.</summary>
    [Serialize(Optional = true)] public bool MapMarkers = true;

    public void Validate(Action<bool, string> require)
    {
        require(AllyHeatFactor > 0 && AllyMoneyFactor > 0 && AllyNotorietyFactor > 0 && ShelterHeatFactor > 0 && RivalFencePremium > 0,
            "Factions heat, money, notoriety and fence factors must be above 0");
        require(RelayWatchedHeatCut is >= 0 and <= 1 && RelayClearHeatCut is >= 0 and <= 1 && BlindHeatCut is >= 0 and <= 1, "Factions jump heat cuts must be 0 to 1");
        require(RelayWatchRadius >= 0 && BlindFuelFactor > 0 && BlindSpinUpFactor >= 1, "Factions.RelayWatchRadius must not be negative, BlindFuelFactor above 0, BlindSpinUpFactor 1 or more");
        require(BlindDamageChance is >= 0 and <= 1 && BlindDamageParts >= 0 && BlindDamageFraction is >= 0 and <= 1, "Factions blind damage: chance and fraction 0 to 1, parts 0 or more");
        require(MarkupStartHeat is >= 0 and < 100 && MarkupMaxFactor >= 1 && CrewPriceAtMaxHeat >= 1, "Factions.MarkupStartHeat must be 0 to below 100, MarkupMaxFactor and CrewPriceAtMaxHeat 1 or more");
        require(PriceHeatStep > 0, "Factions.PriceHeatStep must be above 0");
        require(RefuseChance is >= 0 and <= 1, "Factions.RefuseChance must be 0 to 1");
        require(AllyFaction.Length == 0 || AllyFaction != RivalFaction, "Factions.AllyFaction and RivalFaction must differ");
        foreach (string id in new[] { AllyFaction, RivalFaction })
            require(id.Length == 0 || Cosmoteer.GameApp.Rules is null || FactionsArea.FactionById(id) is not null, $"Factions: no faction with ID '{id}'");
    }
}

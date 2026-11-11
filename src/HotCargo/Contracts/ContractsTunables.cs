using Halfling.Serialization;

namespace HotCargo.Contracts;

/// <summary>Contract economy numbers (spec 100 to 108).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ContractsTunables
{
    /// <summary>Spec 101: open contracts up to this weight leave no trail.</summary>
    [Serialize(Optional = true)] public float TrailFreeContracts = 3f;
    /// <summary>Spec 101: heat per game minute for each open contract above TrailFreeContracts. Not scaled by the tech multiplier.</summary>
    [Serialize(Optional = true)] public float TrailHeatPerContractMinute = 1f;

    /// <summary>Spec 102: chance that an offer without rules terms has an accept fee.</summary>
    [Serialize(Optional = true)] public float FeeChance = 0.5f;
    /// <summary>Spec 102: fee range in standard mission pay at the mission tier (after difficulty and RewardScale).</summary>
    [Serialize(Optional = true)] public float FeeMin = 0.3f;
    [Serialize(Optional = true)] public float FeeMax = 0.8f;
    /// <summary>Spec 102: a paid contract pays fee × this on completion, on top of its normal rewards.</summary>
    [Serialize(Optional = true)] public float PaidBonusFactor = 2f;
    [Serialize(Optional = true)] public int MoneyRoundTo = 500;

    /// <summary>Spec 103: chance that a new offer pairs with an earlier offer of another client (faction or station) as rivals. Offers that target each other's faction always pair.</summary>
    [Serialize(Optional = true)] public float RivalPairChance = 0.2f;

    /// <summary>Spec 104: share of offers without rules terms that carry an informant risk.</summary>
    [Serialize(Optional = true)] public float InformantShare = 0.75f;
    [Serialize(Optional = true)] public float InformantChanceMin = 0.05f;
    [Serialize(Optional = true)] public float InformantChanceMax = 0.15f;
    /// <summary>Spec 104: game seconds after the accept until the client sells the players out.</summary>
    [Serialize(Optional = true)] public float InformantDelayMin = 45f;
    [Serialize(Optional = true)] public float InformantDelayMax = 150f;
    [Serialize(Optional = true)] public float InformantHeat = 15f;
    /// <summary>Spec 104: hunter ships of the sector tier in the sell-out wave. 0 = heat only.</summary>
    [Serialize(Optional = true)] public int InformantShips = 2;

    /// <summary>Spec 100: mission categories that are not contracts. Exploration surveys have no client to pay or to sell you out.</summary>
    [Serialize(Optional = true)] public string[] ExcludedCategories = ["exploration"];

    /// <summary>Spec 100: terms for vanilla mission types, by metatype ID. Rules terms on a mission win over these.</summary>
    [Serialize(Optional = true)] public MetatypeTerms[] Metatypes =
    [
        new() { Metatype = "PirateBaseDestruction", FlatHeat = 5f },
        new() { Metatype = "FactionBaseDestruction", FlatHeat = 5f },
    ];
}

[ReflectiveSerialization(Explicit = true)]
public sealed class MetatypeTerms
{
    [Serialize] public string Metatype = "";
    [Serialize(Optional = true)] public float FlatHeat;
    [Serialize(Optional = true)] public float TrailWeight = 1f;
}

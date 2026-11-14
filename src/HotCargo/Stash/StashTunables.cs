using Halfling.Serialization;

namespace HotCargo.Stash;

/// <summary>The timed stash (spec 970 to 989).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class StashTunables
{
    /// <summary>Spec 970: chance that a system offers a stash rumour at a trade station.</summary>
    [Serialize(Optional = true)] public float OfferChance = 0.35f;
    /// <summary>Spec 971: chance that the corporation found the stash first, at the moment of the offer.</summary>
    [Serialize(Optional = true)] public float FoundFirstBase = 0.1f;
    /// <summary>Spec 971: the found-first chance grows by this much for each game minute after the offer.</summary>
    [Serialize(Optional = true)] public float FoundFirstPerMinute = 0.04f;
    /// <summary>Spec 971: the found-first chance never goes above this.</summary>
    [Serialize(Optional = true)] public float FoundFirstMax = 0.9f;
    /// <summary>Spec 972: share of found-first stashes that are empty. The rest are an ambush.</summary>
    [Serialize(Optional = true)] public float EmptyShare = 0.5f;
    /// <summary>Spec 973: the stash money, times a standard mission's money at the stash's tier.</summary>
    [Serialize(Optional = true)] public float LootMoneyFactor = 3f;
    /// <summary>Spec 973: chance that the stash also holds tech.</summary>
    [Serialize(Optional = true)] public float LootTechChance = 0.5f;
    /// <summary>Spec 973: tech units in a stash that holds tech. Units without vault room drop next to the ship.</summary>
    [Serialize(Optional = true)] public int LootTechUnits = 1;

    public void Validate(Action<bool, string> require)
    {
        require(OfferChance is >= 0 and <= 1 && FoundFirstBase is >= 0 and <= 1 && FoundFirstMax is >= 0 and <= 1 && FoundFirstPerMinute >= 0,
            "Stash chances must be 0 to 1, FoundFirstPerMinute not negative");
        require(EmptyShare is >= 0 and <= 1 && LootTechChance is >= 0 and <= 1, "Stash.EmptyShare and LootTechChance must be 0 to 1");
        require(LootMoneyFactor >= 0 && LootTechUnits >= 0, "Stash.LootMoneyFactor and LootTechUnits must not be negative");
    }
}

using Halfling.Serialization;

namespace HotCargo.Relays;

/// <summary>Relay lockdowns (spec 950 to 969).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class RelaysTunables
{
    /// <summary>Spec 950: relay lockdowns can happen.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;
    /// <summary>Spec 951: chance that a system next to the players' system gets a lockdown, rolled once for each system.</summary>
    [Serialize(Optional = true)] public float ChancePerSystem = 0.15f;
    /// <summary>Spec 951: most lockdowns that the players enter in one run.</summary>
    [Serialize(Optional = true)] public int MaxPerRun = 2;
    /// <summary>Spec 951: the first sector index that can hold a lockdown.</summary>
    [Serialize(Optional = true)] public int MinSector = 2;
    /// <summary>Spec 951: sectors between two lockdowns. Used until the pursuit director paces big events.</summary>
    [Serialize(Optional = true)] public int MinGapSectors = 2;
    /// <summary>Spec 952: weight of a closed relay in the kind roll.</summary>
    [Serialize(Optional = true)] public float ClosedWeight = 1f;
    /// <summary>Spec 952: weight of a relay fee in the kind roll.</summary>
    [Serialize(Optional = true)] public float FeeWeight = 1f;
    /// <summary>Spec 952: weight of a pirate guard in the kind roll.</summary>
    [Serialize(Optional = true)] public float PiratesWeight = 1f;

    /// <summary>Spec 955: game seconds after arrival at which a closed relay opens again. 0 keeps it closed for the visit.</summary>
    [Serialize(Optional = true)] public float ClosedSeconds = 420f;
    /// <summary>Spec 956: the relay fee, in event money units (one unit is about one standard mission's pay).</summary>
    [Serialize(Optional = true)] public float FeeUnits = 0.6f;
    /// <summary>Spec 956: the fee grows by this many money units for each heat point. The corporation asks the faction to squeeze you.</summary>
    [Serialize(Optional = true)] public float FeePerHeat = 0.006f;
    /// <summary>Spec 958: the pirates' price, in event money units.</summary>
    [Serialize(Optional = true)] public float BribeUnits = 1.2f;
    /// <summary>Spec 958: an offer of this share of the price or less always fails. The chance rises in a straight line to 1 at the full price.</summary>
    [Serialize(Optional = true)] public float BribeMinShare = 0.5f;
    /// <summary>Spec 958: resources count at this share of their station buy price in a bribe.</summary>
    [Serialize(Optional = true)] public float BribeResourceRate = 0.8f;
    /// <summary>Spec 958: the most resource kinds that a bribe page offers (the ones the fleet holds most value of).</summary>
    [Serialize(Optional = true)] public int BribeResourceKinds = 3;

    public void Validate(Action<bool, string> require)
    {
        require(ChancePerSystem is >= 0 and <= 1, "Relays.ChancePerSystem must be 0 to 1");
        require(MaxPerRun >= 0 && MinSector >= 0 && MinGapSectors >= 0, "Relays.MaxPerRun, MinSector and MinGapSectors must not be negative");
        require(ClosedWeight >= 0 && FeeWeight >= 0 && PiratesWeight >= 0, "Relays kind weights must not be negative");
        require(ClosedSeconds >= 0 && FeeUnits >= 0 && FeePerHeat >= 0 && BribeUnits > 0, "Relays.ClosedSeconds, FeeUnits and FeePerHeat must not be negative, BribeUnits above 0");
        require(BribeMinShare is >= 0 and < 1 && BribeResourceRate >= 0 && BribeResourceKinds >= 0, "Relays.BribeMinShare must be 0 to below 1, BribeResourceRate and BribeResourceKinds not negative");
    }
}

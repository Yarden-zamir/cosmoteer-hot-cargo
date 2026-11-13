namespace HotCargo.Ux;

/// <summary>What kind of mission an offer is, for the mission select badges and filters (spec 800).</summary>
public enum MissionKind
{
    Vanilla,
    /// <summary>A Hot Cargo mission type that is not a chain stage and not a shady offer.</summary>
    HotCargo,
    /// <summary>A story chain stage (spec 170).</summary>
    Chain,
    /// <summary>A shady offer (spec 38): it can be a trap.</summary>
    Shady,
}

/// <summary>Filters of the mission lists (spec 801). The order is the order in the filter list.</summary>
public enum MissionFilter
{
    All,
    HotCargo,
    Chains,
    Contracts,
    Vanilla,
    NoShady,
}

/// <summary>Pure rules of the mission select. No game types, so the unit tests run on the Mac.</summary>
public static class MissionSelectModel
{
    /// <summary>True when a mission of this kind shows under the filter. Chains and shady offers are Hot Cargo missions too.</summary>
    public static bool Passes(MissionFilter filter, MissionKind kind, bool contract) => filter switch
    {
        MissionFilter.All => true,
        MissionFilter.HotCargo => kind != MissionKind.Vanilla,
        MissionFilter.Chains => kind == MissionKind.Chain,
        MissionFilter.Contracts => contract,
        MissionFilter.Vanilla => kind == MissionKind.Vanilla,
        MissionFilter.NoShady => kind != MissionKind.Shady,
        _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null),
    };

    /// <summary>
    /// Sort key of the risk sort: lowest risk first. Heat counts as it is; a trap or informant chance counts as
    /// <paramref name="heatPerChance"/> heat for a sure event, so a 50 % trap weighs like that much heat × 0.5.
    /// </summary>
    public static float RiskKey(float heat, float trapChance, float informantChance, float heatPerChance)
    {
        if (float.IsNaN(heat) || float.IsNaN(trapChance) || float.IsNaN(informantChance)) throw new ArgumentOutOfRangeException(nameof(heat), "NaN risk");
        return heat + heatPerChance * (Math.Clamp(trapChance, 0f, 1f) + Math.Clamp(informantChance, 0f, 1f));
    }
}

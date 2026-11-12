using Halfling.Serialization;

namespace HotCargo.Journey;

[ReflectiveSerialization(Explicit = true)]
public sealed class JourneyTunables
{
    /// <summary>Sector index at which the Buyer station appears. The short map rules size the galaxy for this.</summary>
    [Serialize(Optional = true)] public int RunSectorCount = 8;
    /// <summary>
    /// Spec 4: mission money and fame × this, on top of the economy difficulty (Fast pays 2× money and 1.5× fame).
    /// 1 = vanilla rates. On Fast, six missions a sector pay about twice what a fleet of even power needs to keep up (BALANCE.md).
    /// </summary>
    [Serialize(Optional = true)] public float RewardScale = 1.0f;
    /// <summary>Spec 7: the Buyer pays tech value × this, and the score values unsold units the same way.</summary>
    [Serialize(Optional = true)] public float BuyerPriceFactor = 4f;
    /// <summary>Spec 301: blueprint prices × this, so that unlocks stay a choice in a short run. The faction reputation factor applies too.</summary>
    [Serialize(Optional = true)] public float TechCostScale = 2.5f;
}

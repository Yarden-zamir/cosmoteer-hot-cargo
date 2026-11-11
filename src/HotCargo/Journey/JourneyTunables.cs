using Halfling.Serialization;

namespace HotCargo.Journey;

[ReflectiveSerialization(Explicit = true)]
public sealed class JourneyTunables
{
    /// <summary>Sector index at which the Buyer station appears. The short map rules size the galaxy for this.</summary>
    [Serialize(Optional = true)] public int RunSectorCount = 8;
    [Serialize(Optional = true)] public float RewardScale = 2.0f;
    [Serialize(Optional = true)] public float BuyerPriceFactor = 1.5f;
}

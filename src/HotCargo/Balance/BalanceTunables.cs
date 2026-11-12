using Halfling.Serialization;

namespace HotCargo.Balance;

/// <summary>Balance numbers that no other area owns (BALANCE.md, spec 300 to 304).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class BalanceTunables
{
    /// <summary>
    /// Spec 304: every station keeps this much fuel per million credits of the next tier's ship value for sale, at least
    /// StationFuelMinimum. The default is one 10 LY relay jump for a fleet of the next tier, so a run never gets stuck on fuel.
    /// </summary>
    [Serialize(Optional = true)] public float StationFuelPerMillion = 100f;
    /// <summary>Spec 304: the smallest fuel floor of a station, for the first tiers.</summary>
    [Serialize(Optional = true)] public int StationFuelMinimum = 10;
}

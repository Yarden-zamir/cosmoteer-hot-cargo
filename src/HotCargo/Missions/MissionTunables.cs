using Halfling.Serialization;

namespace HotCargo.Missions;

[ReflectiveSerialization(Explicit = true)]
public sealed class MissionTunables
{
    [Serialize(Optional = true)] public float ShadyRewardFactor = 2.0f;
    [Serialize(Optional = true)] public float ShadyTrapChanceBase = 0.2f;
    [Serialize(Optional = true)] public float ShadyTrapChancePerHeat = 0.006f;
    [Serialize(Optional = true)] public float ShadyTrapSurvivalReward = 0.5f;
    /// <summary>Spec 36: heat change when a Convoy Duty completes. Negative: a registered convoy hides the players' trail.</summary>
    [Serialize(Optional = true)] public float EscortHeatChange = -8f;
    /// <summary>
    /// Spec 36: Convoy Duty route length, as a share of the distance from the system center to the nearest jump gate.
    /// 0.65 gives about 9,000 in a Hot Cargo system. The route stays inside the gate ring, so a long value can drop offers.
    /// </summary>
    [Serialize(Optional = true)] public float EscortRouteShare = 0.65f;
    /// <summary>Spec 36: share of the route that the freighter covers before the raiders ambush it.</summary>
    [Serialize(Optional = true)] public float EscortAmbushAt = 0.3f;
    /// <summary>Spec 36: distance ahead of the freighter, on its route, where the raiders appear.</summary>
    [Serialize(Optional = true)] public float EscortAmbushLead = 1800f;
    /// <summary>
    /// Spec 36: how much convoy raiders want the freighter. They value it like a ship with this many times their own firepower.
    /// Vanilla AI ignores unarmed civilian ships, so without this they fight only the escort. Vanilla Normal AI weighs distance
    /// with exponent 1.5 and keeps its target until another scores 6 times higher, so the freighter needs a large value to win over a nearer player ship.
    /// </summary>
    [Serialize(Optional = true)] public float EscortRaiderFocus = 20f;
}

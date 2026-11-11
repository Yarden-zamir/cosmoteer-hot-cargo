using Halfling.Serialization;

namespace HotCargo.Missions;

[ReflectiveSerialization(Explicit = true)]
public sealed class MissionTunables
{
    [Serialize(Optional = true)] public float ShadyRewardFactor = 2.0f;
    [Serialize(Optional = true)] public float ShadyTrapChanceBase = 0.2f;
    [Serialize(Optional = true)] public float ShadyTrapChancePerHeat = 0.006f;
    [Serialize(Optional = true)] public float ShadyTrapSurvivalReward = 0.5f;
}

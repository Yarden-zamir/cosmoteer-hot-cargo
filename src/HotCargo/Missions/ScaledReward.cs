using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;

namespace HotCargo.Missions;

/// <summary>The mission tunable that a scaled reward multiplies by.</summary>
public enum RewardScaleTunable
{
    ShadyRewardFactor,
    ShadyTrapSurvivalReward,
}

/// <summary>
/// Rules type "HotCargoScaled": wraps a vanilla Money or Fame reward and multiplies its amount by a mission tunable.
/// The factor is read when the offer spawns, so a tunables reload changes only offers that spawn later.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoScaled")]
[TrackMultipleReferences]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class ScaledRewardSpawner : RewardSpawner
{
    [Serialize, PortableHash] public RewardScaleTunable Tunable;
    [Serialize, PortableHash] public RewardSpawner Reward = null!;
    [Serialize(Optional = true), PortableHash] public int RoundToMultiple = 1;

    public override Reward? CreateReward(Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, Range<int>? tierRange, ID<FactionRules>? faction)
    {
        Reward? reward = Reward.CreateReward(mission, rand, objects, loc, tierRange, faction);
        if (reward is null) return null;
        var t = MissionArea.TunablesFor(mission.Game);
        float factor = Tunable switch
        {
            RewardScaleTunable.ShadyRewardFactor => t.ShadyRewardFactor,
            RewardScaleTunable.ShadyTrapSurvivalReward => t.ShadyTrapSurvivalReward,
            _ => throw new InvalidOperationException($"unknown reward tunable {Tunable}"),
        };
        switch (reward)
        {
            case MoneyReward m: m.Money = MissionModel.ScaleReward(m.Money, factor, RoundToMultiple); break;
            case FameReward f: f.Fame = MissionModel.ScaleReward(f.Fame, factor, RoundToMultiple); break;
            default: throw new InvalidOperationException($"HotCargoScaled supports Money and Fame rewards, not {reward.GetType().Name}");
        }
        return reward;
    }
}

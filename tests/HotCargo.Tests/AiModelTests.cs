using HotCargo.Ai;
using HotCargo.Hunters;
using Xunit;

namespace HotCargo.Tests;

public class AiModelTests
{
    static readonly Func<float> NoRoll = () => throw new InvalidOperationException("no roll expected");

    [Fact]
    public void Flee_always_without_weapons_and_rolls_once_per_drop_below_flee_health()
    {
        var idle = new FleeState(false, false);
        Assert.True(AiModel.NextFlee(idle, hasWeapons: false, 1f, 0.25f, 0.6f, 0f, canStop: true, NoRoll).Fleeing);
        // Healthy: no roll, and the roll re-arms.
        Assert.Equal(idle, AiModel.NextFlee(new FleeState(false, true), true, 0.5f, 0.25f, 0.6f, 0.5f, true, NoRoll));
        // Below flee health: one roll decides.
        Assert.Equal(new FleeState(true, true), AiModel.NextFlee(idle, true, 0.2f, 0.25f, 0.6f, 0.5f, true, () => 0.4f));
        Assert.Equal(new FleeState(false, true), AiModel.NextFlee(idle, true, 0.2f, 0.25f, 0.6f, 0.5f, true, () => 0.6f));
        // Already rolled for this drop: no second roll.
        Assert.Equal(new FleeState(false, true), AiModel.NextFlee(new FleeState(false, true), true, 0.1f, 0.25f, 0.6f, 1f, true, NoRoll));
    }

    [Fact]
    public void Fleeing_stops_only_when_allowed_with_weapons_and_recovered()
    {
        var fleeing = new FleeState(true, true);
        Assert.False(AiModel.NextFlee(fleeing, true, 0.6f, 0.25f, 0.6f, 0.5f, canStop: true, NoRoll).Fleeing);
        Assert.True(AiModel.NextFlee(fleeing, true, 0.5f, 0.25f, 0.6f, 0.5f, canStop: true, NoRoll).Fleeing);
        Assert.True(AiModel.NextFlee(fleeing, true, 1f, 0.25f, 0.6f, 0.5f, canStop: false, NoRoll).Fleeing);
        Assert.True(AiModel.NextFlee(fleeing, false, 1f, 0.25f, 0.6f, 0.5f, canStop: true, NoRoll).Fleeing);
    }

    [Fact]
    public void Call_starts_runs_completes_and_a_storm_blocks_it()
    {
        Assert.Equal(CallStep.Start, AiModel.NextCall(false, 0, 6, triggered: true, hasListeners: true, inStorm: false, coolingDown: false, allyCalling: false));
        Assert.Equal(CallStep.Wait, AiModel.NextCall(false, 0, 6, true, hasListeners: false, false, false, false));
        Assert.Equal(CallStep.Wait, AiModel.NextCall(false, 0, 6, true, true, false, coolingDown: true, false));
        Assert.Equal(CallStep.Wait, AiModel.NextCall(false, 0, 6, true, true, false, false, allyCalling: true));
        Assert.Equal(CallStep.Wait, AiModel.NextCall(false, 0, 6, triggered: false, true, false, false, false));
        Assert.Equal(CallStep.Blocked, AiModel.NextCall(false, 0, 6, true, true, inStorm: true, false, false));
        Assert.Equal(CallStep.Continue, AiModel.NextCall(true, 5.9, 6, false, false, false, false, false));
        Assert.Equal(CallStep.Complete, AiModel.NextCall(true, 6, 6, false, false, false, false, false));
        Assert.Equal(CallStep.Blocked, AiModel.NextCall(true, 3, 6, false, false, inStorm: true, false, false));
        Assert.Equal(0.5f, AiModel.CallProgress(3, 6), 3);
        Assert.True(AiModel.TookDamage(0.9f, 0.85f, 0.02f));
        Assert.False(AiModel.TookDamage(0.9f, 0.89f, 0.02f));
    }

    [Fact]
    public void Regroup_only_when_weaker_and_short_of_nearby_allies()
    {
        Assert.True(AiModel.ShouldRegroup(100, 400, 0.75f, alliesNear: 0, alliesTotal: 2, minGroup: 2, timedOut: false, coolingDown: false));
        Assert.False(AiModel.ShouldRegroup(400, 400, 0.75f, 0, 2, 2, false, false));
        Assert.False(AiModel.ShouldRegroup(100, 400, 0.75f, alliesNear: 1, alliesTotal: 2, minGroup: 2, false, false));
        Assert.False(AiModel.ShouldRegroup(100, 400, 0.75f, alliesNear: 0, alliesTotal: 0, minGroup: 2, false, false));
        Assert.False(AiModel.ShouldRegroup(100, 400, 0.75f, 0, 2, 2, timedOut: true, false));
        Assert.False(AiModel.ShouldRegroup(100, 400, 0.75f, 0, 2, 2, false, coolingDown: true));
        Assert.Equal(3f, AiModel.FocusFactor(0f, 2f), 3);
        Assert.Equal(1f, AiModel.FocusFactor(1.5f, 2f), 3);
    }

    [Fact]
    public void Strike_scales_with_the_run_and_splits_into_waves()
    {
        Assert.Equal(0f, WaveModel.Progress(1, 8));
        Assert.Equal(1f, WaveModel.Progress(8, 8));
        Assert.Equal(1f, WaveModel.Progress(1, 1));
        Assert.Equal(2, WaveModel.ShipCount(3, 0.67f, 0f));
        Assert.Equal(3, WaveModel.ShipCount(3, 0.67f, 1f));
        Assert.Equal(1, WaveModel.ShipCount(1, 0.1f, 0f));
        Assert.Equal(1, WaveModel.TierOffset(2, 1, 0f));
        Assert.Equal(2, WaveModel.TierOffset(2, 1, 1f));
        Assert.Equal(new[] { 2, 2, 1 }, WaveModel.SplitWaves(5, 3));
        Assert.Equal(new[] { 1, 1 }, WaveModel.SplitWaves(2, 3));
        Assert.Equal(new[] { 4 }, WaveModel.SplitWaves(4, 1));
        Assert.Empty(WaveModel.SplitWaves(0, 3));
    }
}

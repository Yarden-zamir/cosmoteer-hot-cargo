using HotCargo.Tactics;
using Xunit;

namespace HotCargo.Tests;

public class TacticsModelTests
{
    [Fact]
    public void Kite_holds_only_when_the_hold_distance_outranges_the_target()
    {
        Assert.Equal(900f, TacticsModel.KiteEdgeDistance(1000f, 500f, 0.9f)!.Value, 3);
        Assert.Null(TacticsModel.KiteEdgeDistance(1000f, 950f, 0.9f));
        Assert.Null(TacticsModel.KiteEdgeDistance(0f, 0f, 0.9f));
    }

    [Fact]
    public void Pincer_sides_alternate_around_the_leader_and_fit_the_group_in_a_circle()
    {
        Assert.Equal(0f, TacticsModel.PincerOffsetDegrees(0, 2, 120f));
        Assert.Equal(120f, TacticsModel.PincerOffsetDegrees(1, 2, 120f));
        Assert.Equal(-120f, TacticsModel.PincerOffsetDegrees(2, 3, 120f));
        // Four ships cannot be 120 degrees apart; the step shrinks to 90.
        Assert.Equal(90f, TacticsModel.PincerOffsetDegrees(1, 4, 120f));
        Assert.Equal(180f, TacticsModel.PincerOffsetDegrees(3, 4, 120f));
        Assert.Throws<ArgumentOutOfRangeException>(() => TacticsModel.PincerOffsetDegrees(2, 2, 120f));
    }

    [Fact]
    public void Fall_back_starts_below_trigger_and_ends_at_recover_timeout_or_never_during_cooldown()
    {
        Assert.True(TacticsModel.FallBackActive(false, 0.3f, 0.35f, 0.6f, timedOut: false, coolingDown: false));
        Assert.False(TacticsModel.FallBackActive(false, 0.3f, 0.35f, 0.6f, timedOut: false, coolingDown: true));
        Assert.True(TacticsModel.FallBackActive(true, 0.5f, 0.35f, 0.6f, timedOut: false, coolingDown: false));
        Assert.False(TacticsModel.FallBackActive(true, 0.6f, 0.35f, 0.6f, timedOut: false, coolingDown: false));
        Assert.False(TacticsModel.FallBackActive(true, 0.1f, 0.35f, 0.6f, timedOut: true, coolingDown: false));
    }

    [Fact]
    public void Bait_pulls_back_when_close_or_late_and_returns_after_the_pull_back_time()
    {
        Assert.Equal(BaitPhase.Approach, TacticsModel.NextBaitPhase(BaitPhase.Approach, 5, 400f, 150f, 20f, 12f));
        Assert.Equal(BaitPhase.PullBack, TacticsModel.NextBaitPhase(BaitPhase.Approach, 5, 160f, 150f, 20f, 12f));
        Assert.Equal(BaitPhase.PullBack, TacticsModel.NextBaitPhase(BaitPhase.Approach, 20, 400f, 150f, 20f, 12f));
        Assert.Equal(BaitPhase.PullBack, TacticsModel.NextBaitPhase(BaitPhase.PullBack, 11, 50f, 150f, 20f, 12f));
        Assert.Equal(BaitPhase.Approach, TacticsModel.NextBaitPhase(BaitPhase.PullBack, 12, 50f, 150f, 20f, 12f));
    }
}

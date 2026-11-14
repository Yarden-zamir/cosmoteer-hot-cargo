using HotCargo.Scavenge;
using Xunit;

namespace HotCargo.Tests;

public class ScavengeModelTests
{
    [Theory]
    [InlineData(100.0, 159.9, 60.0, false)]  // fresh: the cooldown did not pass
    [InlineData(100.0, 160.0, 60.0, true)]   // exactly the cooldown
    [InlineData(100.0, 400.0, 60.0, true)]
    public void A_target_is_ripe_only_after_the_cooldown(double firstSeen, double now, double cooldown, bool ripe) =>
        Assert.Equal(ripe, ScavengeModel.Ripe(firstSeen, now, cooldown));

    [Theory]
    [InlineData(0.95f, true, true, ScavengeStep.Sell)]      // full: sell, even with a target
    [InlineData(0.95f, true, false, ScavengeStep.Idle)]     // full and nowhere to sell (or a ship that keeps its loot)
    [InlineData(0.50f, true, true, ScavengeStep.Collect)]
    [InlineData(0.50f, false, true, ScavengeStep.Sell)]     // nothing left to take: sell what it holds
    [InlineData(0.50f, false, false, ScavengeStep.Idle)]
    [InlineData(0f, false, true, ScavengeStep.Idle)]        // empty, nothing to take
    [InlineData(0f, true, false, ScavengeStep.Collect)]
    public void Next_step_follows_fill_target_and_station(float fill, bool hasTarget, bool canSell, ScavengeStep step) =>
        Assert.Equal(step, ScavengeModel.Next(fill, hasTarget, canSell, fullFraction: 0.9f));

    [Fact]
    public void A_fill_outside_0_to_1_is_a_bug() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ScavengeModel.Next(1.5f, true, true, 0.9f));
}

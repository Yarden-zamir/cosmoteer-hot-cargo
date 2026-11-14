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

    [Theory]
    [InlineData(Side.Human, false, false)]   // friendly to players by default
    [InlineData(Side.Human, true, true)]     // until a player provokes them
    [InlineData(Side.Faction, true, false)]  // never enemies of the vanilla factions
    [InlineData(Side.Neutral, true, false)]
    public void Relations_follow_the_side_and_the_provocation(Side side, bool provoked, bool enemies) =>
        Assert.Equal(enemies, ScavengeModel.Enemies(side, provoked));

    [Fact]
    public void Pirates_and_hunters_keep_the_vanilla_rule() => Assert.Null(ScavengeModel.Enemies(Side.Other, provoked: false));

    [Theory]
    [InlineData(20f, 0f, 1f, 0f, 15f, true)]    // flies straight at the scavenger
    [InlineData(10f, 0f, 1f, 0f, 15f, false)]   // too slow
    [InlineData(0f, 20f, 1f, 0f, 15f, false)]   // flies across, not toward it
    [InlineData(-20f, 0f, 1f, 0f, 15f, false)]  // flies away
    public void A_threat_comes_its_way_when_it_flies_toward_it_fast_enough(float vx, float vy, float dx, float dy, float speed, bool approaching) =>
        Assert.Equal(approaching, ScavengeModel.Approaching(vx, vy, dx, dy, speed));

    [Theory]
    [InlineData(false, 10f, 50f, true, 900f, true)]                  // weaker, a threat comes its way: flee
    [InlineData(false, 100f, 50f, true, 900f, false)]                // clearly stronger (2x): stay
    [InlineData(false, 10f, 0f, true, 900f, false)]                  // an unarmed threat: stay
    [InlineData(false, 10f, 50f, false, 900f, false)]                // the threat does not come its way
    [InlineData(true, 10f, 50f, false, 900f, true)]                  // fleeing: keep going until safe
    [InlineData(true, 10f, 50f, false, 3000f, false)]                // safe away: stop
    [InlineData(false, 10f, 50f, true, float.PositiveInfinity, false)] // no threat at all
    public void Flees_unless_clearly_stronger(bool fleeing, float own, float threats, bool approaching, float nearest, bool flees) =>
        Assert.Equal(flees, ScavengeModel.Flees(fleeing, own, threats, ratio: 2f, approaching, nearest, safeDistance: 2500f));
}

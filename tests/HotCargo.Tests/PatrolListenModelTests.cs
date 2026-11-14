using HotCargo.Events;
using Xunit;

namespace HotCargo.Tests;

public class PatrolListenModelTests
{
    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(50f, 0.8f)]
    [InlineData(100f, 0.6f)]
    [InlineData(140f, 0.6f)]
    [InlineData(-5f, 1f)]
    public void Chance_falls_in_a_line_with_heat(float heat, float expected) =>
        Assert.Equal(expected, PatrolListenModel.SuccessChance(heat, 100f, 1f, 0.6f), 4);

    [Fact]
    public void A_chance_outside_0_to_1_is_clamped() =>
        Assert.Equal(1f, PatrolListenModel.SuccessChance(0f, 100f, 1.5f, 0.6f));

    [Fact]
    public void Max_heat_must_be_above_0() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PatrolListenModel.SuccessChance(10f, 0f, 1f, 0.6f));

    [Fact]
    public void Progress_runs_from_0_to_1()
    {
        Assert.Equal(0f, PatrolListenModel.Progress(10, 10, 8));
        Assert.Equal(0.5f, PatrolListenModel.Progress(14, 10, 8));
        Assert.Equal(1f, PatrolListenModel.Progress(30, 10, 8));
    }

    [Fact]
    public void A_ship_that_never_fired_is_not_in_combat()
    {
        Assert.False(PatrolListenModel.FiredRecently(3, 0, 5));
        Assert.True(PatrolListenModel.FiredRecently(103, 100, 5));
        Assert.False(PatrolListenModel.FiredRecently(106, 100, 5));
    }
}

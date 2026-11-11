using HotCargo.Missions.Mechanics;
using Xunit;

namespace HotCargo.Tests;

public class MechanicsModelTests
{
    [Fact]
    public void Wave_count_grows_with_tier_and_stops_at_the_live_cap()
    {
        Assert.Equal(2, MechanicsModel.WaveShipCount(2f, 0.25f, 1, 0, 10));
        Assert.Equal(5, MechanicsModel.WaveShipCount(2f, 0.25f, 12, 0, 10));
        Assert.Equal(1, MechanicsModel.WaveShipCount(2f, 0.25f, 12, 9, 10));
        Assert.Equal(0, MechanicsModel.WaveShipCount(2f, 0.25f, 12, 12, 10));
    }

    [Fact]
    public void Wave_tier_stays_in_one_to_max()
    {
        Assert.Equal(1, MechanicsModel.WaveShipTier(1f, 0.2f, 1, 3));
        Assert.Equal(3, MechanicsModel.WaveShipTier(1f, 0.2f, 12, 3));
        Assert.Equal(1, MechanicsModel.WaveShipTier(-1f, 1f, 1, 99));
        Assert.Equal(7, MechanicsModel.WaveShipTier(-1f, 1f, 8, 99));
    }

    [Fact]
    public void Waves_are_due_at_the_first_wave_time_and_then_once_per_interval()
    {
        Assert.Equal(0, MechanicsModel.WavesDue(0, 7.9, 8, 35));
        Assert.Equal(1, MechanicsModel.WavesDue(7.9, 8.1, 8, 35));
        Assert.Equal(0, MechanicsModel.WavesDue(8.1, 42.9, 8, 35));
        Assert.Equal(1, MechanicsModel.WavesDue(42.9, 43.1, 8, 35));
        // A big jump (a load, a test skip) counts every wave it crosses.
        Assert.Equal(3, MechanicsModel.WavesDue(0, 80, 8, 35));
        Assert.Throws<ArgumentOutOfRangeException>(() => MechanicsModel.WavesDue(0, 1, 0, 0));
    }

    [Fact]
    public void Leaving_early_keeps_one_share_per_whole_wave_interval()
    {
        Assert.Equal(9, MechanicsModel.Intervals(300, 35));
        Assert.Equal(0f, MechanicsModel.EarnedFraction(34.9, 300, 35));
        Assert.Equal(2f / 9f, MechanicsModel.EarnedFraction(70, 300, 35), 4);
        Assert.Equal(0.5f, MechanicsModel.EarnedFraction(100, 180, 45), 4);
        Assert.Equal(1f, MechanicsModel.EarnedFraction(1000, 180, 45));
    }

    [Fact]
    public void Gap_and_countdown_never_go_below_zero()
    {
        Assert.Equal(100f, MechanicsModel.Gap(600f, 200f, 300f));
        Assert.Equal(0f, MechanicsModel.Gap(400f, 200f, 300f));
        Assert.Equal(61, MechanicsModel.SecondsLeft(300, 239.5));
        Assert.Equal(0, MechanicsModel.SecondsLeft(300, 301));
    }
}

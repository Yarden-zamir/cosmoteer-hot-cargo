using HotCargo.Debris;
using Xunit;

namespace HotCargo.Tests;

public class DebrisModelTests
{
    [Fact]
    public void A_piece_waits_then_glows_for_the_last_glow_seconds_then_burns()
    {
        Assert.Equal(DebrisPhase.Waiting, DebrisModel.Phase(0, 240, 60));
        Assert.Equal(DebrisPhase.Waiting, DebrisModel.Phase(179.9, 240, 60));
        Assert.Equal(DebrisPhase.Glowing, DebrisModel.Phase(180, 240, 60));
        Assert.Equal(DebrisPhase.Glowing, DebrisModel.Phase(239.9, 240, 60));
        Assert.Equal(DebrisPhase.Burning, DebrisModel.Phase(240, 240, 60));
        Assert.Equal(DebrisPhase.Burning, DebrisModel.Phase(1e6, 240, 60));
    }

    [Fact]
    public void No_glow_time_means_no_glow()
    {
        Assert.Equal(DebrisPhase.Waiting, DebrisModel.Phase(239, 240, 0));
        Assert.Equal(DebrisPhase.Burning, DebrisModel.Phase(240, 240, 0));
        Assert.Equal(0f, DebrisModel.GlowLevel(239, 240, 0));
    }

    [Fact]
    public void Glow_level_rises_from_0_to_1_inside_the_glow_only()
    {
        Assert.Equal(0f, DebrisModel.GlowLevel(100, 240, 60));
        Assert.Equal(0f, DebrisModel.GlowLevel(180, 240, 60));
        Assert.Equal(0.5f, DebrisModel.GlowLevel(210, 240, 60), 5);
        Assert.True(DebrisModel.GlowLevel(239.9, 240, 60) > 0.99f);
        Assert.Equal(0f, DebrisModel.GlowLevel(240, 240, 60));
    }

    [Fact]
    public void Check_ticks_is_at_least_one()
    {
        Assert.Equal(60, DebrisModel.CheckTicks(2, 1.0 / 30));
        Assert.Equal(1, DebrisModel.CheckTicks(0.001, 1.0 / 30));
        Assert.Equal(1, DebrisModel.CheckTicks(2, 0));
    }
}

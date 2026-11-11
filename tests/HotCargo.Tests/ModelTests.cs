using HotCargo.Core;
using HotCargo.Tech;
using Xunit;

namespace HotCargo.Tests;

public class HeatModelTests
{
    [Fact]
    public void Multiplier_clamps_to_table_ends()
    {
        float[] table = [0.5f, 1f, 2f];
        Assert.Equal(0.5f, HeatModel.Multiplier(table, -3));
        Assert.Equal(2f, HeatModel.Multiplier(table, 99));
        Assert.Throws<ArgumentException>(() => HeatModel.Multiplier([], 0));
    }

    [Fact]
    public void Fame_heat_is_one_mission_of_heat_per_standard_mission_fame()
    {
        Assert.Equal(15f, HeatModel.FameHeat(15f, 1f, 25, 25), 3);
        Assert.Equal(0f, HeatModel.FameHeat(15f, 1f, -10, 25));
        Assert.Equal(30f, HeatModel.FameHeat(15f, 1f, 2, 0), 3); // divisor floor of 1
    }

    [Fact]
    public void Entering_a_system_keeps_the_hotter_of_carry_over_and_stored_heat()
    {
        Assert.Equal(25f, HeatModel.OnEnterSystem(100f, 0f, 0.25f));
        Assert.Equal(60f, HeatModel.OnEnterSystem(100f, 60f, 0.25f));
        Assert.Equal(100f, HeatModel.OnEnterSystem(100f, 500f, 0.25f));
    }

    [Fact]
    public void Stages_reached_counts_thresholds_at_or_below_heat()
    {
        float[] thresholds = [25, 50, 75, 100];
        Assert.Equal(0, HeatModel.StagesReached(24.9f, thresholds));
        Assert.Equal(1, HeatModel.StagesReached(25f, thresholds));
        Assert.Equal(4, HeatModel.StagesReached(100f, thresholds));
    }
}

public class TechModelTests
{
    [Fact]
    public void Value_grows_per_sector_and_in_whole_price_steps()
    {
        Assert.Equal(1000, TechModel.Value(1000, 0.5f, 1f, 60, 1, 0));
        Assert.Equal(2000, TechModel.Value(1000, 0.5f, 1f, 60, 3, 0));
        Assert.Equal(1000, TechModel.Value(1000, 0.5f, 1f, 60, 1, 3599)); // still inside the first step
        Assert.Equal(2000, TechModel.Value(1000, 0.5f, 1f, 60, 1, 3600));
    }

    [Fact]
    public void Vault_damage_loses_one_unit_per_damage_step_and_keeps_the_rest()
    {
        Assert.Equal((0, 50f), TechModel.UnitsLostToDamage(50f, 100f));
        Assert.Equal((2, 30f), TechModel.UnitsLostToDamage(230f, 100f));
        Assert.Throws<ArgumentOutOfRangeException>(() => TechModel.UnitsLostToDamage(1f, 0f));
    }
}

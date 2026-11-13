using HotCargo.Contracts;
using HotCargo.Core;
using HotCargo.Missions;
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
    public void Value_is_a_fraction_of_the_tier_ship_value_in_whole_steps()
    {
        // Ship value 100,000: base 0.25, +0.1 per hour, +0.05 per 100 notoriety, steps of 1 minute and 5 notoriety.
        long V(double seconds, float notoriety) => TechModel.Value(100_000, 0.25f, 0.1f, 0.05f, 1f, 5f, seconds, notoriety);
        Assert.Equal(25_000, V(0, 0));
        Assert.Equal(35_000, V(3600, 0));
        Assert.Equal(30_000, V(0, 100));
        Assert.Equal(V(0, 0), V(59, 4.9f)); // still inside the first time and notoriety steps
        Assert.Equal(25_300, V(0, 6)); // one notoriety step: 0.05 × 5 / 100 of 100,000 = 250, rounded to 100
        Assert.Throws<ArgumentOutOfRangeException>(() => TechModel.Value(0, 0.25f, 0, 0, 1, 5, 0, 0));
    }

    [Fact]
    public void Value_only_goes_up_with_time_and_notoriety()
    {
        long last = 0;
        for (int minute = 0; minute <= 180; minute++)
        {
            long v = TechModel.Value(230_000, 0.25f, 0.1f, 0.05f, 1f, 5f, minute * 60.0, minute * 2.5f);
            Assert.True(v >= last);
            last = v;
        }
    }

    [Fact]
    public void Mid_run_two_units_buy_about_one_ship_of_the_tier()
    {
        // Spec 19 tuning: 1.2 game hours and 250 notoriety. About 0.5 of a ship per unit.
        long v = TechModel.Value(230_000, 0.25f, 0.1f, 0.05f, 1f, 5f, 1.2 * 3600, 250);
        Assert.InRange(2 * v / 230_000.0, 0.9, 1.1);
    }

}

public class MissionModelTests
{
    [Fact]
    public void Trap_chance_follows_the_heat_formula_and_clamps()
    {
        Assert.Equal(0.2f, MissionModel.TrapChance(0.2f, 0.006f, 0f), 4);
        Assert.Equal(0.8f, MissionModel.TrapChance(0.2f, 0.006f, 100f), 4); // spec 39: 80 % at heat 100
        Assert.Equal(1f, MissionModel.TrapChance(0.5f, 0.01f, 100f));
        Assert.Equal(0f, MissionModel.TrapChance(-0.5f, 0f, 50f));
    }

    [Fact]
    public void Trap_roll_is_below_the_chance_and_rejects_rolls_outside_0_to_1()
    {
        Assert.True(MissionModel.IsTrap(0.79f, 0.8f));
        Assert.False(MissionModel.IsTrap(0.8f, 0.8f));
        Assert.False(MissionModel.IsTrap(0f, 0f));
        Assert.True(MissionModel.IsTrap(0.9999f, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => MissionModel.IsTrap(1f, 0.5f));
    }

    [Fact]
    public void Scaled_rewards_round_to_the_multiple_and_keep_at_least_one_multiple()
    {
        Assert.Equal(12000, MissionModel.ScaleReward(6000, 2f, 500));
        Assert.Equal(3000, MissionModel.ScaleReward(6000, 0.5f, 500));
        Assert.Equal(500, MissionModel.ScaleReward(400, 0.5f, 500));
        Assert.Equal(0, MissionModel.ScaleReward(6000, 0f, 500));
        Assert.Equal(3, MissionModel.ScaleReward(5, 0.5f, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MissionModel.ScaleReward(10, 1f, 0));
    }
}

public class ContractsModelTests
{
    [Fact]
    public void Trail_starts_above_the_free_contracts()
    {
        Assert.Equal(0f, ContractsModel.TrailPerMinute(3, 3, 1f));
        Assert.Equal(1f, ContractsModel.TrailPerMinute(4, 3, 1f));
        Assert.Equal(2.5f, ContractsModel.TrailPerMinute(5.5f, 3, 1f));
        Assert.Equal(0f, ContractsModel.TrailPerMinute(5, 3, -1f));
    }

    [Fact]
    public void Fee_is_standard_pay_units_rounded_and_bonus_pays_it_back_with_profit()
    {
        // Tier 1 standard pay 2,500, Fast economy × RewardScale 2 = factor 4: 0.5 units = 5,000.
        Assert.Equal(5000, ContractsModel.Fee(0.5f, 2500, 4f, 500));
        Assert.Equal(0, ContractsModel.Fee(0f, 2500, 4f, 500));
        Assert.Equal(500, ContractsModel.Fee(0.01f, 2500, 1f, 500)); // a fee is at least one step
        Assert.Equal(10000, ContractsModel.Bonus(5000, 2f, 500));
        Assert.Equal(0, ContractsModel.Bonus(0, 2f, 500));
    }
}

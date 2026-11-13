using HotCargo.Factions;
using Xunit;

namespace HotCargo.Tests;

public class FactionsModelTests
{
    [Fact]
    public void Markup_starts_above_the_start_heat_and_reaches_the_max_at_100_in_steps()
    {
        Assert.Equal(1f, FactionsModel.MarkupFactor(0f, 75f, 2f, 5f));
        Assert.Equal(1f, FactionsModel.MarkupFactor(79.9f, 75f, 2f, 5f)); // still the 75 step
        Assert.Equal(1.2f, FactionsModel.MarkupFactor(80f, 75f, 2f, 5f), 5);
        Assert.Equal(1.6f, FactionsModel.MarkupFactor(94.9f, 75f, 2f, 5f), 5);
        Assert.Equal(2f, FactionsModel.MarkupFactor(100f, 75f, 2f, 5f), 5);
        Assert.Equal(2f, FactionsModel.MarkupFactor(130f, 75f, 2f, 5f), 5);
    }

    [Fact]
    public void Crew_price_rises_from_heat_0_in_steps()
    {
        Assert.Equal(1f, FactionsModel.CrewFactor(4.9f, 2f, 5f));
        Assert.Equal(1.5f, FactionsModel.CrewFactor(52f, 2f, 5f), 5);
        Assert.Equal(2f, FactionsModel.CrewFactor(100f, 2f, 5f), 5);
    }

    [Fact]
    public void Scale_rounds_up_and_stays_in_int_range()
    {
        Assert.Equal(16, FactionsModel.Scale(10, 1.6f));
        Assert.Equal(11, FactionsModel.Scale(10, 1.01f));
        Assert.Equal(int.MaxValue, FactionsModel.Scale(int.MaxValue, 2f));
    }

    [Fact]
    public void Roles_always_differ_and_cover_every_faction()
    {
        var allies = new HashSet<int>();
        for (ulong seed = 0; seed < 200; seed++)
        {
            var (ally, rival) = FactionsModel.PickRoles(seed * 7919UL, 5);
            Assert.NotEqual(ally, rival);
            Assert.InRange(ally, 0, 4);
            Assert.InRange(rival, 0, 4);
            allies.Add(ally);
        }
        Assert.Equal(5, allies.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => FactionsModel.PickRoles(1, 1));
    }

    [Fact]
    public void Zone_factor_and_refusal()
    {
        Assert.Equal(1.5f, FactionsModel.TimeHeatFactor(Zone.Ally, 1.5f, 0.6f));
        Assert.Equal(0.6f, FactionsModel.TimeHeatFactor(Zone.Shelter, 1.5f, 0.6f));
        Assert.Equal(1f, FactionsModel.TimeHeatFactor(Zone.Neutral, 1.5f, 0.6f));
        Assert.Equal(1.5f, FactionsModel.NotorietyFactor(Zone.Ally, 1.5f));
        Assert.Equal(1f, FactionsModel.NotorietyFactor(Zone.Shelter, 1.5f));
        Assert.Equal(1f, FactionsModel.NotorietyFactor(Zone.Neutral, 1.5f));
        Assert.False(FactionsModel.Refuses(89.9f, 90f, 1f, 0f));
        Assert.True(FactionsModel.Refuses(90f, 90f, 0.5f, 0.49f));
        Assert.False(FactionsModel.Refuses(95f, 90f, 0.5f, 0.5f));
    }

    [Fact]
    public void A_jump_keeps_the_heat_that_its_cut_leaves()
    {
        Assert.Equal(0.8f, FactionsModel.HeatKept(0.2f), 5);
        Assert.Equal(0.45f, FactionsModel.HeatKept(0.55f), 5);
        Assert.Equal(1f, FactionsModel.HeatKept(-1f));
        Assert.Equal(0f, FactionsModel.HeatKept(2f));
    }

    [Fact]
    public void Blind_spin_up_takes_factor_times_the_vanilla_time()
    {
        Assert.False(FactionsModel.BlindSpinDone(100f, null, 2f));
        Assert.False(FactionsModel.BlindSpinDone(19.9f, 10f, 2f));
        Assert.True(FactionsModel.BlindSpinDone(20f, 10f, 2f));
        Assert.Equal(0.25f, FactionsModel.BlindSpinProgress(0.5f, 3f, null, 2f), 5);
        Assert.Equal(0.75f, FactionsModel.BlindSpinProgress(1f, 15f, 10f, 2f), 5);
        Assert.Equal(1f, FactionsModel.BlindSpinProgress(1f, 30f, 10f, 2f));
    }
}

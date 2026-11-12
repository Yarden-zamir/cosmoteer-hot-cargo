using HotCargo.RunModes;
using Xunit;

namespace HotCargo.Tests;

public class PressureModelTests
{
    [Fact]
    public void Rows_multiply_and_tiers_add()
    {
        var combat = new Pressure(1.3f, 1.34f, 1, 1f, 1f);
        var econ = new Pressure(1f, 1f, 0, 1.4f, 0.8f);
        var crew = new Pressure(1.1f, 1f, 0, 1f, 1f);
        var p = PressureModel.Combine([combat, econ, crew]);
        Assert.Equal(1.43f, p.HeatRate, 4);
        Assert.Equal(1.34f, p.HunterShips, 4);
        Assert.Equal(1, p.HunterTiers);
        Assert.Equal(1.4f, p.ContractFees, 4);
        Assert.Equal(0.8f, p.TechValue, 4);
        Assert.Equal(Pressure.Neutral, PressureModel.Combine([]));
        Assert.True(PressureModel.Combine([Pressure.Neutral, Pressure.Neutral]).IsNeutral);
    }

    [Fact]
    public void Ships_round_half_up_and_never_drop_below_one()
    {
        var hard = Pressure.Neutral with { HunterShips = 1.34f };
        Assert.Equal(1, hard.Ships(1)); // 1.34
        Assert.Equal(3, hard.Ships(2)); // 2.68
        Assert.Equal(4, hard.Ships(3)); // 4.02
        var easy = Pressure.Neutral with { HunterShips = 0.67f };
        Assert.Equal(1, easy.Ships(1)); // 0.67, at least 1
        Assert.Equal(1, easy.Ships(2)); // 1.34
        Assert.Equal(2, easy.Ships(3)); // 2.01
        Assert.Equal(3, Pressure.Neutral.Ships(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => hard.Ships(0));
    }

    [Fact]
    public void Tech_value_keeps_the_rounding_to_100()
    {
        Assert.Equal(15_000, Pressure.Neutral.Tech(15_000));
        Assert.Equal(12_000, (Pressure.Neutral with { TechValue = 0.8f }).Tech(15_000));
        Assert.Equal(103_100, (Pressure.Neutral with { TechValue = 0.9f }).Tech(114_600)); // 103,140
    }

    [Fact]
    public void Fame_tier_bonus_is_the_gap_above_the_system_capped_and_never_negative()
    {
        Assert.Equal(0, PressureModel.FameTierBonus(fameTier: 4, sectorTier: 5, cap: 2));
        Assert.Equal(0, PressureModel.FameTierBonus(5, 5, 2));
        Assert.Equal(1, PressureModel.FameTierBonus(6, 5, 2));
        Assert.Equal(2, PressureModel.FameTierBonus(10, 5, 2));
        Assert.Equal(0, PressureModel.FameTierBonus(10, 5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PressureModel.FameTierBonus(6, 5, -1));
    }
}

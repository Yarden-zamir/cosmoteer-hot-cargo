using HotCargo.Pursuit;
using Xunit;

namespace HotCargo.Tests;

public class PursuitModelTests
{
    [Fact]
    public void Fleet_factor_is_one_at_the_reference_and_never_leaves_the_band()
    {
        Assert.Equal(1f, PursuitModel.FleetFactor(100, 100, 0.15f, 0.15f), 5);
        Assert.Equal(1.15f, PursuitModel.FleetFactor(200, 100, 0.15f, 0.15f), 5);
        Assert.Equal(0.85f, PursuitModel.FleetFactor(50, 100, 0.15f, 0.15f), 5);
        Assert.Equal(1.15f, PursuitModel.FleetFactor(1e9, 100, 0.15f, 0.15f), 5);
        Assert.Equal(0.85f, PursuitModel.FleetFactor(0, 100, 0.15f, 0.15f), 5);
        Assert.Throws<ArgumentOutOfRangeException>(() => PursuitModel.FleetFactor(10, 0, 0.15f, 0.15f));
    }

    [Fact]
    public void Scaled_count_keeps_the_whole_part_rolls_the_fraction_and_never_drops_to_zero()
    {
        Func<double> noRoll = () => throw new InvalidOperationException("no roll expected");
        Assert.Equal(2, PursuitModel.ScaleCount(2, 1f, noRoll));
        Assert.Equal(3, PursuitModel.ScaleCount(4, 0.75f, noRoll));
        Assert.Equal(1, PursuitModel.ScaleCount(1, 0.5f, () => 0.9));
        Assert.Equal(1, PursuitModel.ScaleCount(1, 0.5f, () => 0.1));
        Assert.Equal(2, PursuitModel.ScaleCount(3, 0.7f, () => 0.5)); // 2.1: 0.5 ≥ 0.1, no extra ship
        Assert.Equal(3, PursuitModel.ScaleCount(3, 0.7f, () => 0.05));
        Assert.Equal(0, PursuitModel.ScaleCount(0, 1.2f, noRoll));
    }

    [Fact]
    public void Pacer_checks_run_limit_then_kind_gap_then_global_gap()
    {
        var h = new List<PaceStart> { new("Outage", 100), new("Blockade", 900) };
        Assert.Equal(PaceVerdict.Ok, PursuitModel.MayStart([], "Outage", 0, 2, 600, 300));
        Assert.Equal(PaceVerdict.KindGap, PursuitModel.MayStart(h, "Outage", 650, 2, 600, 300));
        Assert.Equal(PaceVerdict.GlobalGap, PursuitModel.MayStart(h, "Outage", 1000, 2, 600, 300));
        Assert.Equal(PaceVerdict.Ok, PursuitModel.MayStart(h, "Outage", 1200, 2, 600, 300));
        h.Add(new("Outage", 1200));
        Assert.Equal(PaceVerdict.RunLimit, PursuitModel.MayStart(h, "Outage", 99999, 2, 600, 300));
        Assert.Equal(PaceVerdict.RunLimit, PursuitModel.MayStart([], "Hacker", 0, 0, 0, 0));
    }

    [Fact]
    public void Outage_default_gives_about_two_a_run()
    {
        // Sectors 2 to 7 of an 8 sector run, chance 0.5, at most 2 (PursuitTunables defaults).
        Assert.InRange(PursuitModel.ExpectedCapped(6, 0.5, 2), 1.8, 2.0);
        Assert.Equal(0, PursuitModel.ExpectedCapped(6, 0, 2), 9);
        Assert.Equal(2, PursuitModel.ExpectedCapped(6, 1, 2), 9);
        Assert.True(PursuitModel.OutageSector(2, 8, 2));
        Assert.True(PursuitModel.OutageSector(7, 8, 2));
        Assert.False(PursuitModel.OutageSector(8, 8, 2));
        Assert.False(PursuitModel.OutageSector(1, 8, 2));
    }

    [Fact]
    public void Scout_bar_and_clock_texts()
    {
        Assert.Equal(0f, PursuitModel.ReportProgress(1000, 1200));
        Assert.Equal(0.5f, PursuitModel.ReportProgress(1000, 500), 5);
        Assert.Equal(1f, PursuitModel.ReportProgress(0, 50));
        Assert.Equal("#####-----", PursuitModel.Bar(0.5f, 10));
        Assert.Equal("----------", PursuitModel.Bar(-1f, 10));
        Assert.Equal("##########", PursuitModel.Bar(2f, 10));
        Assert.Equal("3:42", PursuitModel.Clock(221.2));
        Assert.Equal("2:00", PursuitModel.Clock(120));
        Assert.Equal("0:00", PursuitModel.Clock(-5));
    }

    [Fact]
    public void Hacker_price_is_a_rounded_share_of_the_tier_ship_value()
    {
        Assert.Equal(30000, PursuitModel.HackerPrice(60000, 0.5f, 1000));
        Assert.Equal(115000, PursuitModel.HackerPrice(230000, 0.5f, 1000));
        Assert.Equal(1000, PursuitModel.HackerPrice(100, 0.5f, 1000));
    }
}

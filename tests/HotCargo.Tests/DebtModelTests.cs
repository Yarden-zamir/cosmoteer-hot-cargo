using HotCargo.Debt;
using Xunit;

namespace HotCargo.Tests;

public class DebtModelTests
{
    [Fact]
    public void Interest_compounds_and_rounds_up()
    {
        Assert.Equal(10_500, DebtModel.Interest(10_000, 0.05f));
        Assert.Equal(11_025, DebtModel.Interest(10_500, 0.05f));
        Assert.Equal(2, DebtModel.Interest(1, 0.05f));
        Assert.Equal(0, DebtModel.Interest(0, 0.05f));
    }

    [Fact]
    public void Stage_rises_at_the_firm_and_hostile_ratios()
    {
        Assert.Equal(DebtStage.Calm, DebtModel.Stage(DebtModel.Ratio(19_999, 10_000), 2f, 3f));
        Assert.Equal(DebtStage.Firm, DebtModel.Stage(DebtModel.Ratio(20_000, 10_000), 2f, 3f));
        Assert.Equal(DebtStage.Hostile, DebtModel.Stage(DebtModel.Ratio(30_000, 10_000), 2f, 3f));
        Assert.Throws<ArgumentOutOfRangeException>(() => DebtModel.Ratio(1, 0));
    }

    [Fact]
    public void Pay_range_is_share_steps_up_to_the_balance_or_the_money()
    {
        Assert.Equal(new PayRange(13, 124, 13), DebtModel.PayRange(12_345, 50_000, 0.1f));
        Assert.Equal(new PayRange(13, 50, 13), DebtModel.PayRange(12_345, 5_099, 0.1f));
        Assert.Null(DebtModel.PayRange(12_345, 1_000, 0.1f));
        Assert.Null(DebtModel.PayRange(0, 1_000, 0.1f));
        // The top value pays the exact balance, never more.
        Assert.Equal(12_345, DebtModel.PayCredits(124, 12_345));
        Assert.Equal(1_300, DebtModel.PayCredits(13, 12_345));
    }

    [Fact]
    public void Options_round_trip_and_reject_unknown_kinds()
    {
        int option = DebtModel.Option(DebtChoice.Pay, 124);
        Assert.Equal((DebtChoice.Pay, 124), DebtModel.Decode(option));
        Assert.Equal((DebtChoice.NotNow, 0), DebtModel.Decode(DebtModel.Option(DebtChoice.NotNow, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => DebtModel.Decode(9 * DebtModel.Span));
        Assert.Throws<ArgumentOutOfRangeException>(() => DebtModel.Option(DebtChoice.Pay, DebtModel.Span));
    }

    [Fact]
    public void Payoff_takes_what_it_can_off_the_debt_and_pays_the_rest()
    {
        Assert.Equal((4_000L, 0L), DebtModel.PayoffSplit(4_000, 10_000));
        Assert.Equal((10_000L, 2_000L), DebtModel.PayoffSplit(12_000, 10_000));
        Assert.Equal((0L, 500L), DebtModel.PayoffSplit(500, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DebtModel.PayoffSplit(-1, 10));
    }

    [Fact]
    public void Help_share_price_and_claim_follow_the_damage()
    {
        Assert.Equal(0.4f, DebtModel.Share(400, 600), 5);
        Assert.Equal(0f, DebtModel.Share(0, 0));
        Assert.Equal(1.6f, DebtModel.HelpPriceUnits(0.4f, 4f), 5);
        Assert.True(DebtModel.ClaimWorks(0.24f, 0.25f));
        Assert.False(DebtModel.ClaimWorks(0.25f, 0.25f));
        Assert.Equal(40, DebtModel.Percent(0.4f));
        Assert.Throws<ArgumentOutOfRangeException>(() => DebtModel.Share(-1, 0));
    }

    [Fact]
    public void Score_loses_the_debt_times_the_factor()
    {
        Assert.Equal(80_000, DebtModel.Score(100_000, 10_000, 2f));
        Assert.Equal(100_000, DebtModel.Score(100_000, 0, 2f));
    }
}

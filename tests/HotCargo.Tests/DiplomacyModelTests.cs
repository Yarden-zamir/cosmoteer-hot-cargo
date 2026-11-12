using HotCargo.Diplomacy;
using Xunit;

namespace HotCargo.Tests;

public class DiplomacyModelTests
{
    [Fact]
    public void Leverage_is_log4_of_the_strength_ratio_clamped()
    {
        Assert.Equal(0f, DiplomacyModel.Leverage(10, 10), 4);
        Assert.Equal(0.5f, DiplomacyModel.Leverage(20, 10), 4);
        Assert.Equal(1f, DiplomacyModel.Leverage(100, 10));
        Assert.Equal(-1f, DiplomacyModel.Leverage(1, 100));
        Assert.Equal(1f, DiplomacyModel.Leverage(5, 0));
        Assert.Equal(-1f, DiplomacyModel.Leverage(0, 5));
        Assert.Equal(0f, DiplomacyModel.Leverage(0, 0));
    }

    [Fact]
    public void Concentration_joins_vaults_within_the_pile_distance()
    {
        var spread = new List<TechPile> { new(0, 0, 2), new(50, 0, 2), new(100, 0, 2) };
        Assert.Equal(2f / 8f, DiplomacyModel.Concentration(spread, 8, 5f), 4);
        var close = new List<TechPile> { new(0, 0, 2), new(3, 0, 2), new(6, 0, 2) };
        Assert.Equal(6f / 8f, DiplomacyModel.Concentration(close, 8, 5f), 4); // a chain of close vaults is one pile
        Assert.Equal(0f, DiplomacyModel.Concentration([], 8, 5f));
    }

    [Fact]
    public void More_units_raise_the_hand_over_chance_and_a_visible_rest_lowers_it()
    {
        float one = DiplomacyModel.HandOverChance(1, 8, 0f, 0f, 0.15f, 0.95f, 0.5f, 0.15f);
        float seven = DiplomacyModel.HandOverChance(7, 8, 0f, 0f, 0.15f, 0.95f, 0.5f, 0.15f);
        float sevenVisible = DiplomacyModel.HandOverChance(7, 8, 0.125f, 0f, 0.15f, 0.95f, 0.5f, 0.15f);
        Assert.Equal(0.15f, one, 4);
        Assert.True(seven > one);
        Assert.True(sevenVisible < seven);
        Assert.Throws<ArgumentOutOfRangeException>(() => DiplomacyModel.HandOverChance(0, 8, 0, 0, 0, 1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiplomacyModel.HandOverChance(9, 8, 0, 0, 0, 1, 0, 0));
    }

    [Fact]
    public void Deal_chance_falls_with_other_hunters_and_is_zero_above_the_limit()
    {
        float alone = DiplomacyModel.DealChance(0, 4, 8, 8, 0.85f, 0.4f);
        float two = DiplomacyModel.DealChance(2, 4, 8, 8, 0.85f, 0.4f);
        float five = DiplomacyModel.DealChance(5, 4, 8, 8, 0.85f, 0.4f);
        Assert.Equal(0.85f, alone, 4);
        Assert.Equal(0.85f * 0.6f, two, 4);
        Assert.Equal(0f, five);
        Assert.True(DiplomacyModel.DealChance(0, 4, 1, 8, 0.85f, 0.4f) < alone);
    }

    [Fact]
    public void Bluff_works_better_with_a_big_share_and_spread_tech()
    {
        float small = DiplomacyModel.BluffChance(1, 8, 0f, 0.6f, 0.2f, 1.2f);
        float big = DiplomacyModel.BluffChance(6, 8, 0f, 0.6f, 0.2f, 1.2f);
        float bigVisible = DiplomacyModel.BluffChance(6, 8, 0.25f, 0.6f, 0.2f, 1.2f);
        Assert.True(big > small);
        Assert.Equal(big - 0.3f, bigVisible, 4);
    }

    [Fact]
    public void Hire_is_never_possible_while_wanted_and_the_offer_counts_up_to_one_and_a_half_fair_prices()
    {
        Assert.Equal(0f, DiplomacyModel.HireChance(10_000, 1_000, 1f, 0f, wanted: true, 0.5f, 0.6f, 0.3f, 0.4f));
        float fair = DiplomacyModel.HireChance(1_000, 1_000, 0f, 0f, false, -0.1f, 0.6f, 0.3f, 0.4f);
        float lots = DiplomacyModel.HireChance(1_500, 1_000, 0f, 0f, false, -0.1f, 0.6f, 0.3f, 0.4f);
        float more = DiplomacyModel.HireChance(9_000, 1_000, 0f, 0f, false, -0.1f, 0.6f, 0.3f, 0.4f);
        Assert.Equal(0.5f, fair, 4);
        Assert.Equal(lots, more, 4);
        Assert.Equal(0f, DiplomacyModel.HireChance(0, 1_000, 0f, 0f, false, -0.1f, 0.6f, 0.3f, 0.4f));
    }

    [Fact]
    public void Surrender_and_betrayal_chances_follow_their_inputs_and_stay_in_range()
    {
        var weak = new DiplomacyModel.SurrenderInputs(1f, 0.8f, 0f, 0f, 0, false, false, 0f);
        var strong = new DiplomacyModel.SurrenderInputs(-1f, 0f, 0f, 0f, 3, true, true, 100f);
        Assert.Equal(0.93f, DiplomacyModel.SurrenderChance(weak, 0.1f, 0.35f, 0.6f, 0.15f, 0.1f, 0.1f, 0.3f, 0.3f), 4);
        Assert.Equal(0f, DiplomacyModel.SurrenderChance(strong, 0.1f, 0.35f, 0.6f, 0.15f, 0.1f, 0.1f, 0.3f, 0.3f));
        float calm = DiplomacyModel.BetrayChance(0f, 0, 0f, false, 0.1f, 0.3f, 0.1f, -0.15f, -0.05f);
        float hot = DiplomacyModel.BetrayChance(100f, 3, 0f, false, 0.1f, 0.3f, 0.1f, -0.15f, -0.05f);
        Assert.True(hot > calm);
        Assert.False(DiplomacyModel.Succeeds(0f, 0f));
        Assert.True(DiplomacyModel.Succeeds(0.999f, 1f));
    }
}

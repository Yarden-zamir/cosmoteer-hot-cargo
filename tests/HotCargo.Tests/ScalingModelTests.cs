using HotCargo.Scaling;
using Xunit;

namespace HotCargo.Tests;

public class ScalingModelTests
{
    [Fact]
    public void Factor_is_the_setting_at_two_players_and_each_extra_player_adds_the_same_step()
    {
        Assert.Equal(1, ScalingModel.Factor(1.5f, 0));
        Assert.Equal(1, ScalingModel.Factor(1.5f, 1));
        Assert.Equal(1.5, ScalingModel.Factor(1.5f, 2), 9);
        Assert.Equal(2.0, ScalingModel.Factor(1.5f, 3), 9);
        Assert.Equal(2.5, ScalingModel.Factor(1.5f, 4), 9);
        Assert.Equal(1, ScalingModel.Factor(1f, 4));
        Assert.Equal(1.1, ScalingModel.Factor(1.1f, 2), 9); // 1.1f is 1.10000002f; the step snap keeps it exact
        Assert.Throws<ArgumentOutOfRangeException>(() => ScalingModel.Factor(0.5f, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScalingModel.Factor(float.NaN, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScalingModel.Factor(1.5f, -1));
    }

    [Fact]
    public void Whole_counts_need_no_roll_and_the_fraction_is_the_chance_of_one_more()
    {
        Func<double> noRoll = () => throw new InvalidOperationException("no roll expected");
        Assert.Equal(3, ScalingModel.Scale(3, 1, noRoll));
        Assert.Equal(3, ScalingModel.Scale(2, 1.5, noRoll));
        Assert.Equal(0, ScalingModel.Scale(0, 2.5, noRoll));
        Assert.Equal(2, ScalingModel.Scale(1, ScalingModel.Factor(1.5f, 3), noRoll));
        Assert.Equal(11, ScalingModel.Scale(10, ScalingModel.Factor(1.1f, 2), noRoll)); // no float noise roll
        Assert.Equal(2, ScalingModel.Scale(1, 1.5, () => 0.49));
        Assert.Equal(1, ScalingModel.Scale(1, 1.5, () => 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScalingModel.Scale(2, 0.9, noRoll));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScalingModel.Scale(-1, 1.5, noRoll));
    }

    [Fact]
    public void Scaled_counts_average_count_times_factor()
    {
        var rand = new Random(1);
        double sum = 0;
        const int runs = 20000;
        for (int i = 0; i < runs; i++) sum += ScalingModel.Scale(3, 1.5, rand.NextDouble);
        Assert.Equal(4.5, sum / runs, 1);
    }

    [Fact]
    public void Caps_round_half_up_without_a_roll()
    {
        Assert.Equal(2, ScalingModel.Cap(1, 1.5));
        Assert.Equal(3, ScalingModel.Cap(2, 1.5));
        Assert.Equal(18, ScalingModel.Cap(12, 1.5));
        Assert.Equal(1, ScalingModel.Cap(1, 1.4));
        Assert.Equal(0, ScalingModel.Cap(0, 2));
    }

    [Fact]
    public void Spacing_shrinks_by_the_root_of_the_factor_so_the_same_area_holds_factor_times_ships()
    {
        Assert.Equal(4500f, ScalingModel.Spacing(4500f, 1));
        Assert.Equal(4500f / MathF.Sqrt(3), ScalingModel.Spacing(4500f, 3), 3);
        // Ships per area grow with 1 / spacing², so they grow by the factor.
        Assert.Equal(2.5, Math.Pow(4500.0 / ScalingModel.Spacing(4500f, 2.5), 2), 4);
        Assert.Equal(0f, ScalingModel.Spacing(0f, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScalingModel.Spacing(4500f, 0.9));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScalingModel.Spacing(-1f, 2));
    }
}

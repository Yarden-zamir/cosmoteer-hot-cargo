using HotCargo.MultiChoice;
using Xunit;

namespace HotCargo.Tests;

public class MultiChoiceModelTests
{
    [Fact]
    public void Selected_chances_add_up_and_stop_at_the_cap()
    {
        Assert.Equal(0f, MultiChoiceModel.Combine([], 0.9f));
        Assert.Equal(0.35f, MultiChoiceModel.Combine([0.05f, 0.3f], 0.9f), 5);
        Assert.Equal(0.9f, MultiChoiceModel.Combine([0.6f, 0.8f, 0.24f], 0.9f), 5);
        Assert.Equal(1f, MultiChoiceModel.Combine([0.7f, 0.7f], 2f), 5);
        Assert.Throws<ArgumentOutOfRangeException>(() => MultiChoiceModel.Combine([float.NaN], 0.9f));
    }

    [Fact]
    public void A_slider_option_scales_from_zero_at_its_minimum_to_its_full_chance_at_its_maximum()
    {
        Assert.Equal(0f, MultiChoiceModel.Scaled(0, 0, 20, 0.6f));
        Assert.Equal(0.3f, MultiChoiceModel.Scaled(10, 0, 20, 0.6f), 5);
        Assert.Equal(0.6f, MultiChoiceModel.Scaled(20, 0, 20, 0.6f), 5);
        Assert.Equal(0.6f, MultiChoiceModel.Scaled(99, 0, 20, 0.6f), 5);
        Assert.Equal(0.6f, MultiChoiceModel.Scaled(3, 3, 3, 0.6f), 5);
    }

    [Fact]
    public void Values_snap_into_the_range_on_the_step_grid()
    {
        Assert.Equal(4, MultiChoiceModel.Snap(5, 0, 20, 4));
        Assert.Equal(8, MultiChoiceModel.Snap(6, 0, 20, 4));
        Assert.Equal(1, MultiChoiceModel.Snap(-3, 1, 6, 1));
        Assert.Equal(6, MultiChoiceModel.Snap(40, 1, 6, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MultiChoiceModel.Snap(1, 0, 5, 0));
    }
}

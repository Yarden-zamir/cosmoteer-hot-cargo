using HotCargo.Stash;
using Xunit;

namespace HotCargo.Tests;

public class StashModelTests
{
    [Fact]
    public void Found_first_chance_grows_per_minute_up_to_the_cap()
    {
        Assert.Equal(0.1f, StashModel.FoundFirstChance(0, 0.1f, 0.04f, 0.9f), 5);
        Assert.Equal(0.34f, StashModel.FoundFirstChance(360, 0.1f, 0.04f, 0.9f), 5);
        Assert.Equal(0.9f, StashModel.FoundFirstChance(3600, 0.1f, 0.04f, 0.9f), 5);
        Assert.Equal(0.1f, StashModel.FoundFirstChance(-60, 0.1f, 0.04f, 0.9f), 5);
    }

    [Fact]
    public void Percent_rounds_like_the_offer_shows_it()
    {
        Assert.Equal(35, StashModel.Percent(0.348f));
        Assert.Equal(0, StashModel.Percent(0f));
        Assert.Equal(100, StashModel.Percent(1f));
    }

    [Fact]
    public void Outcome_is_loot_above_the_chance_else_empty_or_ambush_by_share()
    {
        Assert.Equal(StashState.Looted, StashModel.Outcome(0.5f, 0.35f, 0f, 0.5f));
        Assert.Equal(StashState.Empty, StashModel.Outcome(0.2f, 0.35f, 0.3f, 0.5f));
        Assert.Equal(StashState.Ambush, StashModel.Outcome(0.2f, 0.35f, 0.7f, 0.5f));
        Assert.Equal(StashState.Looted, StashModel.Outcome(0f, 0f, 0f, 0.5f));
    }
}

using HotCargo.Relays;
using Xunit;

namespace HotCargo.Tests;

public class RelaysModelTests
{
    [Fact]
    public void Kind_follows_the_weights_and_a_system_without_a_faction_gets_only_pirates()
    {
        Assert.Equal(LockKind.Closed, RelaysModel.PickKind(0.1f, 1f, 1f, 1f, hasFaction: true));
        Assert.Equal(LockKind.Fee, RelaysModel.PickKind(0.5f, 1f, 1f, 1f, true));
        Assert.Equal(LockKind.Pirates, RelaysModel.PickKind(0.9f, 1f, 1f, 1f, true));
        Assert.Equal(LockKind.Pirates, RelaysModel.PickKind(0.1f, 1f, 1f, 1f, hasFaction: false));
        Assert.Equal(LockKind.None, RelaysModel.PickKind(0.1f, 1f, 1f, 0f, hasFaction: false));
        Assert.Equal(LockKind.Fee, RelaysModel.PickKind(0.99999f, 0f, 1f, 0f, true));
    }

    [Fact]
    public void Bribe_chance_is_zero_up_to_the_min_share_then_linear_to_one()
    {
        Assert.Equal(0f, RelaysModel.BribeChance(500, 1000, 0.5f));
        Assert.Equal(0.5f, RelaysModel.BribeChance(750, 1000, 0.5f), 5);
        Assert.Equal(1f, RelaysModel.BribeChance(1000, 1000, 0.5f));
        Assert.Equal(1f, RelaysModel.BribeChance(5000, 1000, 0.5f));
        Assert.Equal(1f, RelaysModel.BribeChance(0, 0, 0.5f));
    }

    [Fact]
    public void Fee_and_resource_value_never_go_negative()
    {
        Assert.Equal(1.2f, RelaysModel.FeeUnits(0.6f, 0.006f, 100f), 5);
        Assert.Equal(0.6f, RelaysModel.FeeUnits(0.6f, 0.006f, -50f), 5);
        Assert.Equal(80, RelaysModel.ResourceValue(10, 10, 0.8f));
        Assert.Equal(0, RelaysModel.ResourceValue(-5, 10, 0.8f));
    }

    [Fact]
    public void Closed_relay_opens_after_its_time_and_zero_means_never()
    {
        Assert.False(RelaysModel.ClosedOpens(100, 420f));
        Assert.True(RelaysModel.ClosedOpens(420, 420f));
        Assert.False(RelaysModel.ClosedOpens(1e9, 0f));
    }
}

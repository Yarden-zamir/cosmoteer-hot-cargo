using HotCargo.Balance;
using Xunit;

public class BalanceModelTests
{
    [Fact]
    public void Station_fuel_floor_is_fuel_per_million_of_ship_value_with_a_minimum()
    {
        Assert.Equal(100, BalanceModel.StationFuelFloor(1_000_000, 100f, 10)); // tier 13 value: one late relay jump
        Assert.Equal(18, BalanceModel.StationFuelFloor(180_000, 100f, 10));
        Assert.Equal(10, BalanceModel.StationFuelFloor(75_000, 100f, 10)); // the minimum holds early
        Assert.Throws<ArgumentOutOfRangeException>(() => BalanceModel.StationFuelFloor(0, 100f, 10));
    }

    [Fact]
    public void A_trade_never_takes_station_fuel_below_the_floor()
    {
        Assert.Equal(-20, BalanceModel.ClampStationFuelDelta(-20, 100, 50)); // stays above the floor
        Assert.Equal(-50, BalanceModel.ClampStationFuelDelta(-80, 100, 50)); // stops at the floor
        Assert.Equal(0, BalanceModel.ClampStationFuelDelta(-10, 30, 50)); // already below: gives nothing
        Assert.Equal(15, BalanceModel.ClampStationFuelDelta(15, 30, 50)); // deliveries pass
    }
}

using HotCargo.Tactics;
using Xunit;

namespace HotCargo.Tests;

public class RepairModelTests
{
    static readonly RepairOptions All = new(HasRock: true, HasDonor: true, HasStation: true);

    [Fact]
    public void Plan_order_is_cover_donor_station_away()
    {
        Assert.Equal(RepairPlan.Cover, RepairModel.PickPlan(lacksResources: false, shortOfCrew: false, All));
        Assert.Equal(RepairPlan.Donor, RepairModel.PickPlan(lacksResources: true, shortOfCrew: false, All));
        Assert.Equal(RepairPlan.Station, RepairModel.PickPlan(lacksResources: true, shortOfCrew: false, All with { HasDonor = false }));
        // A donor gives resources, not crew: crew come only from a station.
        Assert.Equal(RepairPlan.Station, RepairModel.PickPlan(lacksResources: false, shortOfCrew: true, All));
        Assert.Equal(RepairPlan.Away, RepairModel.PickPlan(lacksResources: false, shortOfCrew: false, All with { HasRock = false }));
        Assert.Equal(RepairPlan.Away, RepairModel.PickPlan(lacksResources: true, shortOfCrew: true, new(false, false, false)));
    }

    [Fact]
    public void Short_of_crew_below_the_fraction_and_never_without_crew_places()
    {
        Assert.True(RepairModel.ShortOfCrew(1, 4, 0.5f));
        Assert.False(RepairModel.ShortOfCrew(2, 4, 0.5f));
        Assert.False(RepairModel.ShortOfCrew(0, 0, 0.5f));
    }

    [Fact]
    public void Lacking_keeps_only_types_the_ship_cannot_cover()
    {
        var cost = new Dictionary<string, int> { ["steel"] = 50, ["coil"] = 10, ["tristeel"] = 0 };
        var have = new Dictionary<string, int> { ["steel"] = 20, ["coil"] = 30 };
        var lacking = RepairModel.Lacking(cost, t => have.GetValueOrDefault(t));
        Assert.Equal(new Dictionary<string, int> { ["steel"] = 30 }, lacking);
        Assert.Throws<ArgumentOutOfRangeException>(() => RepairModel.Lacking(new Dictionary<string, int> { ["steel"] = -1 }, _ => 0));
    }

    [Fact]
    public void Donor_gives_its_share_at_most_what_is_lacking()
    {
        Assert.Equal(25, RepairModel.DonorGives(lacking: 40, stock: 50, share: 0.5f));
        Assert.Equal(10, RepairModel.DonorGives(lacking: 10, stock: 50, share: 0.5f));
        Assert.Equal(0, RepairModel.DonorGives(lacking: 10, stock: 1, share: 0.5f));
    }

    [Fact]
    public void Trips_get_the_longer_time_limit()
    {
        Assert.Equal(180f, RepairModel.TimeLimit(RepairPlan.Station, 45f, 180f));
        Assert.Equal(180f, RepairModel.TimeLimit(RepairPlan.Donor, 45f, 180f));
        Assert.Equal(45f, RepairModel.TimeLimit(RepairPlan.Cover, 45f, 180f));
        Assert.Equal(45f, RepairModel.TimeLimit(RepairPlan.Away, 45f, 180f));
    }
}

using HotCargo.StartJobs;
using Xunit;

namespace HotCargo.Tests;

public class StartJobsModelTests
{
    static readonly bool[] Pool = [true, false, false, false, false, false, false];

    [Fact]
    public void Always_jobs_come_first_and_the_rest_are_distinct()
    {
        var offer = StartJobsModel.Offer(Pool, 3, 12345UL);
        Assert.Equal(3, offer.Count);
        Assert.Equal(0, offer[0]);
        Assert.Equal(3, offer.Distinct().Count());
        Assert.All(offer.Skip(1), i => Assert.False(Pool[i]));
    }

    [Fact]
    public void The_same_seed_gives_the_same_offer_and_seeds_vary_it()
    {
        Assert.Equal(StartJobsModel.Offer(Pool, 3, 7UL), StartJobsModel.Offer(Pool, 3, 7UL));
        var offers = Enumerable.Range(0, 50).Select(s => string.Join(",", StartJobsModel.Offer(Pool, 3, (ulong)s))).Distinct().Count();
        Assert.True(offers > 10, $"only {offers} different offers from 50 seeds");
    }

    [Fact]
    public void A_count_above_the_pool_offers_every_job()
    {
        Assert.Equal(Pool.Length, StartJobsModel.Offer(Pool, 20, 1UL).Count);
    }

    [Fact]
    public void Bad_counts_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StartJobsModel.Offer(Pool, 0, 1UL));
        Assert.Throws<ArgumentException>(() => StartJobsModel.Offer([true, true, false], 1, 1UL));
    }

    [Fact]
    public void The_serial_follows_the_offer()
    {
        Assert.Equal(StartJobsModel.Serial(["contract", "rush"]), StartJobsModel.Serial(["contract", "rush"]));
        Assert.NotEqual(StartJobsModel.Serial(["contract", "rush"]), StartJobsModel.Serial(["contract", "grudge"]));
        Assert.True(StartJobsModel.Serial(["contract"]) > 0);
    }

    [Fact]
    public void The_run_is_at_least_one_sector()
    {
        Assert.Equal(10, StartJobsModel.Sectors(8, 2));
        Assert.Equal(1, StartJobsModel.Sectors(2, -5));
    }
}

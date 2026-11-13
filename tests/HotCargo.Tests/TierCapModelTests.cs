using HotCargo.Journey;
using Xunit;

namespace HotCargo.Tests;

// The run map rules (mod/journey/galaxy.rules): TierPerJump 2, MaxChangeableLowTier = FinalSectorTier (12).
public class TierCapModelTests
{
    const int PerJump = 2, MaxLow = 12;

    [Fact]
    public void Peak_tier_cap_grows_by_tier_per_jump_from_sector_two()
    {
        Assert.Equal(1, TierCapModel.PeakTierCap(1, PerJump, 1));
        Assert.Equal(3, TierCapModel.PeakTierCap(1, PerJump, 2));
        Assert.Equal(7, TierCapModel.PeakTierCap(1, PerJump, 4));
        Assert.Equal(1, TierCapModel.PeakTierCap(1, PerJump, 0)); // no negative jumps
    }

    [Fact]
    public void Route_tier_stops_at_the_final_sector_tier()
    {
        Assert.Equal(7, TierCapModel.RouteTier(1, PerJump, MaxLow, 4));
        Assert.Equal(MaxLow, TierCapModel.RouteTier(1, PerJump, MaxLow, 8)); // 1 + 2 x 7 = 15, capped
    }

    [Fact]
    public void Capped_lowers_a_system_above_the_cap_and_keeps_its_width()
    {
        Assert.Equal((5, 7), TierCapModel.Capped(9, 11, 5, MaxLow));
        Assert.Null(TierCapModel.Capped(5, 7, 5, MaxLow)); // at the cap
        Assert.Null(TierCapModel.Capped(13, 14, 5, MaxLow)); // a faction home system keeps its tier
    }

    [Fact]
    public void Jump_distances_are_breadth_first_and_do_not_depend_on_connection_order()
    {
        // 0 - 1 - 2 - 3 and a shortcut 0 - 3: node 3 is one jump away, node 2 two.
        var edges = new Dictionary<int, int[]> { [0] = [1, 3], [1] = [0, 2], [2] = [1, 3], [3] = [2, 0] };
        var jumps = TierCapModel.JumpDistances(0, n => edges[n]);
        Assert.Equal(new Dictionary<int, int> { [0] = 0, [1] = 1, [2] = 2, [3] = 1 }, jumps);
        var reversed = TierCapModel.JumpDistances(0, n => edges[n].Reverse());
        Assert.Equal(jumps, reversed);
    }
}

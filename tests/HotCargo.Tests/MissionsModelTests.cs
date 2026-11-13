using System.Numerics;
using HotCargo.Missions;
using Xunit;

namespace HotCargo.Tests;

public class ConvoyRouteTests
{
    static readonly Vector2[] SunAtCenter = [Vector2.Zero];

    [Fact]
    public void Destination_keeps_the_preferred_direction_when_it_fits()
    {
        var dest = MissionModel.ConvoyDestination(new(8000, 0), new(0, 1), 9000, 14000, 6000, SunAtCenter);
        Assert.NotNull(dest);
        Assert.Equal(new Vector2(8000, 9000), dest.Value, new Vec(1f));
    }

    [Fact]
    public void Destination_turns_until_the_route_is_inside_the_reach_and_clear_of_suns()
    {
        var start = new Vector2(11000, 0);
        // Straight at the sun first: the route crosses it and the destination lies outside the reach.
        var dest = MissionModel.ConvoyDestination(start, new(-1, 0), 9000, 13500, 6000, SunAtCenter);
        Assert.NotNull(dest);
        Assert.Equal(9000, Vector2.Distance(start, dest.Value), 1);
        Assert.True(dest.Value.Length() <= 13500);
        Assert.True(MissionModel.SegmentDistance(start, dest.Value, Vector2.Zero) >= 6000);
    }

    [Fact]
    public void Destination_is_null_when_no_direction_fits() =>
        Assert.Null(MissionModel.ConvoyDestination(new(8000, 0), new(1, 0), 30000, 14000, 6000, SunAtCenter));

    [Fact]
    public void Route_progress_is_the_covered_share_and_never_negative()
    {
        Assert.Equal(0.25f, MissionModel.RouteProgress(8000, 6000), 4);
        Assert.Equal(0f, MissionModel.RouteProgress(8000, 9000));
    }

    sealed class Vec(float tolerance) : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) <= tolerance;
        public int GetHashCode(Vector2 v) => 0;
    }
}

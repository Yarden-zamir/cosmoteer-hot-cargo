using System.Numerics;
using HotCargo.Events;
using Xunit;

namespace HotCargo.Tests;

public class RescueModelTests
{
    static readonly Vector2 From = new(0, 0), To = new(1000, 0);

    [Fact]
    public void A_clear_line_goes_straight_to_the_end() =>
        Assert.Equal(To, RescueModel.Waypoint(From, To, [(new Vector2(500, 400), 100f)], 50f));

    [Fact]
    public void A_zone_on_the_line_gives_a_point_beside_it_on_the_line_side()
    {
        var p = RescueModel.Waypoint(From, To, [(new Vector2(500, 50), 200f)], 50f);
        Assert.Equal(500f, p.X, 3);
        Assert.Equal(50f - 250f, p.Y, 3);
    }

    [Fact]
    public void The_first_zone_along_the_line_wins()
    {
        var p = RescueModel.Waypoint(From, To, [(new Vector2(800, 10), 100f), (new Vector2(300, 10), 100f)], 0f);
        Assert.Equal(300f, p.X, 3);
    }

    [Fact]
    public void A_zone_that_covers_the_end_does_not_count() =>
        Assert.Equal(To, RescueModel.Waypoint(From, To, [(new Vector2(900, 0), 300f)], 0f));
}

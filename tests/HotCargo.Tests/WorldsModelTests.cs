using HotCargo.Worlds;
using Xunit;

namespace HotCargo.Tests;

public class WorldsModelTests
{
    [Fact]
    public void Flow_directions_are_unit_vectors_and_every_seed_maps_to_one()
    {
        for (int i = 0; i < WorldsModel.FlowCount; i++)
        {
            var (x, y) = WorldsModel.Flow(i);
            Assert.Equal(1f, MathF.Sqrt(x * x + y * y), 4);
        }
        var seen = new HashSet<int>();
        for (ulong seed = 0; seed < 200; seed++) seen.Add(WorldsModel.FlowIndex(seed));
        Assert.Equal(WorldsModel.FlowCount, seen.Count); // close seeds spread over all directions
        Assert.Equal(WorldsModel.FlowIndex(12345UL), WorldsModel.FlowIndex(12345UL));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldsModel.Flow(WorldsModel.FlowCount));
    }

    [Fact]
    public void Push_is_full_at_rest_and_against_the_flow_and_stops_at_flow_speed()
    {
        Assert.Equal(10f, WorldsModel.PushAccel(0f, 50f, 10f, 1f), 4);
        Assert.Equal(10f, WorldsModel.PushAccel(-80f, 50f, 10f, 1f), 4);
        Assert.Equal(5f, WorldsModel.PushAccel(25f, 50f, 10f, 1f), 4);
        Assert.Equal(0f, WorldsModel.PushAccel(50f, 50f, 10f, 1f));
        Assert.Equal(0f, WorldsModel.PushAccel(90f, 50f, 10f, 1f)); // never brakes
        Assert.Equal(2.5f, WorldsModel.PushAccel(0f, 50f, 10f, 0.25f), 4); // the cloud edge pushes less
        Assert.Equal(0f, WorldsModel.PushAccel(0f, 0f, 10f, 1f));
    }

    [Fact]
    public void Heat_depth_runs_from_the_edge_to_the_kill_radius()
    {
        Assert.Equal(0f, WorldsModel.HeatDepth(5000f, 1500f, 3750f));
        Assert.Equal(0f, WorldsModel.HeatDepth(3750f, 1500f, 3750f));
        Assert.Equal(0.5f, WorldsModel.HeatDepth(2625f, 1500f, 3750f), 4);
        Assert.Equal(1f, WorldsModel.HeatDepth(1500f, 1500f, 3750f));
        Assert.Equal(1f, WorldsModel.HeatDepth(100f, 1500f, 3750f));
        Assert.Throws<ArgumentException>(() => WorldsModel.HeatDepth(0f, 2000f, 2000f));
    }

    [Fact]
    public void Pass_hold_waits_for_entry_then_counts_and_wins()
    {
        var s = new PassState(0f, 0f, false);
        var (waiting, o1) = WorldsModel.PassStep(s, inside: false, 10f, 30f, 5f);
        Assert.Equal(PassOutcome.Waiting, o1); // an empty pass before the first entry never fails
        Assert.False(waiting.Started);
        var (held, o2) = WorldsModel.PassStep(waiting, inside: true, 20f, 30f, 5f);
        Assert.Equal(PassOutcome.Holding, o2);
        var (_, o3) = WorldsModel.PassStep(held, inside: true, 10f, 30f, 5f);
        Assert.Equal(PassOutcome.Won, o3);
    }

    [Fact]
    public void Pass_is_lost_when_empty_too_long_and_a_return_resets_the_count()
    {
        var held = new PassState(10f, 0f, true);
        var (empty, o1) = WorldsModel.PassStep(held, inside: false, 4f, 30f, 5f);
        Assert.Equal(PassOutcome.Holding, o1);
        Assert.Equal(10f, empty.HeldSeconds); // the clock waits while the pass is empty
        var (back, _) = WorldsModel.PassStep(empty, inside: true, 0.1f, 30f, 5f);
        Assert.Equal(0f, back.EmptySeconds);
        var (_, o2) = WorldsModel.PassStep(empty, inside: false, 1f, 30f, 5f);
        Assert.Equal(PassOutcome.Lost, o2);
    }

    [Fact]
    public void Waves_come_once_per_interval_of_holding()
    {
        Assert.Equal(0, WorldsModel.WavesDue(0f, 34.9f, 35f));
        Assert.Equal(1, WorldsModel.WavesDue(34.9f, 35f, 35f));
        Assert.Equal(2, WorldsModel.WavesDue(30f, 75f, 35f));
        Assert.Equal(0, WorldsModel.WavesDue(40f, 40f, 35f));
        Assert.Equal(0, WorldsModel.WavesDue(0f, 100f, 0f));
    }

    [Fact]
    public void Segment_distance_is_perpendicular_inside_and_to_the_end_outside()
    {
        Assert.Equal(3f, WorldsModel.DistanceToSegment(5f, 3f, 0f, 0f, 10f, 0f), 4);
        Assert.Equal(5f, WorldsModel.DistanceToSegment(13f, 4f, 0f, 0f, 10f, 0f), 4);
        Assert.Equal(5f, WorldsModel.DistanceToSegment(3f, 4f, 0f, 0f, 0f, 0f), 4); // a zero-length band is a point
    }
}

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
    public void Outward_points_across_the_flow_to_the_nearer_side()
    {
        // Flow east: the band sides are north (-Y) and south (+Y).
        Assert.Equal((0f, 1f), WorldsModel.Outward(1f, 0f, 500f, 300f));
        Assert.Equal((0f, -1f), WorldsModel.Outward(1f, 0f, -500f, -300f));
        var (x, y) = WorldsModel.Outward(1f, 0f, 700f, 0f);
        Assert.Equal(1f, MathF.Abs(y)); // on the axis: still a side, never zero
        Assert.Equal(0f, x);
    }

    [Fact]
    public void Push_drives_a_drifting_ship_out_of_the_band_at_its_side()
    {
        (float X, float Y) flow = (1f, 0f), outward = (0f, 1f);
        const float flowSpeed = 45f, accel = 9f, share = 0.3f, halfWidth = 1200f, length = 6000f, dt = 0.05f;
        // At rest: full push along the flow and the side share across it.
        var (ax, ay) = WorldsModel.Push(0f, 0f, flow, outward, flowSpeed, accel, share, 1f);
        Assert.Equal(9f, ax, 4);
        Assert.Equal(2.7f, ay, 4);
        // At the drift velocity: no push either way, and the current never brakes.
        var (sx, sy) = WorldsModel.Push(45f, flowSpeed * share, flow, outward, flowSpeed, accel, share, 1f);
        Assert.Equal(0f, sx, 4);
        Assert.Equal(0f, sy, 4);
        Assert.Equal((0f, 0f), WorldsModel.Push(80f, 30f, flow, outward, flowSpeed, accel, share, 1f));
        // A ship that only drifts, from the upstream end on the axis, leaves the band in bounded time.
        float px = 0f, py = 0f, vx = 0f, vy = 0f, t = 0f;
        while (px < length && py < halfWidth)
        {
            var (fx, fy) = WorldsModel.Push(vx, vy, flow, outward, flowSpeed, accel, share, 1f);
            vx += fx * dt; vy += fy * dt; px += vx * dt; py += vy * dt; t += dt;
            Assert.True(t < 120f, "a drifting ship leaves the band");
        }
        Assert.True(py >= halfWidth, "it leaves at the side, before the far end");
        Assert.Equal(0f, WorldsModel.Push(0f, 0f, flow, outward, flowSpeed, accel, 0f, 1f).Y); // no side share: no side push
    }

    [Fact]
    public void Items_float_with_the_current_against_the_vanilla_drag()
    {
        (float X, float Y) flow = (0f, 1f), outward = (-1f, 0f);
        const float damping = 0.5f, dt = 0.05f;
        float vx = 0f, vy = 0f;
        for (int i = 0; i < 600; i++) // 30 s, 15 drag time constants
        {
            vx -= vx * damping * dt; vy -= vy * damping * dt; // vanilla Nugget.FixedUpdateDynamic
            var (dx, dy) = WorldsModel.ItemDrift(flow, outward, 45f, 0.3f, damping, 1f, dt);
            vx += dx; vy += dy;
        }
        Assert.Equal(45f, vy, 0.1f); // the drift velocity of the current
        Assert.Equal(-13.5f, vx, 0.1f);
        Assert.Equal((0f, 0f), WorldsModel.ItemDrift(flow, outward, 45f, 0.3f, damping, 0f, dt)); // outside the cloud: vanilla drag only
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

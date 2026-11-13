namespace HotCargo.Worlds;

/// <summary>State of a pass hold (spec 222). Started is true once a player ship entered the pass.</summary>
public readonly record struct PassState(float HeldSeconds, float EmptySeconds, bool Started);

public enum PassOutcome { Waiting, Holding, Won, Lost }

/// <summary>Pure math of the system archetypes. No game types, so the unit tests run it on the Mac.</summary>
public static class WorldsModel
{
    /// <summary>
    /// The eight flow directions of a current, as unit vectors in world space (+Y is down on the screen), clockwise from east.
    /// A table instead of sin and cos, so every multiplayer client gets the same bits.
    /// </summary>
    static readonly (float X, float Y)[] Flows =
    [
        (1f, 0f), (0.70710677f, 0.70710677f), (0f, 1f), (-0.70710677f, 0.70710677f),
        (-1f, 0f), (-0.70710677f, -0.70710677f), (0f, -1f), (0.70710677f, -0.70710677f),
    ];

    public static int FlowCount => Flows.Length;

    /// <summary>Spec 221: the flow direction index of a system, from the seed of its map node.</summary>
    public static int FlowIndex(ulong seed) => (int)(Mix(seed) % (ulong)Flows.Length);

    public static (float X, float Y) Flow(int index) =>
        index >= 0 && index < Flows.Length ? Flows[index] : throw new ArgumentOutOfRangeException(nameof(index), index, "no such flow direction");

    /// <summary>
    /// Spec 221: the push along the flow, in units per second squared. Full push at rest or against the flow,
    /// less as the ship's speed along the flow nears the flow speed, and none above it. The current never brakes a ship.
    /// </summary>
    public static float PushAccel(float speedAlongFlow, float flowSpeed, float maxAccel, float strength)
    {
        if (flowSpeed <= 0f || maxAccel <= 0f || strength <= 0f) return 0f;
        float lag = (flowSpeed - speedAlongFlow) / flowSpeed;
        return maxAccel * Math.Min(strength, 1f) * Math.Clamp(lag, 0f, 1f);
    }

    /// <summary>
    /// Spec 221: the outward direction at a point of a current band: across the flow, toward the nearer side of the band.
    /// The offset is from the band center (a point on its axis) to the point. A point exactly on the axis goes to one fixed side,
    /// so nothing stays on the axis.
    /// </summary>
    public static (float X, float Y) Outward(float flowX, float flowY, float offsetX, float offsetY)
    {
        float acrossX = -flowY, acrossY = flowX;
        float side = offsetX * acrossX + offsetY * acrossY >= 0f ? 1f : -1f;
        return (acrossX * side, acrossY * side);
    }

    /// <summary>
    /// Spec 221: the push of the current on a ship, in units per second squared. Along the flow it is PushAccel.
    /// Across the flow, toward the outward side, it is PushAccel with the flow speed and the push times sideShare.
    /// So a drifting ship moves toward the drift velocity flowSpeed × (flow + sideShare × outward) and leaves the band at its side.
    /// </summary>
    public static (float X, float Y) Push(float vx, float vy, (float X, float Y) flow, (float X, float Y) outward,
        float flowSpeed, float maxAccel, float sideShare, float strength)
    {
        float along = PushAccel(vx * flow.X + vy * flow.Y, flowSpeed, maxAccel, strength);
        float across = PushAccel(vx * outward.X + vy * outward.Y, flowSpeed * sideShare, maxAccel * sideShare, strength);
        return (flow.X * along + outward.X * across, flow.Y * along + outward.Y * across);
    }

    /// <summary>
    /// Spec 221: the velocity change of a loose item (a vanilla nugget) in the current for one tick. The vanilla drag
    /// (Nugget.FixedUpdateDynamic: v -= v × damping × dt) pulls an item to rest. This adds damping × drift × dt, so in the
    /// current the drag pulls the item toward the drift velocity instead: the item floats with the current.
    /// </summary>
    public static (float X, float Y) ItemDrift((float X, float Y) flow, (float X, float Y) outward,
        float flowSpeed, float sideShare, float damping, float strength, float dt)
    {
        if (flowSpeed <= 0f || damping <= 0f || strength <= 0f || dt <= 0f) return (0f, 0f);
        float k = flowSpeed * damping * Math.Min(strength, 1f) * dt;
        float side = Math.Max(sideShare, 0f);
        return ((flow.X + outward.X * side) * k, (flow.Y + outward.Y * side) * k);
    }

    /// <summary>
    /// Spec 223: how deep a point is in a sun's heat zone. 0 at the outer edge (and outside), 1 at the kill radius (and inside).
    /// The vanilla heat damage scales with the same inverse distance (PlanetDoodad.ProximityEffects).
    /// </summary>
    public static float HeatDepth(float distance, float killRadius, float edgeRadius)
    {
        if (!(edgeRadius > killRadius)) throw new ArgumentException($"heat zone edge {edgeRadius} must be beyond the kill radius {killRadius}");
        return Math.Clamp((edgeRadius - distance) / (edgeRadius - killRadius), 0f, 1f);
    }

    /// <summary>
    /// Spec 222: one step of a pass hold. Time counts while a player ship is in the pass. After the first entry,
    /// an empty pass counts toward the loss, and a return resets that count.
    /// </summary>
    public static (PassState State, PassOutcome Outcome) PassStep(PassState s, bool inside, float dt, float holdSeconds, float lostSeconds)
    {
        if (dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt), dt, "time step must not be negative");
        if (inside)
        {
            var held = s with { HeldSeconds = s.HeldSeconds + dt, EmptySeconds = 0f, Started = true };
            return (held, held.HeldSeconds >= holdSeconds ? PassOutcome.Won : PassOutcome.Holding);
        }
        if (!s.Started) return (s, PassOutcome.Waiting);
        var empty = s with { EmptySeconds = s.EmptySeconds + dt };
        return (empty, empty.EmptySeconds >= lostSeconds ? PassOutcome.Lost : PassOutcome.Holding);
    }

    /// <summary>Spec 222: raider waves due between two hold times, one every interval of holding.</summary>
    public static int WavesDue(float heldBefore, float heldAfter, float interval) =>
        interval <= 0f ? 0 : Math.Max(0, (int)MathF.Floor(heldAfter / interval) - (int)MathF.Floor(heldBefore / interval));

    /// <summary>Distance from a point to the segment from A to B. Spec 221: a current band is all points within half its width of its axis.</summary>
    public static float DistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax, dy = by - ay;
        float lengthSquared = dx * dx + dy * dy;
        float t = lengthSquared <= 0f ? 0f : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0f, 1f);
        float cx = ax + t * dx - px, cy = ay + t * dy - py;
        return MathF.Sqrt(cx * cx + cy * cy);
    }

    /// <summary>SplitMix64 finalizer: spreads the bits of a seed, so close seeds give unrelated directions.</summary>
    static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}

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

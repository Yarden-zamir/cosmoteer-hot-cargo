namespace HotCargo.ShipAi;

/// <summary>A formation slot: the angle from the leader's nose (degrees, positive = clockwise on screen) and the rank (1 = nearest).</summary>
public readonly record struct Slot(float AngleDegrees, int Rank);

/// <summary>
/// Formation rules for the ship tactics (spec 423 to 427). Pure: no game types, so the unit tests cover it.
/// Vectors are plain (x, y) pairs in world units; y points down on screen.
/// </summary>
public static class FormationModel
{
    /// <summary>Spec 425: the angle of the two arms of the V from the leader's nose: behind the leader, 30 degrees apart from straight back.</summary>
    public const float ArmAngle = 150f;

    /// <summary>
    /// Spec 425: slot index 0, 1, 2, ... fills a V behind the leader: index 0 on the left arm, index 1 on the right arm, then one rank
    /// further out on each arm. Left of the nose is a negative angle (y points down).
    /// </summary>
    public static Slot SlotOf(int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index), index, "a slot index is 0 or more");
        return new Slot(index % 2 == 0 ? -ArmAngle : ArmAngle, index / 2 + 1);
    }

    /// <summary>
    /// Spec 424: whether a circle (an ally's hull) blocks the line from a ship to its target: it touches the segment between the two
    /// points, and the touch point lies between them (not behind the ship or beyond the target).
    /// </summary>
    public static bool BlocksLine(float ax, float ay, float bx, float by, float cx, float cy, float radius)
    {
        float dx = bx - ax, dy = by - ay;
        float lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0f) return false;
        float t = ((cx - ax) * dx + (cy - ay) * dy) / lengthSquared;
        if (t <= 0f || t >= 1f) return false;
        float px = ax + t * dx - cx, py = ay + t * dy - cy;
        return px * px + py * py <= radius * radius;
    }

    /// <summary>
    /// Spec 426: where a ship moves to keep its spacing from an ally that is too near: straight away from the ally, until the edge
    /// distance is the spacing plus a margin (so the ship does not stop right at the threshold). Ships on the same point move along +x.
    /// </summary>
    public static (float X, float Y) SpacingTarget(float sx, float sy, float ax, float ay, float edgeDistance, float spacing, float margin)
    {
        float dx = sx - ax, dy = sy - ay;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        (float ux, float uy) = length > 1e-3f ? (dx / length, dy / length) : (1f, 0f);
        float push = Math.Max(0f, spacing - edgeDistance) + margin;
        return (sx + ux * push, sy + uy * push);
    }
}

using System.Numerics;

namespace HotCargo.Events;

/// <summary>Pure rules of the rescue tow (spec 1127).</summary>
public static class RescueModel
{
    /// <summary>
    /// The next point for a tug that flies from <paramref name="from"/> to <paramref name="to"/>. When the straight line passes within
    /// a zone's radius plus the margin, the point beside the first such zone (along the line), on the side of the line; else the end.
    /// A line through a zone's center turns to its left. A zone that covers the end does not count: the tow must go in.
    /// Limit: one detour at a time; the caller asks again as the tug moves.
    /// </summary>
    public static Vector2 Waypoint(Vector2 from, Vector2 to, IReadOnlyList<(Vector2 Center, float Radius)> zones, float margin)
    {
        Vector2 path = to - from;
        float length2 = path.LengthSquared();
        if (length2 <= 0f) return to;
        (float T, Vector2 Point)? first = null;
        foreach (var (center, radius) in zones)
        {
            float t = Math.Clamp(Vector2.Dot(center - from, path) / length2, 0f, 1f);
            Vector2 closest = from + path * t;
            float clear = radius + margin;
            if (Vector2.DistanceSquared(closest, center) >= clear * clear || Vector2.DistanceSquared(to, center) < clear * clear) continue;
            Vector2 away = closest - center;
            Vector2 side = away.LengthSquared() > 1e-6f ? Vector2.Normalize(away) : Vector2.Normalize(new Vector2(-path.Y, path.X));
            if (first is null || t < first.Value.T) first = (t, center + side * clear);
        }
        return first?.Point ?? to;
    }
}

using System.Numerics;

namespace HotCargo.Missions;

/// <summary>Pure mission math on plain numbers. No game types, so unit tests run on the Mac without the game.</summary>
public static class MissionModel
{
    /// <summary>Spec requirement 39: chance that a shady mission is a trap, clamped to 0..1.</summary>
    public static float TrapChance(float chanceBase, float chancePerHeat, float heat) =>
        Math.Clamp(chanceBase + chancePerHeat * heat, 0f, 1f);

    /// <summary>A roll in [0, 1) below the chance is a trap. Chance 0 is never a trap, chance 1 is always a trap.</summary>
    public static bool IsTrap(float roll01, float chance)
    {
        if (roll01 is < 0f or >= 1f) throw new ArgumentOutOfRangeException(nameof(roll01), roll01, "must be in [0, 1)");
        return roll01 < chance;
    }

    /// <summary>Scales a reward amount and rounds it to the nearest multiple. A positive amount never scales below one multiple.</summary>
    public static int ScaleReward(int amount, float factor, int roundToMultiple)
    {
        if (roundToMultiple < 1) throw new ArgumentOutOfRangeException(nameof(roundToMultiple), roundToMultiple, "must be 1 or more");
        if (factor < 0f) throw new ArgumentOutOfRangeException(nameof(factor), factor, "must not be negative");
        int scaled = (int)Math.Round(amount * (double)factor / roundToMultiple, MidpointRounding.AwayFromZero) * roundToMultiple;
        return amount > 0 && factor > 0f ? Math.Max(roundToMultiple, scaled) : scaled;
    }

    /// <summary>Directions that ConvoyDestination tries: the preferred one, then turns of this many radians to each side in turn.</summary>
    const double RouteTurnStep = Math.PI / 12;

    /// <summary>
    /// Spec 36: the convoy destination, <paramref name="length"/> from the start (center of the system at 0, 0).
    /// It tries the preferred direction first, then turns in steps to each side in turn, until the destination is within
    /// <paramref name="reach"/> of the center and the straight route keeps <paramref name="sunClearance"/> from every sun.
    /// Null when no direction fits.
    /// </summary>
    public static Vector2? ConvoyDestination(Vector2 start, Vector2 preferredDirection, float length, float reach, float sunClearance, IReadOnlyList<Vector2> suns)
    {
        if (length <= 0f) throw new ArgumentOutOfRangeException(nameof(length), length, "must be above 0");
        if (preferredDirection == Vector2.Zero) throw new ArgumentException("must not be zero", nameof(preferredDirection));
        double baseAngle = Math.Atan2(preferredDirection.Y, preferredDirection.X);
        int steps = (int)Math.Round(2 * Math.PI / RouteTurnStep);
        for (int i = 0; i < steps; i++)
        {
            int turn = (i + 1) / 2 * (i % 2 == 0 ? -1 : 1); // 0, +1, -1, +2, -2, ...
            double angle = baseAngle + turn * RouteTurnStep;
            Vector2 dest = start + length * new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            if (dest.Length() <= reach && suns.All(s => SegmentDistance(start, dest, s) >= sunClearance)) return dest;
        }
        return null;
    }

    /// <summary>Distance from a point to the segment from a to b.</summary>
    public static float SegmentDistance(Vector2 a, Vector2 b, Vector2 point)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        float t = lengthSquared == 0f ? 0f : Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, a + t * ab);
    }

    /// <summary>Share of the route that the convoy covered, 0 at the start and 1 at the destination. Never below 0.</summary>
    public static float RouteProgress(float routeLength, float distanceLeft)
    {
        if (routeLength <= 0f) throw new ArgumentOutOfRangeException(nameof(routeLength), routeLength, "must be above 0");
        return Math.Max(0f, 1f - distanceLeft / routeLength);
    }
}

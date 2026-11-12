namespace HotCargo.Journey;

/// <summary>Tier math of the Hot Cargo run map (spec 3, 303, 305). No game types; the unit tests cover it.</summary>
public static class TierCapModel
{
    /// <summary>The highest low tier of a system <paramref name="jumps"/> jumps from the start.</summary>
    public static int Cap(int startTier, int tierPerJump, int jumps) => startTier + tierPerJump * jumps;

    /// <summary>The tier range of a system after the cap: lowered to the cap with the same width, or null when it stays as it is
    /// (at or below the cap, or a faction home system above <paramref name="maxChangeableLowTier"/>).</summary>
    public static (int Min, int Max)? Capped(int min, int max, int cap, int maxChangeableLowTier) =>
        min > maxChangeableLowTier || min <= cap ? null : (cap, cap + (max - min));

    /// <summary>Spec 303: the highest tier that counts toward the peak tier at a sector index (sector 1 is the start).</summary>
    public static int PeakTierCap(int startTier, int tierPerJump, int sectorIndex) => Cap(startTier, tierPerJump, Math.Max(0, sectorIndex - 1));

    /// <summary>Spec 305: the main route tier of a sector index: the peak tier cap, up to <paramref name="maxChangeableLowTier"/>.</summary>
    public static int RouteTier(int startTier, int tierPerJump, int maxChangeableLowTier, int sectorIndex) =>
        Math.Min(PeakTierCap(startTier, tierPerJump, sectorIndex), maxChangeableLowTier);

    /// <summary>Each node reachable from the start with its jump count (breadth-first).</summary>
    public static Dictionary<T, int> JumpDistances<T>(T start, Func<T, IEnumerable<T>> connections) where T : notnull
    {
        var jumps = new Dictionary<T, int> { [start] = 0 };
        var queue = new Queue<T>([start]);
        while (queue.TryDequeue(out var node))
            foreach (var next in connections(node))
                if (jumps.TryAdd(next, jumps[node] + 1)) queue.Enqueue(next);
        return jumps;
    }
}

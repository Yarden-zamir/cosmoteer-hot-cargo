namespace HotCargo.StartJobs;

/// <summary>Spec 1200, 1201, 1204: the offer draw, the page serial and the run length. Pure logic, no game types.</summary>
public static class StartJobsModel
{
    /// <summary>
    /// Spec 1200: the indexes of the offered jobs. The Always jobs come first, in list order. A shuffle from the seed fills the
    /// other places from the other jobs. The same seed and list give the same offer on every machine.
    /// </summary>
    public static List<int> Offer(IReadOnlyList<bool> always, int count, ulong seed)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "offers at least 1 job");
        var first = Enumerable.Range(0, always.Count).Where(i => always[i]).ToList();
        if (first.Count > count) throw new ArgumentException($"{first.Count} Always jobs do not fit in an offer of {count}", nameof(always));
        var rest = Enumerable.Range(0, always.Count).Where(i => !always[i]).ToList();
        ulong state = seed;
        for (int i = rest.Count - 1; i > 0; i--)
        {
            int j = (int)(SplitMix(ref state) % (ulong)(i + 1));
            (rest[i], rest[j]) = (rest[j], rest[i]);
        }
        return [.. first, .. rest.Take(count - first.Count)];
    }

    /// <summary>
    /// Spec 1204: the page serial of an offer, from its job IDs (FNV-1a, so it is the same in every process). Never 0.
    /// A different offer gives a different serial, so the votes on an old offer drop.
    /// </summary>
    public static int Serial(IEnumerable<string> ids)
    {
        uint hash = 2166136261;
        foreach (char c in string.Join("\n", ids))
        {
            hash ^= c;
            hash *= 16777619;
        }
        int serial = (int)(hash & 0x7FFF_FFFF);
        return serial == 0 ? 1 : serial;
    }

    /// <summary>Spec 1201: the sector of the Buyer, the run length with the job's sectors. At least 1.</summary>
    public static int Sectors(int runSectorCount, int jobSectors) => Math.Max(1, runSectorCount + jobSectors);

    static ulong SplitMix(ref ulong state)
    {
        ulong z = state += 0x9E37_79B9_7F4A_7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EBUL;
        return z ^ (z >> 31);
    }
}

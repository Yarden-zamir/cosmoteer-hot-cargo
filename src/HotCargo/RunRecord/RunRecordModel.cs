using System.Text.Json;

namespace HotCargo.RunRecord;

/// <summary>Spec 1221: one past run. Id is the galaxy seed of the career. Score is 0 for a loss.</summary>
public sealed record RunEntry(
    string Id, DateTimeOffset Ended, bool Won, long Score, int Delivered, double Seconds,
    int Sector, int Sectors, float PeakHeat, int VaultsLost, string Job, string JobName, int Players);

/// <summary>Spec 1220: the file content. Version changes when an entry field changes its meaning.</summary>
public sealed record RunRecordFile(int Version, List<RunEntry> Runs)
{
    public const int CurrentVersion = 1;
}

/// <summary>Spec 1220 to 1225: the record logic. Pure, no game types.</summary>
public static class RunRecordModel
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>
    /// Spec 1221, 1222, 1224: the entries with this run's entry put in: a new ID comes first, a known ID keeps its place and its end date.
    /// Null when nothing changes, so the caller writes only on a change. At most max entries; the oldest drop.
    /// </summary>
    public static List<RunEntry>? Upsert(IReadOnlyList<RunEntry> runs, RunEntry entry, int max)
    {
        if (max < 1) throw new ArgumentOutOfRangeException(nameof(max), max, "keeps at least 1 entry");
        int i = runs.ToList().FindIndex(r => r.Id == entry.Id);
        if (i < 0) return [entry, .. runs.Take(max - 1)];
        var kept = entry with { Ended = runs[i].Ended };
        if (kept == runs[i]) return null;
        var list = runs.ToList();
        list[i] = kept;
        return list;
    }

    /// <summary>Spec 1225: the highest score of a won run, or null without a win.</summary>
    public static RunEntry? Best(IEnumerable<RunEntry> runs) => runs.Where(r => r.Won).OrderByDescending(r => r.Score).ThenBy(r => r.Ended).FirstOrDefault();

    /// <summary>Spec 1225: newest first.</summary>
    public static List<RunEntry> Rows(IEnumerable<RunEntry> runs) => [.. runs.OrderByDescending(r => r.Ended)];

    public static string Write(IReadOnlyList<RunEntry> runs) => JsonSerializer.Serialize(new RunRecordFile(RunRecordFile.CurrentVersion, [.. runs]), Json);

    /// <summary>Spec 1220: the entries of a file. Throws on bad JSON and on another version, so the caller never overwrites a file it cannot read.</summary>
    public static List<RunEntry> Read(string json)
    {
        var file = JsonSerializer.Deserialize<RunRecordFile>(json, Json) ?? throw new InvalidDataException("the run record file is empty");
        if (file.Version != RunRecordFile.CurrentVersion) throw new InvalidDataException($"the run record file has version {file.Version}; this build reads {RunRecordFile.CurrentVersion}");
        return file.Runs ?? throw new InvalidDataException("the run record file has no runs list");
    }
}

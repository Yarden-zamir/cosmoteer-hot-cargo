using System.Text.Json;

namespace HotCargo.RunRecord;

/// <summary>Spec 1221: one past run. Id is the galaxy seed of the career. Score is 0 for a loss.</summary>
public sealed record RunEntry(
    string Id, DateTimeOffset Ended, bool Won, long Score, int Delivered, double Seconds,
    int Sector, int Sectors, float PeakHeat, int VaultsLost, int Players);

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

/// <summary>Spec 1229: the columns of the Past Runs table, in display order.</summary>
public enum RunColumn { Date, Result, Score, Delivered, Time, PeakHeat, VaultsLost, Players }

/// <summary>Spec 1229, 1230: the sort of the Past Runs table. Pure, no game types.</summary>
public static class RunSort
{
    /// <summary>Spec 1230: the table opens sorted by score, highest first.</summary>
    public const RunColumn DefaultColumn = RunColumn.Score;

    /// <summary>Spec 1230: a click on the sorted column turns the order around; a click on another column sorts by it, highest or newest first.</summary>
    public static (RunColumn Column, bool Descending) Click(RunColumn sorted, bool descending, RunColumn clicked) =>
        clicked == sorted ? (sorted, !descending) : (clicked, true);

    /// <summary>The runs in the order of the column. Ties go to the newer run, so the order is stable.</summary>
    public static List<RunEntry> Sort(IEnumerable<RunEntry> runs, RunColumn column, bool descending)
    {
        var ordered = column switch
        {
            RunColumn.Date => Order(runs, r => r.Ended, descending),
            RunColumn.Result => Order(runs, r => r.Won, descending),
            RunColumn.Score => Order(runs, r => r.Score, descending),
            RunColumn.Delivered => Order(runs, r => r.Delivered, descending),
            RunColumn.Time => Order(runs, r => r.Seconds, descending),
            RunColumn.PeakHeat => Order(runs, r => r.PeakHeat, descending),
            RunColumn.VaultsLost => Order(runs, r => r.VaultsLost, descending),
            RunColumn.Players => Order(runs, r => r.Players, descending),
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, null),
        };
        return [.. ordered.ThenByDescending(r => r.Ended).ThenBy(r => r.Id, StringComparer.Ordinal)];
    }

    static IOrderedEnumerable<RunEntry> Order<T>(IEnumerable<RunEntry> runs, Func<RunEntry, T> key, bool descending) =>
        descending ? runs.OrderByDescending(key) : runs.OrderBy(key);
}

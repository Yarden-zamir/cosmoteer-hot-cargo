using System.Globalization;
using Cosmoteer.Localization;

namespace HotCargo.RunRecord;

/// <summary>Spec 1225: the "PAST RUNS" lines of the career log tab, as XML for labels. UI only.</summary>
public static class RecordRows
{
    /// <summary>Entry rows in the open tab. For autotests.</summary>
    internal static int Shown;

    public static List<string> Lines(IReadOnlyList<RunEntry> runs)
    {
        var lines = new List<string> { Text("Header") };
        Shown = runs.Count;
        if (runs.Count == 0)
        {
            lines.Add(Text("Empty"));
            return lines;
        }
        lines.Add(RunRecordModel.Best(runs) is { } best ? Format("BestFmt", best.Score.ToString("N0", CultureInfo.CurrentCulture), Job(best)) : Text("NoBest"));
        lines.AddRange(RunRecordModel.Rows(runs).Select(Row));
        return lines;
    }

    static string Row(RunEntry e)
    {
        var time = TimeSpan.FromSeconds(e.Seconds);
        return Format(e.Won ? "RowWonFmt" : "RowLostFmt", e.Ended.LocalDateTime.ToString("d MMM", CultureInfo.CurrentCulture),
            e.Score.ToString("N0", CultureInfo.CurrentCulture), Job(e), e.Sector, e.Sectors,
            $"{(int)time.TotalHours}:{time.Minutes:00}", e.PeakHeat.ToString("0", CultureInfo.InvariantCulture), e.VaultsLost);
    }

    static string Job(RunEntry e) => e.JobName.Length > 0 ? Halfling.StringTools.EscapeForXml(e.JobName) : Text("NoJob");

    internal static string Text(string key) => Strings.GetText("HotCargoRunRecord/" + key);
    internal static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoRunRecord/" + key, args);
}

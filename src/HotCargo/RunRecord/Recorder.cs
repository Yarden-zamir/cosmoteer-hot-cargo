using Cosmoteer;
using HotCargo.Core;
using HotCargo.StartJobs;

namespace HotCargo.RunRecord;

/// <summary>
/// Spec 1220 to 1222: keeps the entry of an ended run in the record file of this machine. UI frames only; no sim state changes.
/// </summary>
public static class Recorder
{
    /// <summary>Limit: a constant, not a tunable, because it is not a design number (spec 1224). Revisit if players want a longer history.</summary>
    public const int MaxEntries = 200;

    public static string FilePath => (string)(Paths.SavedGamesFolder / "hot_cargo_runs.json");

    /// <summary>The entries as the last read or write left them. Null before the first read. The career log tab reads it.</summary>
    internal static List<RunEntry>? Runs { get; private set; }

    /// <summary>Counts reads and writes of the file, so the career log tab knows when to fill its rows again.</summary>
    internal static int Version { get; private set; }

    /// <summary>The last entry that this machine checked for each run, without its end date. A frame with the same numbers does nothing.</summary>
    static RunEntry? _checked;

    /// <summary>Spec 1220, 1232: why the file did not read, or null. The career log block and the Past Runs window show it.</summary>
    public static string? LoadError { get; private set; }

    /// <summary>True after a file that does not read: the record stops for this game session and never overwrites that file.</summary>
    static bool _broken;

    /// <summary>The record file's entries. Null when the file cannot be read; the error shows in the overlay.</summary>
    public static List<RunEntry>? Load()
    {
        if (_broken) return null;
        try
        {
            Runs = File.Exists(FilePath) ? RunRecordModel.Read(File.ReadAllText(FilePath)) : [];
            Version++;
            return Runs;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            _broken = true;
            LoadError = $"{FilePath}: {ex.Message}";
            // The player's own file, not a mod failure: it shows where the record shows (spec 1220), not in the error overlay.
            Log.Info($"run record: cannot read {FilePath}; the record stays as it is: {ex.Message}");
            return null;
        }
    }

    /// <summary>Each UI frame: once the run ended, the record holds its entry with the current numbers (spec 1222).</summary>
    internal static void Update(Run run)
    {
        if (run.State.Result == RunResult.Running || _broken) return;
        var entry = Entry(run);
        if (entry == _checked) return;
        _checked = entry;
        if (Load() is not { } runs) return;
        if (RunRecordModel.Upsert(runs, entry with { Ended = DateTimeOffset.Now }, MaxEntries) is not { } changed) return;
        string temp = FilePath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? throw new InvalidOperationException($"{FilePath} has no folder"));
        File.WriteAllText(temp, RunRecordModel.Write(changed));
        File.Move(temp, FilePath, overwrite: true);
        Runs = changed;
        Version++;
        Log.Info($"run record: {(run.State.Result == RunResult.Won ? "won" : "lost")} run {entry.Id} score {entry.Score} written; {changed.Count} entries");
    }

    /// <summary>Spec 1221: the entry of the run now, with a default end date.</summary>
    internal static RunEntry Entry(Run run)
    {
        var s = run.State;
        var job = StartJobs.StartJobs.Job(run);
        bool won = s.Result == RunResult.Won;
        return new RunEntry(
            Id: run.Mode.Init.RandSeed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Ended: default,
            Won: won,
            Score: won ? s.Score : 0,
            Delivered: s.DeliveredUnits,
            Seconds: Math.Round(s.GameSeconds),
            Sector: s.SectorIndex,
            Sectors: run.SectorCount(),
            PeakHeat: MathF.Round(s.RunRecord.PeakHeat),
            VaultsLost: s.Log.Count(e => e.Kind == Onboarding.RunLogKind.Loss),
            Job: job?.Id ?? "",
            JobName: job is null ? "" : StartJobs.StartJobs.Name(job),
            Players: run.Game.HumanPlayerCount);
    }

    /// <summary>Autotests: forgets the last checked entry and a broken file, so a test starts clean.</summary>
    internal static void ResetForTest()
    {
        _checked = null;
        _broken = false;
        LoadError = null;
        Runs = null;
    }
}

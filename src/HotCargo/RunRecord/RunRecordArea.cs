using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.RunRecord;

/// <summary>Entry point of the RunRecord area (spec 1220 to 1226): past runs of this machine. Mod.Boot calls Register once.</summary>
public static class RunRecordArea
{
    public static void Register()
    {
        Run.Hooks.Tick += (run, _, _) => run.State.RunRecord.PeakHeat = Math.Max(run.State.RunRecord.PeakHeat, run.State.Heat);
        Run.Hooks.Frame += run => Errors.Guard("run record", () => Recorder.Update(run));
        AddAutotest();
    }

    static RunEntry? ThisRun()
    {
        string id = Recorder.Entry(Autotest.RequireRun()).Id;
        return (Recorder.Runs ?? Recorder.Load())?.FirstOrDefault(r => r.Id == id);
    }

    static void AddAutotest()
    {
        // rr_clear: deletes the record file of this test slot and resets the recorder. Test slots only: the slot has its own save folder.
        Autotest.Commands["rr_clear"] = _ =>
        {
            if (File.Exists(Recorder.FilePath)) File.Delete(Recorder.FilePath);
            Recorder.ResetForTest();
        };
        Autotest.Fields["rr_entries"] = () => Recorder.Load()?.Count ?? -1;
        Autotest.Fields["rr_peakheat_state"] = () => Autotest.RequireRun().State.RunRecord.PeakHeat;
        // The entry of the current run in the file: present, won, score, peak heat, lost vaults, delivered units and sectors.
        Autotest.Fields["rr_has"] = () => ThisRun() is null ? 0 : 1;
        Autotest.Fields["rr_won"] = () => ThisRun()?.Won == true ? 1 : 0;
        Autotest.Fields["rr_score"] = () => ThisRun()?.Score ?? -1;
        Autotest.Fields["rr_peakheat"] = () => ThisRun()?.PeakHeat ?? -1;
        Autotest.Fields["rr_vaultslost"] = () => ThisRun()?.VaultsLost ?? -1;
        Autotest.Fields["rr_delivered"] = () => ThisRun()?.Delivered ?? -1;
        Autotest.Fields["rr_sectors"] = () => ThisRun()?.Sectors ?? -1;
        Autotest.Fields["rr_tab_rows"] = () => RecordRows.Shown;
        AddPastRunsAutotest();
    }

    static PastRunsDialog Window() => PastRunsDialog.Open ?? throw new InvalidOperationException("the Past Runs window is not open");

    /// <summary>Spec 1227 to 1233: the Past Runs window. These commands work on the main menu, before a career starts.</summary>
    static void AddPastRunsAutotest()
    {
        // pr_sample: writes three sample runs to this test slot's record file (scores 120,000 won, 340,000 won, 0 lost; newest last).
        Autotest.Commands["pr_sample"] = _ =>
        {
            var day = new DateTimeOffset(2026, 11, 10, 20, 0, 0, TimeSpan.Zero);
            List<RunEntry> runs =
            [
                new("3", day.AddDays(2), false, 0, 0, 1500, 4, 6, 100f, 3, 2),
                new("2", day.AddDays(1), true, 340_000, 6, 5400, 10, 10, 95f, 1, 1),
                new("1", day, true, 120_000, 4, 4200, 8, 8, 80f, 0, 1),
            ];
            Directory.CreateDirectory(Path.GetDirectoryName(Recorder.FilePath)!);
            File.WriteAllText(Recorder.FilePath, RunRecordModel.Write(runs));
            Recorder.ResetForTest();
        };
        // pr_badfile: writes a record file that does not read.
        Autotest.Commands["pr_badfile"] = _ =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Recorder.FilePath)!);
            File.WriteAllText(Recorder.FilePath, "{ not json");
            Recorder.ResetForTest();
        };
        // pr_click: clicks the main menu button like a player (the button's own click event).
        Autotest.Commands["pr_click"] = _ =>
        {
            var button = PastRunsButton.Shown is { } b ? b : throw new InvalidOperationException("no Past Runs button on the main menu");
            button.ClickController.InvokeClicked();
            Autotest.Wait(1);
        };
        // pr_sort <column>: clicks the header of a RunColumn.
        Autotest.Commands["pr_sort"] = arg => Window().SortBy(Enum.Parse<RunColumn>(arg.Trim()));
        Autotest.Commands["pr_close"] = _ => { Window().Close(); Autotest.Wait(1); };
        Autotest.Fields["pr_button"] = () => PastRunsButton.Shown is null ? 0 : 1;
        Autotest.Fields["pr_open"] = () => PastRunsDialog.Open is null ? 0 : 1;
        Autotest.Fields["pr_rows"] = () => Window().Shown.Count;
        // The score of the first table row, and the ID of the first row (the sample IDs are numbers).
        Autotest.Fields["pr_first_score"] = () => Window().Shown[0].Score;
        Autotest.Fields["pr_first_id"] = () => double.Parse(Window().Shown[0].Id, System.Globalization.CultureInfo.InvariantCulture);
        Autotest.Fields["pr_best_row"] = () => Window().BestRow;
        Autotest.Fields["pr_descending"] = () => Window().Descending ? 1 : 0;
        Autotest.Fields["pr_error"] = () => Recorder.LoadError is null ? 0 : 1;
    }
}

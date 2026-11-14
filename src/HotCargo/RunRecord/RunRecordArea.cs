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
        // The entry of the current run in the file: present, won, score, peak heat, lost vaults, delivered units, sectors, and 1 when it names the run's job.
        Autotest.Fields["rr_has"] = () => ThisRun() is null ? 0 : 1;
        Autotest.Fields["rr_won"] = () => ThisRun()?.Won == true ? 1 : 0;
        Autotest.Fields["rr_score"] = () => ThisRun()?.Score ?? -1;
        Autotest.Fields["rr_peakheat"] = () => ThisRun()?.PeakHeat ?? -1;
        Autotest.Fields["rr_vaultslost"] = () => ThisRun()?.VaultsLost ?? -1;
        Autotest.Fields["rr_delivered"] = () => ThisRun()?.Delivered ?? -1;
        Autotest.Fields["rr_sectors"] = () => ThisRun()?.Sectors ?? -1;
        Autotest.Fields["rr_job_matches"] = () => ThisRun() is { } e && e.Job == Autotest.RequireRun().State.StartJobs.Job ? 1 : 0;
        Autotest.Fields["rr_tab_rows"] = () => RecordRows.Shown;
    }
}

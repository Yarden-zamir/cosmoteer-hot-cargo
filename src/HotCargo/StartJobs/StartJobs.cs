using Cosmoteer.Localization;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.RunModes;
using HotCargo.Tech;

namespace HotCargo.StartJobs;

/// <summary>
/// Spec 1200 to 1205: the offer, the effects of the chosen job and its rule texts. Sim logic runs only in Run.Hooks and the
/// shared choice input. The persistent rules read the live tunables at each use, like the difficulty rows.
/// </summary>
public static class StartJobs
{
    /// <summary>The chosen job, or null. A job ID that a live tunables change removed reports once and counts as no job.</summary>
    public static StartJobRow? Job(Run run)
    {
        string id = run.State.StartJobs.Job;
        if (id.Length == 0) return null;
        if (run.T.StartJobs.Jobs.FirstOrDefault(j => j.Id == id) is { } job) return job;
        Errors.Report("start jobs", $"the run's job '{id}' is not in StartJobs.Jobs any more; it counts as no job");
        return null;
    }

    /// <summary>Spec 1200: the jobs that the waiting choice offers, from the live tunables and the galaxy seed.</summary>
    public static List<StartJobRow> Offer(Run run)
    {
        var jobs = run.T.StartJobs.Jobs;
        return [.. StartJobsModel.Offer([.. jobs.Select(j => j.Always)], run.T.StartJobs.OfferCount, (ulong)run.Mode.Init.RandSeed).Select(i => jobs[i])];
    }

    /// <summary>Spec 1204: the shared choice serial of the current offer.</summary>
    public static int Serial(Run run) => StartJobsModel.Serial(Offer(run).Select(j => j.Id));

    /// <summary>True while the players still pick a job.</summary>
    public static bool Waiting(Run run) => run.State.StartJobs.Pending && run.State.Result == RunResult.Running;

    /// <summary>Spec 1201: the Buyer's sector, Journey.RunSectorCount with the job's sectors. Every reader of the run length uses it.</summary>
    public static int SectorCount(this Run run) => StartJobsModel.Sectors(run.T.Journey.RunSectorCount, Job(run)?.Sectors ?? 0);

    /// <summary>Spec 1201: the job's factor on the Buyer unit price.</summary>
    public static float BuyerPrice(Run run) => Job(run)?.BuyerPrice ?? 1f;

    /// <summary>Spec 1201: the job's pressure factors. They combine with the difficulty rows in Difficulty.Pressure.</summary>
    public static Pressure Pressure(Run run) => Job(run)?.ToPressure() ?? RunModes.Pressure.Neutral;

    internal static void OnNewRun(Run run)
    {
        if (!run.T.StartJobs.Enabled || run.T.StartJobs.Jobs.Length == 0) return;
        run.State.StartJobs.Pending = true;
        Log.Info($"start jobs: offer {string.Join(", ", Offer(run).Select(j => j.Id))}");
    }

    /// <summary>Spec 1201, 1205: takes the job of the option. Sim logic: the shared choice input or its vote decision.</summary>
    internal static bool Pick(Run run, SimRoot sim, int option)
    {
        if (!Waiting(run)) return false;
        var offer = Offer(run);
        if (option < 0 || option >= offer.Count) throw new ArgumentOutOfRangeException(nameof(option), option, $"the offer has {offer.Count} jobs");
        var job = offer[option];
        if (job.Tech > 0) throw new InvalidOperationException($"start job {job.Id} adds tech; a run builds no vault (spec 1014)");
        run.State.StartJobs.Pending = false;
        run.State.StartJobs.Job = job.Id;
        int removed = job.Tech < 0 ? Vaults.Remove(sim, -job.Tech) : 0;
        if (job.Heat != 0) run.AddHeat(job.Heat, sim, ignoreBlock: true);
        string rules = string.Join(", ", RuleTexts(run, job));
        Log.Info($"start jobs: picked {job.Id}; tech removed {removed}, heat {run.State.Heat:0.0}, sectors {run.SectorCount()}");
        Onboarding.RunLog.Add(run, Onboarding.RunLogKind.Job, Format("LogFmt", Name(job), rules));
        Run.OnNextFrame(() => Ux.UxKit.Guard("start job line", () =>
        {
            string line = Format("PickedFmt", Name(job), rules);
            Ux.UxKit.CenterText(run.Game, line);
            Ux.UxKit.ChatLine(run.Game, line);
        }));
        return true;
    }

    /// <summary>Spec 1202: one plain text for each rule of the job that changes something, or "No changes."</summary>
    public static List<string> RuleTexts(Run run, StartJobRow job)
    {
        var parts = new List<string>();
        if (job.Tech != 0) parts.Add(Format("TechFmt", -job.Tech));
        if (job.Heat != 0) parts.Add(Format("HeatFmt", job.Heat.ToString("+0;-0", System.Globalization.CultureInfo.InvariantCulture)));
        if (job.Sectors != 0) parts.Add(Format("SectorsFmt", StartJobsModel.Sectors(run.T.Journey.RunSectorCount, job.Sectors)));
        if (job.BuyerPrice != 1f) parts.Add(Format("BuyerFmt", job.BuyerPrice));
        parts.AddRange(Difficulty.FactorTexts(job.ToPressure()));
        if (parts.Count == 0) parts.Add(Text("NoChanges"));
        return parts;
    }

    public static string Name(StartJobRow job) => Text(job.Id + "Name");

    public static string Blurb(StartJobRow job) => Text(job.Id + "Text");

    /// <summary>Spec 1207: the job IDs of the tunables without their two strings.</summary>
    public static List<string> MissingStrings(StartJobsTunables t) =>
        [.. t.Jobs.Where(j => !Strings.TryGetText($"HotCargoStartJobs/{j.Id}Name", out _) || !Strings.TryGetText($"HotCargoStartJobs/{j.Id}Text", out _)).Select(j => j.Id)];

    internal static string Text(string key) => Strings.GetText("HotCargoStartJobs/" + key);
    internal static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoStartJobs/" + key, args);
}

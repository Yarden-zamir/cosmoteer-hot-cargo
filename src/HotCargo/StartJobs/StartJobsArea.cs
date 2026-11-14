using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.SharedChoice;

namespace HotCargo.StartJobs;

/// <summary>Entry point of the StartJobs area (spec 1200 to 1208): the job choice at the run start. Mod.Boot calls Register once.</summary>
public static class StartJobsArea
{
    public static void Register()
    {
        SharedChoices.Register(StartJobsDomain.Instance);
        Run.Hooks.NewRunStarted += StartJobs.OnNewRun;
        Run.Hooks.Frame += run => Errors.Guard("start job panel", () => StartJobPanel.Update(run));
        Run.Hooks.Frame += CheckStrings;
        Onboarding.Hints.Add("hc_jobs", run => run.State.StartJobs.Job.Length > 0);
        AddAutotest();
    }

    static Run? _stringsChecked;

    /// <summary>Spec 1207: at the first frame of a run, each job of the tunables has its strings.</summary>
    static void CheckStrings(Run run)
    {
        if (_stringsChecked == run) return;
        _stringsChecked = run;
        if (StartJobs.MissingStrings(run.T.StartJobs) is { Count: > 0 } missing)
            Errors.Report("start jobs", $"jobs without the strings HotCargoStartJobs/(ID)Name and (ID)Text: {string.Join(", ", missing)}");
    }

    static Run R() => Autotest.RequireRun();

    /// <summary>The index in the current offer of the job with this ID.</summary>
    static int OfferIndex(Run run, string id) =>
        StartJobs.Offer(run).FindIndex(j => j.Id == id) is var i and >= 0 ? i
            : throw new InvalidOperationException($"job '{id}' is not in the offer [{string.Join(", ", StartJobs.Offer(run).Select(j => j.Id))}]");

    static void AddAutotest()
    {
        // sj_set <StartJobsTunables field> <value>: a live tunable for this run. Test only; "mp sj_set" in co-op.
        Autotest.Commands["sj_set"] = arg => Autotest.SetField(R().T.StartJobs, arg);
        // sj_pick <job ID>: sends the pick input like a first-pick click on the panel.
        Autotest.Commands["sj_pick"] = id => R().Game.NetManager.EnqueueInput(StartJobsDomain.PickInput(R(), OfferIndex(R(), id.Trim())));
        // sj_vote <job ID>: sends a vote on the job, like a click in vote mode.
        Autotest.Commands["sj_vote"] = id => R().Game.NetManager.EnqueueInput(
            new SharedChoiceInput(StartJobsDomain.Name, StartJobs.Serial(R()), SharedChoiceAction.Vote, OfferIndex(R(), id.Trim())));
        // sj_click <job ID>: clicks the job's button on this machine's panel, like a player.
        Autotest.Commands["sj_click"] = id => { StartJobPanel.ClickForTest(OfferIndex(R(), id.Trim())); Autotest.Wait(1); };
        // sj_reset: the choice waits again (single player, test only). Rules that applied at the pick (Tech, Heat) stay.
        Autotest.Commands["sj_reset"] = _ =>
        {
            if (R().IsMultiplayer) throw new InvalidOperationException("sj_reset is single player only");
            R().State.StartJobs.Pending = true;
            R().State.StartJobs.Job = "";
        };
        // sj_check_buttons: the panel shows one button per offered job, and each button holds the job's name and every rule text (spec 1202).
        Autotest.Commands["sj_check_buttons"] = _ =>
        {
            var run = R();
            var offer = StartJobs.Offer(run);
            var texts = StartJobPanel.ButtonTexts.ToList();
            Autotest.Check(texts.Count == offer.Count, $"{offer.Count} job buttons (actual {texts.Count})");
            for (int i = 0; i < offer.Count; i++)
            {
                var job = offer[i];
                var want = StartJobs.RuleTexts(run, job).Prepend(StartJobs.Name(job)).Select(Events.EventView.Plain).ToList();
                Autotest.Check(want.All(texts[i].Contains), $"button {i + 1} holds '{string.Join("', '", want)}' (actual '{texts[i]}')");
            }
        };
        Autotest.Fields["sj_pending"] = () => R().State.StartJobs.Pending ? 1 : 0;
        Autotest.Fields["sj_panel"] = () => StartJobPanel.IsShown ? 1 : 0;
        Autotest.Fields["sj_offer"] = () => StartJobs.Offer(R()).Count;
        Autotest.Fields["sj_sectors"] = () => R().SectorCount();
        Autotest.Fields["sj_buyerfactor"] = () => Journey.Buyer.PriceFactor(R());
        // 1 + the index in StartJobs.Jobs of the chosen job, 0 without a job.
        Autotest.Fields["sj_job"] = () => StartJobs.Job(R()) is { } job ? Array.IndexOf(R().T.StartJobs.Jobs, job) + 1 : 0;
        // Other players that hover a job on this machine's panel.
        Autotest.Fields["sj_hovers"] = () => Presence.Others(R(), StartJobsDomain.Name, StartJobs.Serial(R())).Count;
        Autotest.Fields["sj_votes"] = () => R().State.SharedChoice.Box(StartJobsDomain.Name, StartJobs.Serial(R()))?.Votes.Count ?? 0;
        Autotest.Fields["sj_missing_strings"] = () => StartJobs.MissingStrings(R().T.StartJobs).Count;
    }
}

using Cosmoteer.Simulation;
using Halfling.Random;
using HotCargo.Core;

namespace HotCargo.Pursuit;

/// <summary>
/// Spec 920 to 926: the network outage. While it lasts every heat gain is blocked (Run.Hooks.HeatGainsBlocked); losses still apply.
/// Natural outages roll once per new system and go through the pacer. The hacker sells one more (spec 931), outside the natural limit.
/// </summary>
public static class Outage
{
    public static bool Active(Run run) => run.PressureActive && run.State.GameSeconds < run.State.Pursuit.OutageUntil;

    public static double SecondsLeft(Run run) => Active(run) ? run.State.Pursuit.OutageUntil - run.State.GameSeconds : 0;

    /// <summary>UI only: real time of the last blocked heat gain of 0.5 or more, for the "+0" on the heat panel. Not sim state.</summary>
    internal static double LastBlockedShownAt = double.MinValue;

    /// <summary>
    /// A seeded roll for this run and sector (galaxy seed, sector index, salt), the same on every machine and after a load.
    /// The pick of a new system happens once per sector, so the roll needs no save state.
    /// </summary>
    internal static FastRand SectorRand(Run run, ulong salt) =>
        new(unchecked((ulong)run.Mode.Init.RandSeed * 0x9E37_79B9_7F4A_7C15UL + (ulong)run.State.SectorIndex), salt);

    /// <summary>Spec 920: rolls the natural outage of a new system. Any system switch drops a roll that did not start.</summary>
    internal static void OnSystemEntered(Run run, bool firstVisit)
    {
        var s = run.State.Pursuit;
        var t = run.T.Pursuit;
        s.OutageDueAt = -1;
        if (!firstVisit || !t.Enabled || !PursuitModel.OutageSector(run.State.SectorIndex, run.T.Journey.RunSectorCount, t.OutageMinSector)) return;
        var rand = SectorRand(run, 0x4F55_5441_4745UL);
        if (rand.Single() >= t.OutageChance) return;
        s.OutageDueAt = run.State.GameSeconds + t.OutageDelayMin + rand.Single() * (t.OutageDelayMax - t.OutageDelayMin);
        Log.Info($"pursuit: natural outage rolled for {s.OutageDueAt:0}s");
    }

    internal static void Tick(Run run, SimRoot sim)
    {
        var s = run.State.Pursuit;
        if (s.OutageDueAt >= 0 && run.State.GameSeconds >= s.OutageDueAt)
        {
            s.OutageDueAt = -1;
            var verdict = Pacer.Verdict(run, PacedKinds.Outage);
            if (verdict == PaceVerdict.Ok && run.PressureActive) Start(run, run.T.Pursuit.OutageSeconds, natural: true);
            else Log.Info($"pursuit: natural outage skipped ({verdict})");
        }
        if (s.OutageUntil > 0 && run.State.GameSeconds >= s.OutageUntil)
        {
            s.OutageUntil = 0;
            Log.Info("pursuit: outage over");
            PursuitText.Center(run, PursuitText.Text("OutageOver"));
            PursuitText.Chat(run, PursuitText.Text("OutageOver"));
        }
    }

    /// <summary>
    /// Spec 921, 931: starts an outage of this length, or makes the current one last at least that long. Only natural outages count
    /// in the pacer. Sim logic.
    /// </summary>
    public static void Start(Run run, double seconds, bool natural)
    {
        var s = run.State.Pursuit;
        s.OutageUntil = Math.Max(s.OutageUntil, run.State.GameSeconds + seconds);
        if (natural) Pacer.Started(run, PacedKinds.Outage);
        Log.Info($"pursuit: outage for {seconds:0}s natural={natural}, until {s.OutageUntil:0}s");
        string text = PursuitText.Format("OutageStartFmt", PursuitModel.Clock(SecondsLeft(run)));
        PursuitText.Center(run, PursuitText.Blue(text));
        PursuitText.Chat(run, text);
    }

    /// <summary>Spec 922: a blocked gain. Discrete gains show "+0" on the heat panel; time and trail heat come in tiny steps and do not.</summary>
    internal static void OnGainBlocked(Run run, float amount)
    {
        if (amount >= 0.5f) LastBlockedShownAt = Halfling.App.Clock.Time.Seconds;
    }
}

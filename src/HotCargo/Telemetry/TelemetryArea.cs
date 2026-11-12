using System.Globalization;
using Halfling;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Telemetry;

/// <summary>
/// Entry point of the telemetry area (TELEMETRY.md). It records each game session to a JSONL file in the mod's telemetry folder.
/// It hooks in from outside: Run.Hooks, game EventHub events and read-only Harmony patches in this folder.
/// </summary>
public static class TelemetryArea
{
    static bool _frameHooked, _traceInstalled;

    /// <summary>A name for the sessions of this process, for example the autotest script. In the session.start header.</summary>
    internal static string Label = "";

    public static void Register()
    {
        Run.Hooks.NewRunStarted += _ => Session.NewRunPending = true;
        Run.Hooks.StageStarted += Session.OnStageStarted;
        Run.Hooks.StrikeWave += Session.OnStrikeWave;
        Run.Hooks.SystemEntered += Session.OnSystemEntered;
        Events.EventRunner.OutcomeChosen += Session.OnOutcomeChosen;
        Log.Written += OnLogWritten;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Session.End("process_exit");
        AddAutotest();
    }

    /// <summary>Runs once per session start, on the main thread or a game build thread. Hooks what needs a running game.</summary>
    internal static void OnSessionStarted(Run run)
    {
        if (!_traceInstalled)
        {
            _traceInstalled = true;
            Errors.Guard("telemetry trace", () => Trace.Install(new Harmony(Mod.HarmonyId + ".telemetry"), run.T.Telemetry.TraceMethods));
        }
        if (!_frameHooked)
        {
            _frameHooked = true;
            Run.OnNextFrame(() => App.Director.FrameEnded += Perf.OnFrameEnded);
        }
    }

    static void OnLogWritten(string level, string message)
    {
        if (!Tele.On(Cat.Log)) return;
        Tele.Emit(Cat.Log, "log", (level, message), static (w, a) =>
        {
            w.WriteString("level", a.level);
            w.WriteString("msg", a.message);
        });
    }

    /// <summary>
    /// Telemetry time on game threads in percent of the real time of the session. At a steady frame rate this is the share of the frame time
    /// (target: well below 1 %). Headless test games draw no frames, so their frame times are no measure.
    /// </summary>
    static double OverheadPct() =>
        Session.Current is { RealSeconds: > 0 } s ? Stats.Ms(Interlocked.Read(ref Stats.OverheadTicks)) * 100 / (s.RealSeconds * 1000) : 0;

    static void AddAutotest()
    {
        // telename <name>: labels the telemetry of this game process. scripts/autotest.sh sends the script name first.
        Autotest.Commands["telename"] = arg => Label = arg;
        SelfProfile.AddAutotest();
        Autotest.Fields["tele_active"] = () => Session.Current is null ? 0 : 1;
        Autotest.Fields["tele_events"] = () => Interlocked.Read(ref Stats.Events);
        Autotest.Fields["tele_dropped"] = () => Interlocked.Read(ref Stats.Dropped);
        Autotest.Fields["tele_nested_dropped"] = () => Interlocked.Read(ref Stats.NestedDropped);
        Autotest.Fields["tele_overhead_ms"] = () => Stats.Ms(Interlocked.Read(ref Stats.OverheadTicks));
        Autotest.Fields["tele_overhead_pct"] = OverheadPct;
        Autotest.Fields["tele_trace_missing"] = () => Stats.TraceMissing;
        Autotest.Fields["tele_trace_patched"] = () => Stats.TracePatched;
        // teleflush: waits until the writer wrote every queued event, so a check can read the file.
        Autotest.Commands["teleflush"] = _ => Autotest.WaitFor(() => TelemetryWriter.PendingCount == 0, 10, "telemetry flush", () => Autotest.Wait(1.5));
        // telesnap [why]: one snapshot now. Single player only (it reads the sim from the GUI frame).
        Autotest.Commands["telesnap"] = arg => (Session.Current ?? throw new InvalidOperationException("no telemetry session")).Snapshot(Autotest.Game!.Sim, arg.Length > 0 ? arg : "test");
        // telecount <event> <op> <n>: counts the events of that name in every file of the current run. Run teleflush first.
        Autotest.Commands["telecount"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) throw new InvalidOperationException("telecount needs: event op n");
            var s = Session.Current ?? throw new InvalidOperationException("no telemetry session");
            int count = CountEvents(Path.GetDirectoryName(s.FilePath)!, s.RunId, parts[0]);
            double n = double.Parse(parts[2], CultureInfo.InvariantCulture);
            bool ok = parts[1] switch { ">=" => count >= n, "<=" => count <= n, "==" => count == n, ">" => count > n, "<" => count < n, _ => throw new InvalidOperationException($"unknown op {parts[1]}") };
            Autotest.Check(ok, $"telemetry {parts[0]} count {parts[1]} {parts[2]} (actual {count})");
        };
        // damagehostiles <fraction>: sets every part of each hunter and hostile ship within 6000 of the fleet to this fraction of its health,
        // so the player's guns finish them. Test only, single player: it changes the sim from a GUI frame. It stands in for a won fight.
        Autotest.Commands["damagehostiles"] = arg =>
        {
            var run = Autotest.RequireRun();
            if (run.IsMultiplayer) throw new InvalidOperationException("damagehostiles is single player only");
            float fraction = float.Parse(arg, CultureInfo.InvariantCulture);
            var sim = run.Game.Sim;
            var center = SimQueries.FleetCenter(sim) ?? throw new InvalidOperationException("no player ship");
            int n = 0;
            foreach (var ship in sim.Ships.ToList())
            {
                if (Session.Side(ship) is not ("hunter" or "hostile") || ship.DetWorldCenter.DistanceTo(center) > 6000) continue;
                foreach (var part in ship.Parts.ToList()) part.Health = Math.Max(1, (int)(part.Rules.MaxHealth * fraction));
                n++;
            }
            Log.Info($"AUTOTEST damagehostiles {fraction}: {n} ships");
        };
        // telestats: logs the overhead numbers of the session, for reports.
        Autotest.Commands["telestats"] = _ =>
            Log.Info($"TELEMETRY STATS events={Interlocked.Read(ref Stats.Events)} bytes={Interlocked.Read(ref Stats.Bytes)} dropped={Interlocked.Read(ref Stats.Dropped)} " +
                     $"overhead_ms={Stats.Ms(Interlocked.Read(ref Stats.OverheadTicks)):0.0} frames={Perf.SessionFrames} frame_ms={Perf.SessionFrameMs:0} " +
                     $"overhead_pct_wall={OverheadPct():0.000} real_s={Session.Current?.RealSeconds:0.0} ticks={Perf.SessionTicks} tick_ms={Perf.SessionTickMs:0} " +
                     $"writer_ms={Stats.Ms(Interlocked.Read(ref Stats.WriterTicks)):0.0} trace_patched={Stats.TracePatched} trace_missing={Stats.TraceMissing} " +
                     $"missing=[{string.Join(", ", Trace.MissingMethods)}] file={Session.Current?.FilePath}");
    }

    /// <summary>Counts lines with "e":"name" in the files of a run: every session and part file.</summary>
    static int CountEvents(string dir, string runId, string name)
    {
        string needle = $"\"e\":\"{name}\"";
        int count = 0;
        foreach (string file in Directory.GetFiles(dir, runId + "__*.jsonl"))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line) if (line.Contains(needle, StringComparison.Ordinal)) count++;
        }
        return count;
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Cosmoteer.Simulation;
using Halfling;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Telemetry;

/// <summary>
/// Test-only profiler of Hot Cargo code (autotest command "selfprof on|reset|report"). It times every method that the game calls into Hot Cargo:
/// the Hot Cargo Harmony patch methods, the Hot Cargo overrides of game methods (AI modules, MP inputs, widgets, spawners) and the Hot Cargo
/// handlers of Run.Hooks, the game EventHub and the frame events of App.Director. The list comes from the code, not from a list here.
/// Times are inclusive. The totals count only the outermost Hot Cargo call of each thread, so nested calls count once.
/// It patches at "selfprof on" only, so normal play never pays for it. It reads state only.
/// Limit: the JIT can inline a tiny patch method into the Harmony replacement, and such a method then shows no calls.
/// Inlined methods are small by definition; revisit if a known busy patch shows no calls in a report.
/// </summary>
static class SelfProfile
{
    static Harmony? _harmony;
    static readonly Dictionary<IntPtr, int> Index = [];
    static MethodBase[] _methods = [];
    static long[] _calls = [], _ticks = [];
    [ThreadStatic] static int _depth;
    static int _mainThread;
    static volatile bool _inTick;
    /// <summary>Outermost Hot Cargo time: on the main thread inside a sim tick, on the main thread outside sim ticks, and on worker threads (parallel sim buckets).</summary>
    static long _tickTicks, _frameTicks, _workerTicks;
    static long _startedAt, _framesAtStart, _ticksAtStart;
    static double _tickMsAtStart;
    static int _failed;

    internal static void AddAutotest()
    {
        Dev.Autotest.Commands["selfprof"] = arg =>
        {
            string[] parts = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            switch (parts.FirstOrDefault())
            {
                case "on": Install(); Reset(); break;
                case "reset": Reset(); break;
                case "report": Report(parts.Length > 1 ? parts[1] : "-", 25); break;
                default: throw new InvalidOperationException("selfprof needs on, reset or report [label]");
            }
        };
        // Hot Cargo time per sim tick in percent of the sim tick time, since the last "selfprof reset". Parallel worker time counts in full.
        Dev.Autotest.Fields["selfprof_pct_tick"] = () => Summary().PctTick;
        Dev.Autotest.Fields["selfprof_ms_per_tick"] = () => Summary().OursPerTickMs;
    }

    static void Install()
    {
        if (_harmony is not null) return;
        _mainThread = Environment.CurrentManagedThreadId;
        _harmony = new Harmony(Mod.HarmonyId + ".selfprof");
        var targets = Targets();
        _methods = [.. targets];
        _calls = new long[_methods.Length];
        _ticks = new long[_methods.Length];
        for (int i = 0; i < _methods.Length; i++) Index[_methods[i].MethodHandle.Value] = i;
        var prefix = new HarmonyMethod(AccessTools.Method(typeof(SelfProfile), nameof(Prefix))) { priority = Priority.First };
        var finalizer = new HarmonyMethod(AccessTools.Method(typeof(SelfProfile), nameof(Finalizer))) { priority = Priority.Last };
        foreach (var m in _methods)
        {
            try { _harmony.Patch(m, prefix: prefix, finalizer: finalizer); }
            catch (Exception ex)
            {
                _failed++;
                Log.Info($"selfprof: cannot patch {Name(m)}: {ex.Message}");
            }
        }
        var fixedUpdate = AccessTools.Method(typeof(SimRoot), "FixedUpdate") ?? throw new InvalidOperationException("SimRoot.FixedUpdate not found");
        _harmony.Patch(fixedUpdate, prefix: new HarmonyMethod(typeof(SelfProfile), nameof(TickStart)), finalizer: new HarmonyMethod(typeof(SelfProfile), nameof(TickEnd)));
        Log.Info($"AUTOTEST selfprof on: {_methods.Length - _failed} methods timed, {_failed} failed");
    }

    /// <summary>Every Hot Cargo method that game code calls: patch methods, overrides of game methods, and event and hook handlers.</summary>
    static List<MethodBase> Targets()
    {
        var ours = typeof(SelfProfile).Assembly;
        var game = new[] { typeof(SimRoot).Assembly, typeof(App).Assembly };
        var set = new HashSet<MethodBase>();
        void Add(MethodBase m)
        {
            if (m.DeclaringType is not { } t || t.Assembly != ours || t == typeof(SelfProfile) || t.ContainsGenericParameters || m.ContainsGenericParameters) return;
            if (m.GetMethodBody() is null) return;
            set.Add(m);
        }
        foreach (var original in Harmony.GetAllPatchedMethods().ToList())
        {
            if (Harmony.GetPatchInfo(original) is not { } info) continue;
            foreach (var p in info.Prefixes.Concat(info.Postfixes).Concat(info.Finalizers))
                if (p.owner == Mod.HarmonyId) Add(p.PatchMethod);
        }
        foreach (var type in AccessTools.GetTypesFromAssembly(ours))
            foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.IsAbstract || m.IsSpecialName) continue;
                var baseType = m.GetBaseDefinition().DeclaringType;
                if (baseType is not null && baseType != type && game.Contains(baseType.Assembly)) Add(m);
            }
        void AddHandlers(object? owner, Type type, BindingFlags flags)
        {
            foreach (var field in type.GetFields(flags))
                if (field.GetValue(owner) is Delegate d)
                    foreach (var h in d.GetInvocationList()) Add(h.Method);
        }
        AddHandlers(null, typeof(Run.Hooks), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        AddHandlers(App.Director, App.Director.GetType(), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (Run.Current?.Game.EventHub is { } hub) AddHandlers(hub, hub.GetType(), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return [.. set.OrderBy(Name, StringComparer.Ordinal)];
    }

    static void Prefix(out long __state)
    {
        _depth++;
        __state = Stopwatch.GetTimestamp();
    }

    static void Finalizer(MethodBase __originalMethod, long __state)
    {
        long elapsed = Stopwatch.GetTimestamp() - __state;
        if (Index.TryGetValue(__originalMethod.MethodHandle.Value, out int i))
        {
            Interlocked.Increment(ref _calls[i]);
            Interlocked.Add(ref _ticks[i], elapsed);
        }
        if (--_depth > 0) return;
        if (Environment.CurrentManagedThreadId != _mainThread) Interlocked.Add(ref _workerTicks, elapsed);
        else if (_inTick) _tickTicks += elapsed;
        else _frameTicks += elapsed;
    }

    static void TickStart() => _inTick = true;
    static void TickEnd() => _inTick = false;

    static void Reset()
    {
        Array.Clear(_calls);
        Array.Clear(_ticks);
        _tickTicks = _frameTicks = _workerTicks = 0;
        _startedAt = Stopwatch.GetTimestamp();
        _framesAtStart = Perf.SessionFrames;
        _ticksAtStart = Perf.SessionTicks;
        _tickMsAtStart = Perf.SessionTickMs;
    }

    readonly record struct Totals(double WindowS, long Frames, long Ticks, double TickMs, double TickPartMs, double FramePartMs, double WorkerMs)
    {
        public double TickMeanMs => Ticks > 0 ? TickMs / Ticks : 0;
        public double OursPerTickMs => Ticks > 0 ? (TickPartMs + WorkerMs) / Ticks : 0;
        public double PctTick => TickMs > 0 ? (TickPartMs + WorkerMs) * 100 / TickMs : 0;
        public double OursPerFrameMs => Frames > 0 ? FramePartMs / Frames : 0;
    }

    static Totals Summary() => new(
        Stats.Ms(Stopwatch.GetTimestamp() - _startedAt), Perf.SessionFrames - _framesAtStart, Perf.SessionTicks - _ticksAtStart,
        Perf.SessionTickMs - _tickMsAtStart, Stats.Ms(_tickTicks), Stats.Ms(_frameTicks), Stats.Ms(Interlocked.Read(ref _workerTicks)));

    static void Report(string label, int top)
    {
        if (_harmony is null) throw new InvalidOperationException("selfprof is not on");
        var s = Summary();
        var inv = CultureInfo.InvariantCulture;
        Log.Info(string.Format(inv, "SELFPROF SUMMARY {0} window_s={1:0.0} ticks={2} tick_mean_ms={3:0.000} ours_ms_per_tick={4:0.000} ours_pct_tick={5:0.0} " +
            "ours_tick_main_ms={6:0.0} ours_worker_ms={7:0.0} frames={8} ours_ms_per_frame_outside_tick={9:0.000}",
            label, s.WindowS, s.Ticks, s.TickMeanMs, s.OursPerTickMs, s.PctTick, s.TickPartMs, s.WorkerMs, s.Frames, s.OursPerFrameMs));
        var rows = Enumerable.Range(0, _methods.Length).Where(i => _calls[i] > 0).OrderByDescending(i => _ticks[i]).Take(top);
        foreach (int i in rows)
        {
            double ms = Stats.Ms(_ticks[i]);
            Log.Info(string.Format(inv, "SELFPROF {0} {1} calls={2} ms={3:0.0} ms_per_tick={4:0.0000} us_per_call={5:0.0}",
                label, Name(_methods[i]), _calls[i], ms, s.Ticks > 0 ? ms / s.Ticks : 0, ms * 1000 / _calls[i]));
        }
    }

    static string Name(MethodBase m) => $"{m.DeclaringType?.FullName?.Replace("HotCargo.", "")}.{m.Name}";
}

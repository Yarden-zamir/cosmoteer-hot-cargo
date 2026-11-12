using System.Diagnostics;
using Cosmoteer.Simulation;
using HarmonyLib;

namespace HotCargo.Telemetry;

/// <summary>
/// Frame time, sim tick time, GC and telemetry overhead. Frames are timed between two FrameEnded events, sim ticks around
/// SimRoot.FixedUpdate. Every Telemetry.PerfSeconds real seconds one "perf" event sums the window.
/// </summary>
static class Perf
{
    /// <summary>Frames per window that keep their time for the percentiles. Sums and maxima count every frame.</summary>
    const int MaxFrames = 8192;
    static readonly double[] Frames = new double[MaxFrames];
    static int _frames, _frameCount, _slowFrames;
    static double _frameSum, _frameMax;
    static long _lastFrame, _windowStart;
    static double _tickMs, _tickMaxMs;
    static int _ticks;
    static long _overheadAtStart, _writerAtStart, _eventsAtStart, _bytesAtStart;
    static int _gen0, _gen1, _gen2;
    static TimeSpan _gcPause;

    /// <summary>Overhead and frame time of the whole session, for the autotest fields and the session end.</summary>
    internal static double SessionFrameMs, SessionTickMs;
    internal static long SessionFrames, SessionTicks;

    internal static void OnFrameEnded(object? sender, EventArgs e)
    {
        if (Session.Current is not { } s || !Tele.On(Cat.Perf)) { _lastFrame = 0; return; }
        long now = Stopwatch.GetTimestamp();
        if (_lastFrame != 0)
        {
            double ms = Stats.Ms(now - _lastFrame);
            if (_frames < MaxFrames) Frames[_frames++] = ms;
            _frameCount++;
            _frameSum += ms;
            if (ms > _frameMax) _frameMax = ms;
            if (ms > 33.4) _slowFrames++;
            SessionFrameMs += ms;
            SessionFrames++;
        }
        _lastFrame = now;
        if (_windowStart == 0) { StartWindow(now); return; }
        if (Stats.Ms(now - _windowStart) < s.Run.T.Telemetry.PerfSeconds * 1000) return;
        long t0 = Stats.Enter();
        try { Emit(now); }
        finally { Stats.Exit(t0); }
        StartWindow(Stopwatch.GetTimestamp());
    }

    static void StartWindow(long now)
    {
        _windowStart = now;
        _frames = _frameCount = _slowFrames = 0;
        _frameSum = _frameMax = 0;
        _ticks = 0;
        _tickMs = _tickMaxMs = 0;
        _overheadAtStart = Interlocked.Read(ref Stats.OverheadTicks);
        _writerAtStart = Interlocked.Read(ref Stats.WriterTicks);
        _eventsAtStart = Interlocked.Read(ref Stats.Events);
        _bytesAtStart = Interlocked.Read(ref Stats.Bytes);
        _gen0 = GC.CollectionCount(0);
        _gen1 = GC.CollectionCount(1);
        _gen2 = GC.CollectionCount(2);
        _gcPause = GC.GetTotalPauseDuration();
    }

    static void Emit(long now)
    {
        double windowMs = Stats.Ms(now - _windowStart);
        double[] sorted = Frames[.._frames];
        Array.Sort(sorted);
        int count = _frameCount, slow = _slowFrames;
        double sum = _frameSum, max = _frameMax;
        double P(double q) => sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(q * sorted.Length))];
        double overheadMs = Stats.Ms(Interlocked.Read(ref Stats.OverheadTicks) - _overheadAtStart);
        Tele.Emit(Cat.Perf, "perf", w =>
        {
            Tele.Num(w, "window_ms", windowMs);
            w.WriteNumber("frames", count);
            Tele.Num(w, "fps", count * 1000 / windowMs);
            Tele.Num(w, "frame_mean_ms", count > 0 ? sum / count : 0);
            Tele.Num(w, "frame_p50_ms", P(0.5));
            Tele.Num(w, "frame_p95_ms", P(0.95));
            Tele.Num(w, "frame_p99_ms", P(0.99));
            Tele.Num(w, "frame_max_ms", max);
            w.WriteNumber("frames_over_33ms", slow);
            w.WriteNumber("ticks", _ticks);
            Tele.Num(w, "tick_mean_ms", _ticks > 0 ? _tickMs / _ticks : 0);
            Tele.Num(w, "tick_max_ms", _tickMaxMs);
            Tele.Num(w, "tick_total_ms", _tickMs);
            Tele.Num(w, "tele_ms", overheadMs);
            Tele.Num(w, "tele_pct_wall", overheadMs * 100 / windowMs);
            Tele.Num(w, "tele_pct_tick", _tickMs > 0 ? overheadMs * 100 / _tickMs : 0);
            Tele.Num(w, "tele_writer_ms", Stats.Ms(Interlocked.Read(ref Stats.WriterTicks) - _writerAtStart));
            w.WriteNumber("tele_events", Interlocked.Read(ref Stats.Events) - _eventsAtStart);
            w.WriteNumber("tele_bytes", Interlocked.Read(ref Stats.Bytes) - _bytesAtStart);
            w.WriteNumber("tele_pending", TelemetryWriter.PendingCount);
            w.WriteNumber("tele_dropped", Interlocked.Read(ref Stats.Dropped));
            w.WriteNumber("gc0", GC.CollectionCount(0) - _gen0);
            w.WriteNumber("gc1", GC.CollectionCount(1) - _gen1);
            w.WriteNumber("gc2", GC.CollectionCount(2) - _gen2);
            Tele.Num(w, "gc_pause_ms", (GC.GetTotalPauseDuration() - _gcPause).TotalMilliseconds);
            w.WriteNumber("heap_mb", GC.GetTotalMemory(false) >> 20);
            w.WriteNumber("working_set_mb", Environment.WorkingSet >> 20);
        });
    }

    internal static void ResetSession()
    {
        _windowStart = _lastFrame = 0;
        SessionFrameMs = SessionTickMs = 0;
        SessionFrames = SessionTicks = 0;
    }

    [HarmonyPatch(typeof(SimRoot), "FixedUpdate")]
    static class TickTimer
    {
        static void Prefix(out long __state) => __state = Stopwatch.GetTimestamp();

        static void Postfix(SimRoot __instance, long __state)
        {
            if (Session.Current is not { } s || __instance != s.Game.Sim) return;
            double ms = Stats.Ms(Stopwatch.GetTimestamp() - __state);
            _tickMs += ms;
            _ticks++;
            if (ms > _tickMaxMs) _tickMaxMs = ms;
            SessionTickMs += ms;
            SessionTicks++;
        }
    }
}

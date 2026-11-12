using System.Buffers;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace HotCargo.Telemetry;

/// <summary>Event categories. Each has a switch of the same name in TelemetryTunables.</summary>
public enum Cat { Run, Heat, Tech, Economy, Missions, Story, Combat, Ai, Snapshots, Perf, Coop, Log, Trace }

/// <summary>
/// Builds telemetry events and hands them to the background writer. Thread safe: the sim, AI and part updates can run on worker threads.
/// Usage: <c>Tele.Emit(Cat.Heat, "heat.change", w => w.WriteNumber("amount", 3));</c>
/// Every event gets the common fields of the session context (see TELEMETRY.md). Nothing here changes game state.
/// </summary>
public static class Tele
{
    static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, SkipValidation = true };

    /// <summary>One bit per Cat. 0 while no session writes.</summary>
    static volatile int _mask;

    [ThreadStatic] static ArrayBufferWriter<byte>? _buffer;
    [ThreadStatic] static Utf8JsonWriter? _json;
    [ThreadStatic] static bool _building;
    [ThreadStatic] static long _startedAt;

    public static bool On(Cat c) => (_mask & (1 << (int)c)) != 0;

    internal static void SetMask(TelemetryTunables? t)
    {
        if (t is null || !t.Enabled) { _mask = 0; return; }
        int m = 0;
        void Set(Cat c, bool on) { if (on) m |= 1 << (int)c; }
        Set(Cat.Run, t.Run); Set(Cat.Heat, t.Heat); Set(Cat.Tech, t.Tech); Set(Cat.Economy, t.Economy); Set(Cat.Missions, t.Missions);
        Set(Cat.Story, t.Story); Set(Cat.Combat, t.Combat); Set(Cat.Ai, t.Ai); Set(Cat.Snapshots, t.Snapshots); Set(Cat.Perf, t.Perf);
        Set(Cat.Coop, t.Coop); Set(Cat.Log, t.Log); Set(Cat.Trace, t.Trace);
        _mask = m;
    }

    /// <summary>Writes one event. The fields callback adds the event's own fields. A failure reports once and drops the event.</summary>
    public static void Emit(Cat c, string name, Action<Utf8JsonWriter> fields)
    {
        if (Begin(c, name) is not { } w) return;
        bool ok = false;
        try { fields(w); ok = true; }
        catch (Exception ex) { Core.Errors.Report($"telemetry {name}", ex.Message, ex); }
        finally { if (ok) End(w); else Abort(); }
    }

    /// <summary>Emit with a state argument, so hot callers can pass a static lambda that allocates no closure.</summary>
    public static void Emit<TState>(Cat c, string name, TState state, Action<Utf8JsonWriter, TState> fields)
    {
        if (Begin(c, name) is not { } w) return;
        bool ok = false;
        try { fields(w, state); ok = true; }
        catch (Exception ex) { Core.Errors.Report($"telemetry {name}", ex.Message, ex); }
        finally { if (ok) End(w); else Abort(); }
    }

    static void Abort()
    {
        _building = false;
        Stats.Exit(_startedAt);
    }

    /// <summary>
    /// Starts an event with the common fields. Null when the category is off or no session writes.
    /// An event that another event starts while it is built (a log line from inside a getter) is dropped, so the buffers stay simple.
    /// </summary>
    static Utf8JsonWriter? Begin(Cat c, string name)
    {
        if (!On(c) || Session.Current is not { } s) return null;
        if (_building) { Interlocked.Increment(ref Stats.NestedDropped); return null; }
        _building = true;
        _startedAt = Stats.Enter();
        var buffer = _buffer ??= new ArrayBufferWriter<byte>(1024);
        buffer.ResetWrittenCount();
        var w = _json ??= new Utf8JsonWriter(buffer, Options);
        w.Reset(buffer);
        w.WriteStartObject();
        w.WriteString("e", name);
        Num(w, "t", s.RealSeconds);
        s.WriteContext(w);
        return w;
    }

    /// <summary>Ends the event that Begin started and queues it for the writer.</summary>
    static void End(Utf8JsonWriter w)
    {
        try
        {
            w.WriteEndObject();
            w.Flush();
            TelemetryWriter.Enqueue(_buffer!.WrittenSpan.ToArray());
        }
        finally
        {
            _building = false;
            Stats.Exit(_startedAt);
        }
    }

    /// <summary>Writes a number rounded to 3 decimals, so files stay small. NaN and infinity become null.</summary>
    public static void Num(Utf8JsonWriter w, string name, double v)
    {
        if (double.IsFinite(v)) w.WriteNumber(name, Math.Round(v, 3));
        else w.WriteNull(name);
    }

    /// <summary>A JSON object as a string, for the session header and the tunables snapshot.</summary>
    public static string Json(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);
        using (var w = new Utf8JsonWriter(buffer, Options)) body(w);
        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // ---- Context stack: what game code runs right now, set by the context patches (TelemetryPatches.Contexts).

    [ThreadStatic] static string[]? _context;
    [ThreadStatic] static int _depth;

    internal static void Push(string label)
    {
        var ctx = _context ??= new string[16];
        if (_depth < ctx.Length) ctx[_depth] = label;
        _depth++;
    }

    internal static void Pop() { if (_depth > 0) _depth--; }

    /// <summary>The innermost context label, or null outside any context.</summary>
    internal static string? Top => _depth > 0 && _depth <= _context!.Length ? _context[_depth - 1] : null;

    /// <summary>All context labels, outer first, joined with '>'. Empty outside any context.</summary>
    internal static string Path => _depth == 0 ? "" : string.Join('>', _context!.Take(Math.Min(_depth, _context.Length)));

    internal static bool InContext(string label)
    {
        for (int i = 0; i < Math.Min(_depth, _context?.Length ?? 0); i++) if (_context![i] == label) return true;
        return false;
    }
}

/// <summary>Counters for the perf event and the autotest fields. Updated with Interlocked from any thread.</summary>
public static class Stats
{
    /// <summary>Stopwatch ticks spent in telemetry code on game threads (not on the writer thread).</summary>
    public static long OverheadTicks;
    public static long Events;
    public static long Bytes;
    public static long Dropped;
    public static long NestedDropped;
    public static long WriterTicks;
    public static int PatchErrors;
    public static int TraceMissing;
    public static int TracePatched;

    [ThreadStatic] static int _measureDepth;

    /// <summary>Counters start over with each session, so session.end and perf report one session.</summary>
    public static void Reset()
    {
        Interlocked.Exchange(ref OverheadTicks, 0);
        Interlocked.Exchange(ref Events, 0);
        Interlocked.Exchange(ref Bytes, 0);
        Interlocked.Exchange(ref Dropped, 0);
        Interlocked.Exchange(ref NestedDropped, 0);
        Interlocked.Exchange(ref WriterTicks, 0);
    }

    /// <summary>Starts an overhead measurement. Nested measurements count once, in the outermost one.</summary>
    public static long Enter() { _measureDepth++; return Stopwatch.GetTimestamp(); }

    public static void Exit(long startedAt)
    {
        if (--_measureDepth == 0) Interlocked.Add(ref OverheadTicks, Stopwatch.GetTimestamp() - startedAt);
        if (_measureDepth < 0) _measureDepth = 0;
    }

    public static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}

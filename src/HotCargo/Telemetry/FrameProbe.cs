using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Gui;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Telemetry;

/// <summary>
/// Test only (autotest command "frameprof on|report [label]"). Times the Hot Cargo code that a drawing game runs in each frame and that
/// a headless game never runs: the per-frame handlers of Hot Cargo widgets, the Hot Cargo sim overlay draw handlers, the ship blip and
/// hover texts and the map node texts and tooltips. Each probe runs once per frame, so time throttles count as they do in play.
/// The text probes include the vanilla part, because the Hot Cargo postfixes run inside it. It reads state only.
/// Limit: a headless game has a null graphics device, so the draw calls of overlays cost less than on a real GPU.
/// </summary>
static class FrameProbe
{
    sealed class Probe
    {
        public long Ticks, Bytes, Frames;
        public int Items;
    }

    static readonly Dictionary<string, Probe> Probes = new(StringComparer.Ordinal);
    static readonly HashSet<string> Failed = new(StringComparer.Ordinal);
    static readonly List<(Widget Widget, EventHandler<EventArgs> Handler)> WidgetHandlers = [];
    static readonly FieldInfo OverlayEvent = AccessTools.Field(typeof(SimOverlayRenderer), nameof(SimOverlayRenderer.DrawingCustomOverlays))
        ?? throw new InvalidOperationException("SimOverlayRenderer.DrawingCustomOverlays field not found");
    static readonly StringBuilder MapText = new();
    static readonly Assembly Ours = typeof(FrameProbe).Assembly;
    static bool _hooked, _on;
    static long _frames;

    internal static void AddAutotest()
    {
        Dev.Autotest.Commands["frameprof"] = arg =>
        {
            string[] parts = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            switch (parts.FirstOrDefault())
            {
                case "on":
                    Probes.Clear();
                    _frames = 0;
                    _on = true;
                    if (!_hooked) App.Director.FrameEnded += (_, _) => OnFrame();
                    _hooked = true;
                    break;
                case "report":
                    _on = false;
                    Report(parts.Length > 1 ? parts[1] : "-");
                    break;
                default: throw new InvalidOperationException("frameprof needs on or report [label]");
            }
        };
        // Hot Cargo frame time per frame in microseconds, summed over every probe, since "frameprof on".
        Dev.Autotest.Fields["frameprof_us_per_frame"] = () => _frames == 0 ? 0 : Probes.Values.Sum(p => Stats.Ms(p.Ticks)) * 1000 / _frames;
    }

    static void OnFrame()
    {
        if (!_on || Run.Current is not { } run || run.Game.Sim is not { } sim) return;
        _frames++;
        var game = run.Game;

        WidgetHandlers.Clear();
        foreach (var w in game.Gui.RootWidget.GetChainedWidgets(recursive: true).Prepend(game.Gui.RootWidget)) CollectWidgetHandlers(w);
        foreach (var (widget, handler) in WidgetHandlers)
            Time("widget " + Name(handler.Method), 1, () => handler(widget, EventArgs.Empty));

        if (OverlayEvent.GetValue(sim.Overlays) is EventHandler<EventArgs> overlays)
            foreach (var d in overlays.GetInvocationList())
                if (d.Method.DeclaringType?.Assembly == Ours && d is EventHandler<EventArgs> h)
                    Time("overlay " + Name(h.Method), 1, () => h(sim.Overlays, EventArgs.Empty));

        var ships = sim.Ships.ToList();
        Time("ship blip texts", ships.Count, () => { foreach (var s in ships) s.IndicatorSource?.GetNonMouseoverText(); });
        Time("ship hover texts", ships.Count, () => { foreach (var s in ships) sim.Mode.GetShipMouseOverText(s, null); });

        var nodes = game.Map.Nodes.ToList();
        Time("map node texts", nodes.Count, () =>
        {
            foreach (var n in nodes)
            {
                MapText.Clear();
                game.Mode.GetMapNodeText(n, MapText);
            }
        });
        Time("map node tooltips", nodes.Count, () => { foreach (var n in nodes) game.Mode.GetMapNodeToolTip(n); });
    }

    static void CollectWidgetHandlers(Widget w)
    {
        if (w.e_beforeDraw is { } e)
            foreach (var d in e.GetInvocationList())
                if (d.Method.DeclaringType?.Assembly == Ours && d is EventHandler<EventArgs> h) WidgetHandlers.Add((w, h));
    }

    static void Time(string name, int items, Action body)
    {
        if (Failed.Contains(name)) return;
        if (!Probes.TryGetValue(name, out var p)) Probes[name] = p = new Probe();
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        try { body(); }
        catch (Exception ex)
        {
            Failed.Add(name);
            Log.Info($"frameprof: probe '{name}' fails and stops: {ex.GetType().Name}: {ex.Message}");
            return;
        }
        p.Ticks += Stopwatch.GetTimestamp() - start;
        p.Bytes += GC.GetAllocatedBytesForCurrentThread() - bytes;
        p.Frames++;
        p.Items = items;
    }

    static void Report(string label)
    {
        var inv = CultureInfo.InvariantCulture;
        double total = Probes.Values.Sum(p => Stats.Ms(p.Ticks)) * 1000 / Math.Max(1, _frames);
        long bytes = Probes.Values.Sum(p => p.Bytes) / Math.Max(1, _frames);
        Log.Info(string.Format(inv, "FRAMEPROF SUMMARY {0} frames={1} us_per_frame={2:0.0} bytes_per_frame={3}", label, _frames, total, bytes));
        foreach (var (name, p) in Probes.OrderByDescending(kv => kv.Value.Ticks))
            Log.Info(string.Format(inv, "FRAMEPROF {0} {1} items={2} us_per_frame={3:0.00} bytes_per_frame={4}",
                label, name.Replace(' ', '_'), p.Items, Stats.Ms(p.Ticks) * 1000 / Math.Max(1, p.Frames), p.Bytes / Math.Max(1, p.Frames)));
    }

    static string Name(MethodInfo m) => $"{m.DeclaringType?.FullName?.Replace("HotCargo.", "")}.{m.Name}";
}

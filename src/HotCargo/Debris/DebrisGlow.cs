using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Overlays;
using Halfling.Graphics;
using HotCargo.Core;
using HotCargo.Ux;

namespace HotCargo.Debris;

/// <summary>
/// Spec 1243: a junk piece in its glow time gets a pulsing orange outline on every part, brighter toward the burn-up. UI only: it reads
/// the run state and the sim, and never changes them. The vault outline (Ux/Markers.cs) works the same way.
/// </summary>
static class DebrisGlow
{
    static SimRoot? _sim;
    static BatchedPartNineSliceRenderer? _renderer;

    /// <summary>Hooks the overlay of the current sim. Called each frame; it hooks again after a system jump or a load.</summary>
    internal static void Update(Run run)
    {
        var sim = run.Game.Sim;
        if (sim == _sim) return;
        if (_sim is not null) _sim.Overlays.DrawingCustomOverlays -= OnDrawOverlays;
        _sim = sim;
        _renderer ??= new BatchedPartNineSliceRenderer(sim.Rules.SimGui.SelectedPartNineSlice);
        sim.Overlays.DrawingCustomOverlays += OnDrawOverlays;
    }

    static void OnDrawOverlays(object? sender, EventArgs e) => UxKit.Guard("debris glow", () =>
    {
        if (Run.Current is not { } run || run.Game.Sim != _sim || _renderer is null) return;
        var t = run.T.Debris;
        var since = run.State.Debris.Since;
        if (!t.Enabled || since.Count == 0) return;
        double now = run.State.GameSeconds;
        float level = 0f;
        int count = 0;
        // Only tracked pieces can glow, and they are few (MaxParts parts each), so a lookup for each is cheap.
        foreach (var (id, start) in since)
        {
            float l = DebrisModel.GlowLevel(now - start, t.DecaySeconds, t.GlowSeconds);
            if (l <= 0f || _sim.Ships[id] is not { } ship) continue;
            foreach (Part part in ship.Parts)
            {
                if (part.IsDestroyed) continue;
                _renderer.QueueNineSlice(part);
                count++;
            }
            level = Math.Max(level, l);
        }
        if (count == 0) return;
        // One color for the batch: the oldest glowing piece sets the brightness.
        float pulse = 0.55f + 0.45f * MathF.Sin((float)Halfling.App.Clock.Time.Seconds * (3f + 6f * level));
        _renderer.Draw(_sim, new Color(1f, 0.45f + 0.25f * (1f - level), 0.1f, (0.35f + 0.65f * level) * pulse));
    });
}

using Cosmoteer.Game;
using Cosmoteer.Gui;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using HotCargo.Core;
using HotCargo.Ux;
using System.Runtime.CompilerServices;
using Cosmoteer.Localization;

namespace HotCargo.Worlds;

/// <summary>
/// The heat zone of a sun (spec 223). The damage is vanilla: PlanetDoodad.ProximityEffects kills inside
/// InstantKillRadiusFactor × scale / 2 and burns out to EffectsRadiusFactor × scale / 2, stronger toward the sun.
/// This class only measures the depth, for the sundive objective and the HUD line.
/// </summary>
public static class HeatZone
{
    public readonly record struct Sun(Vector2 Center, float KillRadius, float EdgeRadius);

    /// <summary>Planets with a kill radius and a wider effects radius, that is suns.</summary>
    public static List<Sun> Suns(SimRoot sim)
    {
        var suns = new List<Sun>();
        foreach (var doodad in sim.Doodads)
        {
            if (doodad is not PlanetDoodad p || p.Rules.ProximityEffects is not { InstantKillRadiusFactor: { } kill, EffectsRadiusFactor: { } edge }) continue;
            if (edge <= kill) continue;
            suns.Add(new Sun(p._originLoc, kill * p._originScale / 2f, edge * p._originScale / 2f));
        }
        return suns;
    }

    /// <summary>Depth of a point in the deepest heat zone that covers it: 0 outside or at the edge, 1 at a kill radius.</summary>
    public static float Depth(IReadOnlyList<Sun> suns, Vector2 at)
    {
        float depth = 0f;
        foreach (var s in suns) depth = Math.Max(depth, WorldsModel.HeatDepth(at.DistanceTo(s.Center), s.KillRadius, s.EdgeRadius));
        return depth;
    }

    public static float Depth(SimRoot sim, Vector2 at) => Depth(Suns(sim), at);

    // ---- HUD line: the deepest local ship in a heat zone. GUI only.

    static readonly ConditionalWeakTable<GameRoot, Label> Lines = new();
    static readonly ConditionalWeakTable<SimRoot, List<Sun>> SunCache = new();

    static double _next;

    internal static void OnFrame(Run run)
    {
        var game = run.Game;
        if (!Lines.TryGetValue(game, out var label))
        {
            label = CreateLabel();
            game.Gui.RootWidget.AddChild(label);
            Lines.AddOrUpdate(game, label);
        }
        // Spec 872: the text refreshes every HudRefreshSeconds; the galaxy map still hides it at once.
        if (!UxKit.HudDue(ref _next))
        {
            if (game.MapDisplayFactor >= 1f) label.SelfActive = false;
            return;
        }
        var sim = game.Sim;
        // Suns do not move, so they are found once per sim.
        var suns = SunCache.GetValue(sim, Suns);
        string? text = null;
        if (suns.Count > 0 && game.MapDisplayFactor < 1f)
        {
            float deepest = 0f;
            string? name = null;
            foreach (var ship in sim.Ships.LocalPlayerShips)
            {
                float d = Depth(suns, ship.DetWorldCenter);
                if (d > deepest) { deepest = d; name = Halfling.StringTools.EscapeForXml(ship.Metadata.PlainShipName); }
            }
            if (name is not null)
            {
                string tag = deepest >= run.T.Worlds.HeatWarnDepth ? "bad" : "limited_color";
                text = $"<{tag}>{Strings.FormatText("HotCargoWorlds/HeatZoneFmt", name, deepest * 100f)}</{tag}>";
            }
        }
        label.SelfActive = text is not null;
        if (text is not null) label.Text = text;
    }

    static Label CreateLabel()
    {
        var label = new Label
        {
            AnchorRect = AnchorPresets.TopCenter,
            Y = 150f,
            InputBlockMode = InputBlockMode.Never,
        };
        label.TextRenderer.Font = WidgetRules.Instance.ShadowGlowFont;
        label.TextRenderer.FontSize = 18;
        label.TextRenderer.XmlFormatting = true;
        label.TextRenderer.HAlignment = HAlignment.Center;
        label.TextRenderer.VAlignment = VAlignment.Top;
        label.TextRenderer.OversizeMode = OversizeMode.None;
        label.TextRenderer.Clip = false;
        label.AutoSize.AutoWidthMode = AutoSizeMode.Enable;
        label.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        label.AutoSize.Anchor = new Vector2(0.5f, 0f);
        label.SelfActive = false;
        return label;
    }
}

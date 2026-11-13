using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Localization;
using Halfling;
using Halfling.Events;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Text;
using Cosmoteer.Gui;
using HotCargo.Core;

namespace HotCargo.Ux;

/// <summary>Small shared helpers for the UX area: switches, strings, the chat log.</summary>
public static class UxKit
{
    static readonly UxTunables Defaults = new();
    static bool _modEnabled;
    static double _nextEnabledCheck = double.MinValue;

    /// <summary>
    /// The UX switches in effect, or null when Hot Cargo is not enabled (spec 63: the game stays vanilla).
    /// Limit: outside a Hot Cargo run (title screen, creative mode) the switches are the defaults, not tunables.rules.
    /// Revisit if a player needs to turn a vanilla change off outside career mode.
    /// </summary>
    public static UxTunables? Switches
    {
        get
        {
            if (Run.Current is { } run) return run.T.Ux;
            double now = App.Clock.Time.Seconds;
            if (now >= _nextEnabledCheck)
            {
                _modEnabled = Activation.IsModEnabled();
                _nextEnabledCheck = now + 5;
            }
            return _modEnabled ? Defaults : null;
        }
    }

    public static bool On(Func<UxTunables, bool> pick) => Switches is { } s && pick(s);

    /// <summary>Text from mod/ux/strings.en.rules (section HotCargoUx).</summary>
    public static string Text(string key) => Strings.GetText("HotCargoUx/" + key);

    public static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoUx/" + key, args);

    /// <summary>Adds a line to the chat log (single player and multiplayer). Local only, so it is safe in multiplayer.</summary>
    public static void ChatLine(GameRoot game, string xml)
    {
        if (game.NetManager is IChatBoxProvider p) p.ChatBox.DisplayRawMessage(xml);
    }

    /// <summary>
    /// A center screen text in the style of the game's area name (GameGui.ShowAreaName: same font, size, place and fades),
    /// with XML formatting, and wrapped to the screen width. The vanilla label never wraps, so a long line runs off both sides.
    /// </summary>
    public static async void CenterText(GameRoot game, string xml)
    {
        // async void: an exception here would end the game, so it reports instead.
        try { await ShowCenterText(game, xml); }
        catch (Exception ex) { Errors.Report("ux center text", ex.Message, ex); }
    }

    /// <summary>Center texts on screen now. A new one goes below them, so two alerts at once do not draw over each other.</summary>
    static readonly List<Label> CenterTexts = [];

    static async Task ShowCenterText(GameRoot game, string xml)
    {
        var root = game.Gui.RootWidget;
        float w = Math.Min(1100f, root.ScreenRect.Width - 80f);
        CenterTexts.RemoveAll(l => l.Root != game.Gui);
        var lbl = new Label
        {
            AnchorRect = AnchorPresets.TopCenter,
            Left = -w / 2f,
            Right = w / 2f,
            Y = 100f,
            Text = xml,
            InputBlockMode = InputBlockMode.Never,
        };
        lbl.TextRenderer.Font = WidgetRules.Instance.ShadowFont;
        lbl.TextRenderer.FontSize = 48;
        lbl.TextRenderer.XmlFormatting = true;
        lbl.TextRenderer.HAlignment = HAlignment.Center;
        lbl.TextRenderer.VAlignment = VAlignment.Top;
        lbl.TextRenderer.OversizeMode = OversizeMode.Wrap;
        lbl.TextRenderer.Clip = false;
        lbl.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        // Placed each frame below the older texts: a text has its height only after its first layout.
        lbl.BeforeFrameDraw += (_, _) =>
        {
            float y = 100f;
            foreach (var l in CenterTexts)
            {
                if (l == lbl) break;
                y += l.Height + 8f;
            }
            if (Math.Abs(lbl.Y - y) > 0.5f) lbl.Y = y;
        };
        root.AddChild(lbl);
        CenterTexts.Add(lbl);
        try
        {
            await lbl.FadeIn(1f);
            await App.Clock.UntilElapsed(2f);
            await lbl.FadeOut(1f);
        }
        finally
        {
            CenterTexts.Remove(lbl);
            root.Children.Remove(lbl);
        }
    }

    static readonly Dictionary<(Cosmoteer.GalaxyMap.MapNode From, Cosmoteer.GalaxyMap.MapNode To), int> JumpCache = new();

    /// <summary>
    /// Jumps on the shortest known jumpable route from the current system, or -1 without a route. Cached per route: the map
    /// changes only when a system becomes known, and a new current system makes a new key.
    /// Limit: a newly known route can shorten a cached path; the cache clears at 256 routes. Revisit if a stale count shows.
    /// </summary>
    public static int Jumps(GameRoot game, Cosmoteer.GalaxyMap.MapNode to)
    {
        var key = (game.CurrentMapNode, to);
        if (JumpCache.TryGetValue(key, out int cached)) return cached;
        if (JumpCache.Count >= 256) JumpCache.Clear();
        using var path = Halfling.Pooling.TempList<Cosmoteer.GalaxyMap.MapNode>.Alloc();
        float dist = game.Map.Nodes.FindPathTo(to, path, requireJumpable: true);
        return JumpCache[key] = float.IsInfinity(dist) ? -1 : path.Count - 1;
    }

    /// <summary>Runs a UI patch body. A failure logs once, shows in the overlay, and the vanilla UI stays.</summary>
    public static void Guard(string what, Action body) => Errors.Guard("ux " + what, body);

    /// <summary>Per-stage text "HotCargoUx/Stage{Name}". A stage that the strings do not know gets a generic line.</summary>
    public static string StageText(string stageName) =>
        Strings.TryGetText("HotCargoUx/Stage" + stageName, out string? text) ? text : Text("StageGeneric");

    /// <summary>Percent text in the game's good, limited or bad text color.</summary>
    public static string PercentXml(float fraction)
    {
        string tag = fraction >= 0.75f ? "good" : fraction >= 0.4f ? "limited_color" : "bad";
        return $"<{tag}>{fraction * 100f:0}%</{tag}>";
    }
}

/// <summary>A tooltip text that is computed each time the game reads it.</summary>
public sealed class LiveText(Func<string> text) : ITextProvider
{
    public string Text => text();

    // The value is computed on read, so there is never a change to announce.
    public event EventHandler<ValueChangedEventArgs<string>>? TextChanged { add { } remove { } }
}

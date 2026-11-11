using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Gui;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Cosmoteer.Game.Gui;

namespace HotCargo.Ux;

/// <summary>
/// V2. Vanilla removes the icon of a lost ship without a word, and shows "no control" only for one selected ship.
/// This shows a red banner, plays the error sound and logs a chat line when one of your ships is destroyed,
/// becomes derelict, or loses control. GUI only.
/// </summary>
static class ShipAlerts
{
    enum Kind { Destroyed, Derelict, NoControl }

    /// <summary>Local ships of the last poll and whether they could execute commands. Cleared when the sim changes.</summary>
    static readonly Dictionary<Ship, bool> Tracked = [];
    static readonly HashSet<Ship> Seen = [];
    static SimRoot? _polledSim;
    static double _nextPoll;
    static int _bannerCount;

    internal static void OnFrame()
    {
        var game = App.Director.States.OfType<GameRoot>().FirstOrDefault();
        if (game?.Sim is not { } sim) return;
        bool on = UxKit.On(s => s.ShipLossAlerts);
        double now = App.Clock.Time.Seconds;
        if (now < _nextPoll) return;
        _nextPoll = now + 0.25;
        // A system jump or a load replaces the sim. Start again without alerts, so a jump does not read as a loss.
        bool fresh = sim != _polledSim;
        if (fresh) { Tracked.Clear(); _polledSim = sim; }
        Seen.Clear();
        foreach (var ship in sim.Ships.LocalPlayerShips)
        {
            bool can = ship.Commands.CanExecuteCommands;
            if (Tracked.TryGetValue(ship, out bool could) && could && !can && on)
                Show(game, Kind.NoControl, ship.Metadata.PlainShipName, ship);
            Tracked[ship] = can;
            Seen.Add(ship);
        }
        // A ship that left the local list in the same sim was destroyed (disposed or no parts left) or became derelict (junk).
        // A local jump keeps the parts and the owner, and a transfer to another player keeps the parts, so neither alerts.
        foreach (var ship in Tracked.Keys.Where(s => !Seen.Contains(s)).ToList())
        {
            Tracked.Remove(ship);
            if (!on) continue;
            if (ship.IsDisposed || ship.Parts.Count == 0) Show(game, Kind.Destroyed, ship.Metadata.PlainShipName, null);
            else if (ship.IsJunk) Show(game, Kind.Derelict, ship.Metadata.PlainShipName, null);
        }
    }

    /// <summary>Alerts shown since start. For autotests.</summary>
    internal static int Shown;
    internal static int TrackedCount => Tracked.Count;

    static void Show(GameRoot game, Kind kind, string name, Ship? ship)
    {
        Shown++;
        Log.Info($"ux: ship alert {kind} {name}");
        string text = kind switch
        {
            Kind.Destroyed => UxKit.Format("ShipLostFmt", name),
            Kind.Derelict => UxKit.Format("ShipDerelictFmt", name),
            _ => UxKit.Format("ShipNoControlFmt", name, ship?.Commands.CommandPointsUsed ?? 0, ship?.Commands.CommandPointsProvided ?? 0),
        };
        UxKit.ChatLine(game, $"<bad>{Halfling.StringTools.EscapeForXml(text)}</bad>");
        game.Gui.PlayErrorSound();
        Banner(game, text, WidgetRules.Instance.BadTextColor);
    }

    /// <summary>A red banner under the area name position. Banners that come together stack instead of overlapping.</summary>
    internal static async void Banner(GameRoot game, string text, Halfling.Graphics.Color color)
    {
        var lbl = new Label
        {
            AnchorRect = AnchorPresets.TopCenter,
            Y = 165f + 40f * (_bannerCount++ % 4),
            Text = text,
            InputBlockMode = InputBlockMode.Never,
        };
        lbl.TextRenderer.Font = WidgetRules.Instance.ShadowFont;
        lbl.TextRenderer.FontSize = 30;
        lbl.TextRenderer.Color = color;
        lbl.TextRenderer.HAlignment = HAlignment.Center;
        lbl.TextRenderer.VAlignment = VAlignment.Top;
        lbl.TextRenderer.OversizeMode = OversizeMode.None;
        lbl.TextRenderer.Clip = false;
        lbl.AutoSize.AutoWidthMode = AutoSizeMode.Enable;
        lbl.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        lbl.AutoSize.Anchor = new Vector2(0.5f, 0f);
        game.Gui.RootWidget.AddChild(lbl);
        try
        {
            await lbl.FadeIn(0.3f);
            await App.Clock.UntilElapsed(4f);
            await lbl.FadeOut(1f);
        }
        finally
        {
            game.Gui.RootWidget.Children.Remove(lbl);
            _bannerCount = Math.Max(0, _bannerCount - 1);
        }
    }
}

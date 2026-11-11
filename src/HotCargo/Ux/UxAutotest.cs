using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Data;
using Cosmoteer.Resources;
using Halfling;
using HotCargo.Dev;
using HotCargo.Tech;

namespace HotCargo.Ux;

/// <summary>Autotest commands and fields for the batch 2 UX items (UX.md V7 to V15, H8). Test aids only.</summary>
static class UxAutotest
{
    internal static void Register()
    {
        // uxpick N: presses dialogue key N (1-based) in the frontmost dialogue (event window first, then comms).
        Autotest.Commands["uxpick"] = arg =>
        {
            int n = int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
            Autotest.Check(DialogueKeys.Pick(n - 1), $"dialogue key {n} picked an option");
        };
        // supplies: logs the watched supplies and the player ship's fraction of each.
        Autotest.Commands["supplies"] = _ =>
        {
            var ship = TechAutotest.PlayerShip();
            var parts = LowSupplies.Watched(ship.Rules).Select(id =>
                $"{id}={ship.Resources.GetResourceTotal(id)}/{ship.Resources.GetCapacityTotal(id, includeImplicit: true, includeUntyped: false)}");
            Log.Info($"ux: supplies {string.Join(" ", parts)}");
        };
        // wanted: test only. Makes the nearest station's faction want the player, as an attack on it would.
        Autotest.Commands["wanted"] = _ =>
        {
            var run = Autotest.RequireRun();
            run.Mode.SimMode.Wanted.WarTriggeredByPlayer(TechAutotest.NearestStation());
        };
        // selectparts: selects every part of the player ship, so the parts card shows the several-parts info.
        Autotest.Commands["selectparts"] = _ =>
        {
            var ship = TechAutotest.PlayerShip();
            ship.Sim.PlayerInput.SelectParts(ship.Parts.ToList());
        };
        Autotest.Fields["ux_parts_info"] = () => MultiPartInfo.Shown;
        // supplycheck: runs the low supply check now, without the one-second wait.
        Autotest.Commands["supplycheck"] = _ =>
        {
            var run = Autotest.RequireRun();
            LowSupplies.Poll(run.Game, run.Game.Sim, run.T.Ux);
        };
        // commstab trade|dialogue: selects a tab in the open comms window with the nearest station (the tab builds on first show).
        Autotest.Commands["commstab"] = arg =>
        {
            if (!CommWindow.s_openWindows.TryGetValue(TechAutotest.NearestStation(), out var window))
                throw new InvalidOperationException("no comms window with the nearest station");
            var tab = arg switch
            {
                "trade" => window._tradeTab,
                "dialogue" => window._dialogueTab,
                _ => throw new InvalidOperationException("commstab needs: trade | dialogue"),
            } ?? throw new InvalidOperationException($"the station has no {arg} tab");
            window._tabs.TabsListBox.SelectedWidget = tab;
        };
        Autotest.Commands["tradehint"] = _ => Log.Info($"ux: trade hint\n{TradeText()}");
        Autotest.Commands["wantedline"] = _ => Log.Info($"ux: wanted line: {WantedTimer.Text(Autotest.RequireRun().Mode)}");

        // Layout checks (the null graphics device of a headless game still lays out the GUI).
        Autotest.Fields["ux_win_centered"] = () => Centered(Ui.CenterPanel.Newest ?? throw new InvalidOperationException("no center panel is open"), "win panel");
        Autotest.Fields["ux_loss_centered"] = () => Centered(
            CareerInfoPatches.GameOver.LossPanel?.TryGetTarget(out var p) == true ? p : throw new InvalidOperationException("no loss panel"), "loss panel");
        Autotest.Fields["ux_trade_overlap"] = TradeOverlaps;
        Autotest.Fields["ux_event_text_numbers"] = () => DialogueKeys.EventTextNumbers();
        Autotest.Fields["ux_parts_xml"] = () => MultiPartInfo.HealthLabel?.TextRenderer.XmlFormatting == true ? 1 : 0;
        Autotest.Fields["ux_damage_hits"] = () => DamagePings.Hits;
        Autotest.Fields["ux_supply_alerts"] = () => LowSupplies.AlertsShown;
        Autotest.Fields["ux_dialogue_picks"] = () => DialogueKeys.Picks;
        Autotest.Fields["ux_trade_hint"] = () => TradeText().Length;
        Autotest.Fields["ux_wanted_line"] = () => WantedTimer.Text(Autotest.RequireRun().Mode).Length;
        Autotest.Fields["ux_comms_open"] = () => CommWindow.IsCommWindowOpen(Autotest.RequireRun().Game) ? 1 : 0;
        Autotest.Fields["ux_watched_battery"] = () => LowSupplies.Watched(TechAutotest.PlayerShip().Rules).Contains(new ID<ResourceRules>("battery")) ? 1 : 0;
        Autotest.Fields["ux_watched_carbon"] = () => LowSupplies.Watched(TechAutotest.PlayerShip().Rules).Contains(new ID<ResourceRules>("carbon")) ? 1 : 0;
    }

    /// <summary>1 when the panel's center is on the screen center (within 2 units) and the panel is inside the screen.</summary>
    static double Centered(Halfling.Gui.Widget panel, string what)
    {
        var r = panel.ScreenRect;
        var screen = panel.Root!.RootWidget.ScreenRect;
        Log.Info($"ux: {what} rect {r} screen {screen}");
        bool inside = r.Left >= screen.Left && r.Right <= screen.Right && r.Top >= screen.Top && r.Bottom <= screen.Bottom;
        return inside && Math.Abs(r.Center.X - screen.Center.X) < 2f && r.Width > 0 ? 1 : 0;
    }

    /// <summary>The number of trade page widgets that the hint box overlaps. Fails when the box has no height.</summary>
    static double TradeOverlaps()
    {
        if (TradeHints.LastBox?.TryGetTarget(out var box) != true || box.Parent?.Widget is not Halfling.Gui.ScrollBox page)
            throw new InvalidOperationException("no trade hint box");
        var b = box.ScreenRect;
        Log.Info($"ux: trade hint box {b}");
        if (!box.IsActive || b.Height < 10f) throw new InvalidOperationException($"the trade hint box is not shown or has no height: {b}");
        int n = 0;
        foreach (var w in page.Children.Concat(page.DockedChildren.Where(d => d != box)))
        {
            var r = w.ScreenRect;
            if (w.IsActive && r.Width > 0 && r.Height > 0 && b.Left < r.Right && r.Left < b.Right && b.Top < r.Bottom && r.Top < b.Bottom)
            {
                Log.Info($"ux: trade hint box overlaps {w.GetType().Name} {r}");
                n++;
            }
        }
        return n;
    }

    static string TradeText() =>
        TradeHints.Last?.TryGetTarget(out var tab) == true ? TradeHints.Text(tab) : throw new InvalidOperationException("no trade tab was opened");
}

using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Data;
using Cosmoteer.Resources;
using Halfling;
using Halfling.Geometry;
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
            var gui = Autotest.RequireRun().Game.Gui.ShipGui;
            Log.Info($"ux: selected {ship.Sim.PlayerInput.SelectedParts.Count} parts; ship gui active={gui.SelfActive} parts card={gui.PartsCard.SelfActive}");
        };
        // partsstate: logs the part selection and the parts card state.
        Autotest.Commands["partsstate"] = _ =>
        {
            var game = Autotest.RequireRun().Game;
            var gui = game.Gui.ShipGui;
            Log.Info($"ux: selected parts {game.Sim.PlayerInput.SelectedParts.Count}, ships {game.Sim.PlayerInput.SelectedShips.Count}; ship gui={gui.SelfActive} parts card={gui.PartsCard.SelfActive} active={gui.PartsCard.IsActive} map={game.MapDisplayFactor}");
        };
        Autotest.Fields["ux_parts_info"] = () => MultiPartInfo.Shown;
        // supplycheck: runs the low supply check now, without the one-second wait.
        Autotest.Commands["supplycheck"] = _ =>
        {
            var run = Autotest.RequireRun();
            LowSupplies.Poll(run.Game, run.Game.Sim, run.T.Ux);
        };
        // commstab trade|dialogue: selects a tab in the comms window that comms open opened (the tab builds on first show).
        Autotest.Commands["commstab"] = arg =>
        {
            var window = UxArea.OpenComms;
            var tab = arg switch
            {
                "trade" => window._tradeTab,
                "dialogue" => window._dialogueTab,
                _ => throw new InvalidOperationException("commstab needs: trade | dialogue"),
            } ?? throw new InvalidOperationException($"the station has no {arg} tab");
            window._tabs.TabsListBox.SelectedWidget = tab;
        };
        // commsmission: shows the Missions tab of the open comms window and selects the offer that contract_pick chose
        // (or the first offer), so a screenshot shows its info panel with the contract terms. Test only.
        Autotest.Commands["commsmission"] = _ =>
        {
            var window = UxArea.OpenComms;
            window._tabs.TabsListBox.SelectedWidget = window._missionsTab ?? throw new InvalidOperationException("the station has no missions tab");
        };
        // commsselect [text]: selects the offer whose name contains text, else the offer that contract_pick chose, else the first.
        Autotest.Commands["commsselect"] = arg => SelectMission(UxArea.OpenComms, arg, Contracts.ContractsArea.PickedForTests);
        // missionlog open|close: opens or closes the career log window, as the HUD button does.
        Autotest.Commands["missionlog"] = arg =>
        {
            var mode = Autotest.RequireRun().Mode;
            bool open = mode._missionLog is { SelfActive: true, Parent: not null };
            if ((arg == "open") != open) mode.OnMissionLogClicked(null, EventArgs.Empty);
        };
        // logselect [text]: selects the mission in the open mission log whose name contains text, else the first.
        Autotest.Commands["logselect"] = arg =>
            SelectMission(Autotest.RequireRun().Mode._missionLog ?? throw new InvalidOperationException("no mission log; run missionlog open first"), arg, null);
        // tipshow map|mapcur|station|fame|text <xml>: shows that tooltip at a fixed spot, for screenshots. The capture hides hover
        // tooltips, because the real cursor rests somewhere over the hidden window. tipclear removes it.
        Autotest.Commands["tipshow"] = arg =>
        {
            var run = Autotest.RequireRun();
            var parts = arg.Split(' ', 2);
            string text = parts[0] switch
            {
                "map" => run.Mode.GetMapNodeToolTip(NearestOtherNode(run)) ?? "",
                "mapcur" => run.Mode.GetMapNodeToolTip(run.Game.CurrentMapNode) ?? "",
                "station" => run.Game.Sim.Mode.GetShipMouseOverText(TechAutotest.NearestStation()),
                "fame" => Walk(run.Game.Gui.RootWidget).OfType<Cosmoteer.Game.Gui.PlayerFameLabel>().First().ToolTips.TextProvider?.Text ?? "",
                "text" when parts.Length == 2 => parts[1],
                _ => throw new InvalidOperationException("tipshow needs: map | mapcur | station | fame | text <xml>"),
            };
            TipClear(run.Game.Gui);
            var screen = run.Game.Gui.RootWidget.ScreenRect;
            _tip = new Cosmoteer.Gui.ToolTips.MultiToolTipBox(run.Game.Gui.ToolTipRight);
            run.Game.Gui.AddDropTarget(_tip);
            _tip.SetToolTipSource(new FixedTip(text, new Rect(screen.Width * 0.55f, screen.Height * 0.3f, 1f, 1f)));
            Log.Info($"ux: tooltip {parts[0]}: {text}");
        };
        Autotest.Commands["tipclear"] = _ => TipClear(Autotest.RequireRun().Game.Gui);
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
        Autotest.Fields["ux_event_open"] = () => Events.EventDialog.IsShown ? 1 : 0;
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

    static Cosmoteer.Gui.ToolTips.MultiToolTipBox? _tip;

    static void TipClear(Cosmoteer.Game.Gui.GameGui gui)
    {
        if (_tip is not null) gui.RemoveDropTarget(_tip);
        _tip = null;
    }

    sealed class FixedTip(string xml, Rect at) : Halfling.Gui.Components.ToolTips.IToolTipSource
    {
        public Halfling.Gui.Components.Text.ITextProvider TextProvider { get; } = new Halfling.Gui.Components.Text.SimpleTextProvider(xml, null);
        public Halfling.Gui.Components.Rects.IRectProvider RectProvider { get; } = new Halfling.Gui.Components.Rects.SimpleRectProvider(at, null);
        public Halfling.Gui.Edges BoundingEdges => Halfling.Gui.Edges.None;
        public void OnAboutToBeDisplayed() { }
    }

    /// <summary>The map node nearest to the current one, for a tooltip of a system that the players have not visited.</summary>
    static Cosmoteer.GalaxyMap.MapNode NearestOtherNode(Core.Run run)
    {
        var here = run.Game.CurrentMapNode;
        return run.Game.Map.Nodes.Where(n => n != here).MinBy(n => n.Location.DistanceSquaredTo(here.Location))
            ?? throw new InvalidOperationException("the map has one node");
    }

    static void SelectMission(Halfling.Gui.Widget window, string text, Cosmoteer.Modes.Career.Missions.Mission? fallback)
    {
        var panel = Walk(window).OfType<Cosmoteer.Modes.Career.Missions.MissionsPanel>().FirstOrDefault()
            ?? throw new InvalidOperationException("no missions panel in the window");
        var item = (text.Length > 0 ? panel.FirstOrDefault(i => i.Mission.Name.Text.Contains(text, StringComparison.OrdinalIgnoreCase)) : null)
            ?? panel.FirstOrDefault(i => i.Mission == fallback) ?? panel.FirstOrDefault()
            ?? throw new InvalidOperationException("the missions panel lists no missions");
        item.IsSelected = true;
        Log.Info($"ux: selected mission {item.Mission.Name.Text}");
    }

    static IEnumerable<Halfling.Gui.Widget> Walk(Halfling.Gui.Widget w) => w.GetChainedWidgets(recursive: true).Prepend(w);
}

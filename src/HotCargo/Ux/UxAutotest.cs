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

        Autotest.Fields["ux_damage_hits"] = () => DamagePings.Hits;
        Autotest.Fields["ux_supply_alerts"] = () => LowSupplies.AlertsShown;
        Autotest.Fields["ux_dialogue_picks"] = () => DialogueKeys.Picks;
        Autotest.Fields["ux_trade_hint"] = () => TradeText().Length;
        Autotest.Fields["ux_wanted_line"] = () => WantedTimer.Text(Autotest.RequireRun().Mode).Length;
        Autotest.Fields["ux_comms_open"] = () => CommWindow.IsCommWindowOpen(Autotest.RequireRun().Game) ? 1 : 0;
        Autotest.Fields["ux_watched_battery"] = () => LowSupplies.Watched(TechAutotest.PlayerShip().Rules).Contains(new ID<ResourceRules>("battery")) ? 1 : 0;
        Autotest.Fields["ux_watched_carbon"] = () => LowSupplies.Watched(TechAutotest.PlayerShip().Rules).Contains(new ID<ResourceRules>("carbon")) ? 1 : 0;
    }

    static string TradeText() =>
        TradeHints.Last?.TryGetTarget(out var tab) == true ? TradeHints.Text(tab) : throw new InvalidOperationException("no trade tab was opened");
}

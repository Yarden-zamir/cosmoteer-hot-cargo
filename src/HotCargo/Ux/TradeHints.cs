using Cosmoteer;
using Cosmoteer.Factions;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Cosmoteer.Ships;
using Halfling;
using Halfling.Graphics.Text;
using Halfling.Gui;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Journey;
using HotCargo.Tech;

namespace HotCargo.Ux;

/// <summary>
/// A hint block at the top of the comms trade tab:
/// - V13 (TradeHints): the price factors of your standing with the station's faction, and the station's delivery missions
///   (accepted or offered) with progress and money reward. A sale counts toward a delivery mission.
/// - H8 (TechSaleHint): what selling tech here does to the heat rate and the final score, or that a sale here wins the run.
/// GUI only. It refreshes twice a second.
/// </summary>
[HarmonyPatch(typeof(CommTradeTab), MethodType.Constructor, typeof(ScrollBox), typeof(GameRoot), typeof(Ship), typeof(Ship))]
static class TradeHints
{
    /// <summary>The newest trade tab. For autotests.</summary>
    internal static WeakReference<CommTradeTab>? Last;

    /// <summary>The hint box of the newest trade tab. For autotests that check the layout.</summary>
    internal static WeakReference<LayoutBox>? LastBox;

    /// <summary>
    /// The hints sit in a box docked at the top of the trade page, outside its scrolled rows, so they take their own space.
    /// A label inside the scrolled rows measured one line and drew its wrapped lines over the rows below.
    /// </summary>
    static void Postfix(CommTradeTab __instance, ScrollBox pageBox) => UxKit.Guard("trade hints", () =>
    {
        Last = new WeakReference<CommTradeTab>(__instance);
        var box = new LayoutBox { DockMode = DockMode.Top, InputBlockMode = InputBlockMode.Never };
        box.NineSlice.Flags = NineSliceFlags.Bottom | NineSliceFlags.Center;
        box.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        box.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        box.Children.BorderPadding = new Borders(75f, 8f, 75f, 8f);
        var label = new Label { InputBlockMode = InputBlockMode.Never };
        label.TextRenderer.FontSize = 16;
        label.TextRenderer.XmlFormatting = true;
        label.TextRenderer.HAlignment = HAlignment.Left;
        label.TextRenderer.VAlignment = VAlignment.Top;
        label.TextRenderer.OversizeMode = OversizeMode.Wrap;
        label.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        box.AddChild(label);
        box.SelfActive = false;
        pageBox.DockedChildren.Insert(0, box);
        LastBox = new WeakReference<LayoutBox>(box);
        double next = 0;
        pageBox.BeforeInputWhileActive((_, _) =>
        {
            double now = App.Clock.Time.Seconds;
            if (now < next) return;
            next = now + 0.5;
            UxKit.Guard("trade hints", () =>
            {
                string text = Text(__instance);
                label.Text = text;
                box.SelfActive = text.Length > 0;
            });
        });
    });

    /// <summary>Hint text for this trade tab, or "" when no switch applies. Also read by the autotest.</summary>
    internal static string Text(CommTradeTab tab)
    {
        var lines = new List<string>();
        if (Run.Current is { } run && run.Game == tab.Game && run.State.Result == RunResult.Running && run.T.Ux.TechSaleHint)
            TechLine(run, tab, lines);
        if (UxKit.On(s => s.TradeHints))
        {
            PriceLine(tab, lines);
            DeliveryLines(tab, lines);
        }
        return string.Join("\n", lines);
    }

    static void TechLine(Run run, CommTradeTab tab, List<string> lines)
    {
        int held = run.State.FleetTech;
        if (held <= 0 || tab.Caller.Resources.GetResourceTotal(Vaults.TechId) <= 0) return;
        if (Buyer.IsBuyer(tab.Callee)) { lines.Add($"<good>{UxKit.Text("TechSaleBuyer")}</good>"); return; }
        // The planned sale on the tech row (a negative delta sells), or one unit when nothing is planned.
        var row = tab._tradeWidgets.FirstOrDefault(w => w.ResourceType == Vaults.TechId);
        int n = Math.Clamp(row is { Delta: < 0 } ? -row.Delta : 1, 1, held);
        long refund = -tab.GetTradeCost(Vaults.TechId, -n);
        if (refund <= 0) return; // The station does not buy tech.
        var heat = run.T.Heat;
        float before = HeatModel.Multiplier(heat.HeatMultiplierByUnits, held) * heat.HeatPerGameMinute;
        float after = HeatModel.Multiplier(heat.HeatMultiplierByUnits, held - n) * heat.HeatPerGameMinute;
        // The Journey score rule (spec 7): sell now (the refund is money) against keep (the units count as held, at the Buyer price).
        // The other money and the other held units are the same base for both, so they are left out.
        long price = Buyer.UnitPrice(run);
        long scoreDelta = Buyer.Score(refund, 0, price) - Buyer.Score(0, n, price);
        lines.Add(UxKit.Format("TechSaleFmt", n, before, after, scoreDelta.ToString("N0")));
    }

    static void PriceLine(CommTradeTab tab, List<string> lines)
    {
        if (FactionRules.GetFaction(tab.Callee.Metadata.PlayerIndex) is not { } faction) return;
        var mode = tab.SimMode.GameMode;
        var title = mode.GetCurrentReputationTitle(faction.ID);
        float sell = tab.SimMode.EconDifficulty.ResourceSaleRefund * title.ResourceSellCostFactor;
        lines.Add(UxKit.Format("TradePricesFmt", faction.NameKey.Text, title.NameKey.Text, title.ResourceBuyCostFactor, sell));
    }

    static void DeliveryLines(CommTradeTab tab, List<string> lines)
    {
        var mode = tab.SimMode.GameMode;
        var seen = new HashSet<Mission>();
        Add(mode.Missions, accepted: true);
        if (tab.Callee.AI is { } ai && ai.WillProvideMissions(tab.Caller) && ai.GetProvidedMissions(tab.Caller) is { } offered) Add(offered, accepted: false);

        void Add(IEnumerable<Mission> missions, bool accepted)
        {
            var objectives = new List<DeliverResourcesObjective>();
            foreach (var m in missions)
            {
                if (!seen.Add(m)) continue;
                objectives.Clear();
                m.GetObjectives(objectives, activeOnly: accepted);
                foreach (var o in objectives.Where(o => o.ToShip.ShipID == tab.Callee.UniqueID))
                {
                    var rr = o.ResourceType.GetResourceRules();
                    int money = m.GetRewardSortValue(Reward.SortValueType.Money);
                    lines.Add(UxKit.Format(accepted ? "DeliveryAcceptedFmt" : "DeliveryOfferedFmt",
                        (rr.PluralNameKey ?? rr.NameKey).Text, o.Count, o.TargetCount, money.ToString("N0")));
                }
            }
        }
    }
}

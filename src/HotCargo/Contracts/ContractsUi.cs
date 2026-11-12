using System.Runtime.CompilerServices;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Modes.Career.Missions;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Text;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Contracts;

/// <summary>
/// Spec 106: contract terms in the mission info panel (comms missions tab and mission log), the fee on the accept button,
/// and a line for the heat panel. GUI only: it reads state and never writes it.
/// </summary>
static class ContractsUi
{
    static string T(string key) => Strings.GetText("HotCargoContracts/" + key);
    static string F(string key, params object?[] args) => Strings.FormatText("HotCargoContracts/" + key, args);

    static Run? RunOf(Mission m) => Run.Current is { } r && r.Game == m.Game && r.PressureActive ? r : null;

    /// <summary>Lines of the contract section, or empty when the mission is not a contract. Each line has XML text and an optional tooltip.</summary>
    internal static List<(string Text, string? Tip)> Lines(Run run, Mission m)
    {
        var lines = new List<(string, string?)>();
        bool accepted = run.Mode.Missions.ContainsMission(m);
        var terms = accepted ? ContractBook.Find(run, m) : ContractBook.Preview(run, m);
        if (terms is null) return lines;
        var state = run.State.Contracts;
        float open = ContractBook.OpenWeight(run);
        float rateNow = ContractBook.TrailPerMinute(run, open);

        if (accepted)
        {
            if (state.SoldOut.Contains(m.MissionID)) lines.Add(($"<bad>{F("SoldOutFmt", Esc(ContractBook.ClientName(m)))}</bad>", T("SoldOutTip")));
            if (m.Completion != CompletionState.InProgress) return lines;
            if (terms.Fee > 0) lines.Add(($"<gray>{F("FeePaidFmt", terms.Fee)}</gray>", null));
            lines.Add((F("OpenTrailFmt", Num(terms.TrailWeight), Num(open), Rate(rateNow)), F("TrailTipFmt", Num(run.T.Contracts.TrailFreeContracts), Num(run.T.Contracts.TrailHeatPerContractMinute))));
            if (terms.InformantChance > 0 && !state.SoldOut.Contains(m.MissionID))
                lines.Add(($"<limited_color>{F("InformantFmt", Pct(terms.InformantChance))}</limited_color>", T("InformantTip")));
            return lines;
        }

        if (terms.Fee > 0)
        {
            bool canPay = run.Game.LocalPlayerMoney >= terms.Fee;
            string fee = F("FeeFmt", terms.Fee) + (canPay ? "" : " " + F("FeeShortFmt", run.Game.LocalPlayerMoney));
            lines.Add(($"<bad>{fee}</bad>", F("FeeTipFmt", terms.Bonus)));
        }
        else lines.Add(($"<good>{T("NoFee")}</good>", null));
        if (terms.FlatHeat > 0) lines.Add(($"<bad>{F("FlatHeatFmt", Num(terms.FlatHeat))}</bad>", T("FlatHeatTip")));
        float after = open + terms.TrailWeight;
        float rateAfter = ContractBook.TrailPerMinute(run, after);
        string trail = F("TrailFmt", Num(terms.TrailWeight), Num(open), Num(after), Rate(rateNow), Rate(rateAfter));
        lines.Add((rateAfter > rateNow ? $"<bad>{trail}</bad>" : trail,
            F("TrailTipFmt", Num(run.T.Contracts.TrailFreeContracts), Num(run.T.Contracts.TrailHeatPerContractMinute))));
        foreach (var (rival, station) in ContractBook.RivalsOf(run, m.Game.Sim, m))
        {
            string text = station is null ? F("RivalFmt", Ux.MissionTitle.Xml(rival)) : F("RivalAtFmt", Ux.MissionTitle.Xml(rival), Esc(station));
            lines.Add(($"<limited_color>{text}</limited_color>", T("RivalTip")));
        }
        lines.Add(terms.InformantChance > 0
            ? ($"<bad>{F("InformantFmt", Pct(terms.InformantChance))}</bad>", T("InformantTip"))
            : ($"<good>{T("Discreet")}</good>", null));
        return lines;
    }

    /// <summary>Heat panel line: open contracts and the trail rate. Null with no open contracts.</summary>
    internal static string? HeatPanelLine(Run run)
    {
        if (!run.PressureActive) return null;
        float open = ContractBook.OpenWeight(run);
        if (open <= 0) return null;
        float rate = ContractBook.TrailPerMinute(run, open);
        return rate > 0 ? $"<bad>{F("PanelTrailFmt", Num(open), Rate(rate))}</bad>" : $"<gray>{F("PanelOpenFmt", Num(open), Num(run.T.Contracts.TrailFreeContracts))}</gray>";
    }

    static string Num(float v) => v.ToString(v == MathF.Round(v) ? "0" : "0.0");
    static string Rate(float v) => v.ToString("0.0");
    static string Pct(float v) => (v * 100f).ToString("0");
    static string Esc(string s) => StringTools.EscapeForXml(s);

    /// <summary>Adds the contract section under the rewards of the mission info panel. Both the comms tab and the mission log use this panel.</summary>
    [HarmonyPatch(typeof(MissionsPanel.MissionInfoPanel), nameof(MissionsPanel.MissionInfoPanel.Refresh))]
    static class InfoPanelPatch
    {
        static readonly ConditionalWeakTable<MissionsPanel.MissionInfoPanel, LayoutBox<Label, Widget>> Sections = new();

        static void Postfix(MissionsPanel.MissionInfoPanel __instance) => Errors.Guard("contract terms panel", () =>
        {
            if (Sections.TryGetValue(__instance, out var old)) { __instance.Children.Remove(old); Sections.Remove(__instance); }
            if (__instance.Mission is not { } m || RunOf(m) is not { } run) return;
            var lines = Lines(run, m);
            if (lines.Count == 0) return;
            var box = new LayoutBox<Label, Widget>();
            box.NineSlice.Flags = NineSliceFlags.None;
            box.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
            box.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
            box.Children.WidgetPadding = new Vector2(5f);
            // Same look as the vanilla "Rewards" header and reward lines.
            var header = new Label { Text = T("Header") };
            header.TextRenderer.HAlignment = HAlignment.Left;
            header.TextRenderer.FontStyle = FontStyle.Bold;
            header.TextRenderer.OversizeMode = OversizeMode.Wrap;
            header.TextRenderer.Padding = new Borders(0f, 5f, 0f, 5f);
            header.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
            box.AddChild(header);
            foreach (var (text, tip) in lines)
            {
                var label = new Label { Text = text };
                label.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
                label.TextRenderer.HAlignment = HAlignment.Left;
                label.TextRenderer.VAlignment = VAlignment.Center;
                label.TextRenderer.XmlFormatting = true;
                label.TextRenderer.OversizeMode = OversizeMode.Wrap;
                label.TextRenderer.Padding = new Borders(6f, 0f, 0f, 0f);
                if (tip is not null)
                {
                    label.ToolTips.TextProvider = new SimpleTextProvider(tip, null);
                    label.ToolTipDisplay = m.Game.Gui.ToolTipLeft;
                }
                box.AddChild(label);
            }
            int at = __instance.Children.IndexOf(__instance._rewardDisplayLines);
            if (at < 0) throw new InvalidOperationException("mission info panel has no reward lines");
            __instance.Children.Insert(at + 1, box);
            Sections.Add(__instance, box);
        });
    }

    /// <summary>The accept button names the fee, and it does not react when the player cannot pay. The accept input checks again.</summary>
    [HarmonyPatch(typeof(CommMissionsTab), "OnMissionSelected")]
    static class AcceptButtonPatch
    {
        static void Postfix(CommMissionsTab __instance, Mission m) => Errors.Guard("contract accept button", () =>
        {
            var btn = __instance._acceptBtn;
            int fee = RunOf(m) is { } run && !run.Mode.Missions.ContainsMission(m) ? ContractBook.Preview(run, m)?.Fee ?? 0 : 0;
            if (fee <= 0)
            {
                btn.TextProvider = Strings.KeyString("CommWindow/AcceptMission");
                btn.SelfInputActive = true;
                return;
            }
            bool canPay = m.Game.LocalPlayerMoney >= fee;
            btn.TextProvider = new SimpleTextProvider(F(canPay ? "AcceptPayFmt" : "AcceptCannotPayFmt", fee), null);
            btn.SelfInputActive = canPay;
        });
    }
}

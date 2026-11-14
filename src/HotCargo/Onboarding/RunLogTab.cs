using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career.Missions;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Rects;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Tech;

namespace HotCargo.Onboarding;

/// <summary>
/// Spec 732: a "Hot Cargo run" tab in the vanilla career log window, after the mission log and reputation tabs.
/// It lists the run log, newest first. UI only: it reads the run state.
/// </summary>
[HarmonyPatch(typeof(CareerLogWindow), MethodType.Constructor, typeof(GameRoot))]
static class RunLogTab
{
    /// <summary>Rows in the open tab. For autotests.</summary>
    internal static int Rows;

    static void Postfix(CareerLogWindow __instance, GameRoot game) => Errors.Guard("run log tab", () =>
    {
        if (Run.Current?.Game != game) return;
        var tabs = __instance.Children.OfType<TabBox>().FirstOrDefault() ?? throw new InvalidOperationException("career log window has no tab box");
        var icon = GameApp.Rules.GetResourceRules(Vaults.TechId).Icon ?? throw new InvalidDataException("the Stolen Tech resource has no icon");
        // The same settings as the vanilla tabs (CareerLogWindow._AddTab).
        var button = new ImageListItem(icon, null);
        button.CopySettingsFrom(WidgetRules.Instance.TabButton);
        button.ToolTips.TextProvider = Strings.KeyString("HotCargoRunLog/TabName");
        button.ToolTipDisplay = game.Gui.ToolTipAbove;
        button.Size = new Vector2(50f, 50f);
        button.AutoSize.AutoWidthMode = AutoSizeMode.Disable;
        button.ImageRectController.InnerRectMode = InnerRectMode.Fill;
        button.ImageSprite.ScaleMode = SpriteScaleMode.FitMaintainAspect;
        var page = new ScrollBox(TabBox.DefaultPageBox);
        page.NineSlice.Flags = NineSliceFlags.None;
        page.AnchorRect = AnchorPresets.Fill;
        page.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        page.Children.BorderPadding = new Borders(10f);
        page.Children.WidgetPadding = new Vector2(0f, 4f);
        page.ScrollBarController.ShowHorizontal = ScrollBarShowMode.Never;
        page.ScrollBarController.ShowVertical = ScrollBarShowMode.WhenNeeded;
        tabs.AddTab(button, page);
        RunLogEntry? newest = null;
        int count = -1;
        int record = -1;
        page.BeforeDrawWhileActive((_, _) => Errors.Guard("run log tab", () =>
        {
            if (Run.Current is not { } run || run.Game != game) return;
            var log = run.State.Log;
            // Spec 1225: the past runs come from the record file; a write or read of it changes the version.
            var runs = RunRecord.Recorder.Runs ?? RunRecord.Recorder.Load() ?? [];
            if (log.Count == count && ReferenceEquals(newest, log.LastOrDefault()) && record == RunRecord.Recorder.Version) return;
            count = log.Count;
            newest = log.LastOrDefault();
            record = RunRecord.Recorder.Version;
            Fill(page, log, runs);
        }));
    });

    static void Fill(ScrollBox page, List<RunLogEntry> log, IReadOnlyList<RunRecord.RunEntry> runs)
    {
        page.Children.Clear();
        foreach (string line in RunRecord.RecordRows.Lines(runs)) page.AddChild(Row(line));
        page.AddChild(Row(RunRecord.RecordRows.Text("ThisRun")));
        if (log.Count == 0) page.AddChild(Row(RunLog.Text("Empty")));
        for (int i = log.Count - 1; i >= 0; i--)
        {
            var e = log[i];
            var time = TimeSpan.FromSeconds(e.Seconds);
            page.AddChild(Row(RunLog.Format("RowFmt", e.Sector, $"{(int)time.TotalHours}:{time.Minutes:00}", e.Text)));
        }
        Rows = log.Count;
    }

    static Label Row(string xml)
    {
        var label = new Label { Text = xml };
        label.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        label.TextRenderer.XmlFormatting = true;
        label.TextRenderer.HAlignment = HAlignment.Left;
        label.TextRenderer.VAlignment = VAlignment.Top;
        label.TextRenderer.OversizeMode = OversizeMode.Wrap;
        return label;
    }
}

using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Gui;
using Cosmoteer.Modes.Career;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Graphics;
using HotCargo.Core;
using HotCargo.Tech;
using HotCargo.Ux;

namespace HotCargo.Ui;

/// <summary>
/// The heat panel (UX.md H1, H2): sector, a heat bar with a mark for each stage, the current stage and the next one,
/// the tech count and price, and a strip that names a stage when it starts. GUI only: it reads run state, it never writes it.
/// </summary>
sealed class HeatPanel : LayoutBox
{
    const float BarHeight = 14f;
    static readonly Color Calm = new(0.35f, 0.75f, 0.95f);
    static readonly Color Warm = new(1f, 0.8f, 0.2f);
    static readonly Color Hot = new(0.96f, 0.19f, 0.17f);

    readonly CareerGameModeManager _mode;
    readonly Label _sector;
    readonly Label _status;
    readonly Label _tech;
    readonly Label _contracts;
    readonly Label _alert;
    readonly Label _debug;

    /// <summary>Stage alerts shown since start. For autotests.</summary>
    internal static int AlertsShown;

    ObjectID? _node;
    int _seenStages = -1;
    double _alertUntil;
    double _flashUntil;

    public HeatPanel(CareerGameModeManager mode)
    {
        _mode = mode;
        Width = 330f;
        AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        InputBlockMode = InputBlockMode.Never;
        Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        Children.BorderPadding = new Borders(12f, 8f, 12f, 10f);
        Children.WidgetPadding = new Vector2(4f);
        WidgetRules.AddBlur(this);

        var header = new SimpleBox { Height = 22f, InputBlockMode = InputBlockMode.Never };
        header.NineSlice.Flags = NineSliceFlags.None;
        var title = NewLabel(16, $"<b>{UxKit.Text("Title")}</b>");
        title.AnchorRect = AnchorPresets.Fill;
        header.AddChild(title);
        _sector = NewLabel(16, "");
        _sector.AnchorRect = AnchorPresets.Fill;
        _sector.TextRenderer.HAlignment = HAlignment.Right;
        header.AddChild(_sector);
        AddChild(header);

        var bar = new StaticWidget { Height = BarHeight, InputBlockMode = InputBlockMode.Never };
        bar.NineSlice.Flags = NineSliceFlags.None;
        bar.Components.Add(new WidgetDelegatedRenderer(DrawBar, bar));
        AddChild(bar);

        _status = NewLabel(16, "");
        AddChild(_status);
        _tech = NewLabel(14, "");
        AddChild(_tech);
        _contracts = NewLabel(14, "");
        AddChild(_contracts);
        _alert = NewLabel(18, "");
        AddChild(_alert);
        _debug = NewLabel(12, "");
        AddChild(_debug);

        BeforeFrameDraw += (_, _) => Refresh();
    }

    static Label NewLabel(int size, string text)
    {
        var l = new Label { Text = text, InputBlockMode = InputBlockMode.Never };
        l.TextRenderer.Font = WidgetRules.Instance.ShadowFont;
        l.TextRenderer.FontSize = size;
        l.TextRenderer.XmlFormatting = true;
        l.TextRenderer.HAlignment = HAlignment.Left;
        l.TextRenderer.VAlignment = VAlignment.Top;
        l.TextRenderer.OversizeMode = OversizeMode.Wrap;
        l.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        return l;
    }

    Run? MyRun => Run.Current is { } r && r.Mode == _mode ? r : null;

    void Refresh()
    {
        var run = MyRun;
        SelfActive = run is not null && run.T.Ux.HeatPanel && Overlay.Visible && _mode.Game.MapDisplayFactor < 1f;
        if (run is null || !SelfActive) return;
        var s = run.State;
        var t = run.T;
        double now = App.Clock.Time.Seconds;
        DetectStageStart(run, now);

        _sector.Text = UxKit.Format("SectorFmt", s.SectorIndex, t.Journey.RunSectorCount);
        float mult = run.HeatMultiplier(s.FleetTech);
        _status.Text = s.Result switch
        {
            RunResult.Won => $"<good>{UxKit.Format("StatusWonFmt", s.Score.ToString("N0"))}</good>",
            RunResult.Lost => $"<bad>{UxKit.Text("StatusLost")}</bad>",
            _ when !s.HeatEnabled => $"<gray>{UxKit.Text("StatusHeatOff")}</gray>",
            _ => StatusLine(s, t.Heat, mult * t.Heat.HeatPerGameMinute + Contracts.ContractBook.TrailPerMinute(run, Contracts.ContractBook.OpenWeight(run))),
        };
        long value = run.TechValue();
        _tech.Text = LossTimer.PanelLine(run) ?? UxKit.Format("TechFmt", s.FleetTech, value.ToString("N0"));
        string? contracts = Contracts.ContractsUi.HeatPanelLine(run);
        _contracts.SelfActive = contracts is not null;
        _contracts.Text = contracts ?? "";
        _alert.SelfActive = now < _alertUntil;
        // Spec 60: in multiplayer the tunables hash stays visible, so players can compare it.
        _debug.SelfActive = t.Debug.DebugKeys || run.IsMultiplayer;
        if (_debug.SelfActive)
            _debug.Text = run.IsMultiplayer
                ? $"<gray>tunables {s.TunablesHash} (mp, no reload)</gray>"
                : $"<gray>heat {s.Heat:0.0}  x{mult:0.00}  tunables {s.TunablesHash}  F9/F10</gray>";
    }

    /// <summary>ratePerMinute: time heat plus the contract trail (spec 24, 101).</summary>
    static string StatusLine(RunState s, HeatTunables heat, float ratePerMinute)
    {
        string stage = s.StagesStarted == 0
            ? $"<gray>{UxKit.Text("Calm")}</gray>"
            : $"{StageTag(s.StagesStarted - 1, heat.Stages.Length)}{heat.Stages[s.StagesStarted - 1].Name.ToUpperInvariant()}{StageTagEnd(s.StagesStarted - 1, heat.Stages.Length)}";
        string next = s.StagesStarted < heat.Stages.Length
            ? UxKit.Format("NextFmt", heat.Stages[s.StagesStarted].Name, heat.Stages[s.StagesStarted].Heat)
            : UxKit.Text("NextMax");
        return UxKit.Format("StatusFmt", stage, next, ratePerMinute);
    }

    static string StageTag(int stage, int count) => stage >= count - 2 ? "<bad>" : "<limited_color>";
    static string StageTagEnd(int stage, int count) => stage >= count - 2 ? "</bad>" : "</limited_color>";

    /// <summary>Names a stage when it starts during a system visit. A jump or a save load sets the baseline again.</summary>
    void DetectStageStart(Run run, double now)
    {
        var s = run.State;
        var node = _mode.Game.CurrentMapNode?.UniqueID;
        if (_seenStages < 0 || !Equals(node, _node) || s.StagesStarted < _seenStages)
        {
            _node = node;
            _seenStages = s.StagesStarted;
            return;
        }
        if (s.StagesStarted == _seenStages) return;
        _seenStages = s.StagesStarted;
        if (!run.T.Ux.StageAlerts || s.StagesStarted == 0) return;
        var stage = run.T.Heat.Stages[s.StagesStarted - 1];
        string what = UxKit.StageText(stage.Name);
        AlertsShown++;
        _alert.Text = $"<bad>{stage.Name.ToUpperInvariant()}</bad>  {what}";
        _alertUntil = now + run.T.Ux.StageAlertSeconds;
        _flashUntil = now + 2.0;
        UxKit.ChatLine(_mode.Game, UxKit.Format("StageChatFmt", stage.Name.ToUpperInvariant(), what));
    }

    void DrawBar(GraphicsManager gfx, IntRect pixelRect, Rect r, Color colorScale)
    {
        if (MyRun is not { } run) return;
        var s = run.State;
        var stages = run.T.Heat.Stages;
        double now = App.Clock.Time.Seconds;
        float f = Math.Clamp(s.Heat / HeatModel.MaxHeat, 0f, 1f);

        gfx.SetToMaterial(null);
        gfx.Draw<Quad>(new Quad(r, new Color(0f, 0f, 0f, 0.55f) * colorScale));

        Color fill = s.StagesStarted == 0 ? Color.Lerp(Calm, Warm, f) : Color.Lerp(Warm, Hot, f);
        if (!s.HeatEnabled || s.Result != RunResult.Running) fill = new Color(0.5f, 0.5f, 0.5f);
        // Pulse when the next stage is close, and flash when a stage just started.
        bool close = s.StagesStarted < stages.Length && stages[s.StagesStarted].Heat - s.Heat <= run.T.Ux.StageWarnHeat;
        if (now < _flashUntil || (close && s.HeatEnabled))
        {
            float speed = now < _flashUntil ? 10f : 4f;
            float pulse = 0.5f + 0.5f * MathF.Sin((float)now * speed);
            fill = Color.Lerp(fill, Color.White, 0.45f * pulse);
        }
        var filled = new Rect(r.X, r.Y, r.Width * f, r.Height);
        gfx.Draw<Quad>(new Quad(filled, fill * colorScale));

        for (int i = 0; i < stages.Length; i++)
        {
            float x = r.X + r.Width * Math.Clamp(stages[i].Heat / HeatModel.MaxHeat, 0f, 1f);
            bool reached = i < s.StagesStarted;
            var mark = new Rect(x - 1f, r.Y - 3f, 2f, r.Height + 6f);
            gfx.Draw<Quad>(new Quad(mark, (reached ? new Color(0f, 0f, 0f, 0.8f) : new Color(1f, 1f, 1f, 0.85f)) * colorScale));
        }
    }
}

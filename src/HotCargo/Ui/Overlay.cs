using Cosmoteer.Gui;
using Cosmoteer.Modes.Career;
using Halfling;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Input;
using HotCargo.Core;
using HotCargo.Tech;

namespace HotCargo.Ui;

/// <summary>Always-visible Hot Cargo status text (spec requirement 55). F8 toggles it.</summary>
public static class Overlay
{
    /// <summary>Set when a lifecycle hook failed. Shown in red so the failure is not silent.</summary>
    public static string? FatalError;

    static bool _visible = true;

    internal static void Attach(CareerGameModeManager mode)
    {
        FatalError = null;
        var label = new Label
        {
            AnchorRect = AnchorPresets.TopRight,
            X = -20f,
            Y = 140f,
            InputBlockMode = InputBlockMode.Never,
        };
        label.TextRenderer.Font = WidgetRules.Instance.ShadowGlowFont;
        label.TextRenderer.FontSize = 16;
        label.TextRenderer.HAlignment = HAlignment.Right;
        label.TextRenderer.VAlignment = VAlignment.Top;
        label.TextRenderer.OversizeMode = OversizeMode.None;
        label.TextRenderer.Clip = false;
        label.AutoSize.AutoWidthMode = AutoSizeMode.Enable;
        label.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        label.AutoSize.Anchor = new Halfling.Geometry.Vector2(1f, 0f);
        label.BeforeFrameDraw += (_, _) =>
        {
            if (App.Keyboard.HotkeyPressed(ViKey.F8)) _visible = !_visible;
            label.SelfActive = _visible && mode.Game.MapDisplayFactor < 1f;
            if (label.SelfActive) label.Text = Text(mode);
        };
        mode.Game.Gui.RootWidget.AddChild(label);
    }

    static string Text(CareerGameModeManager mode)
    {
        if (FatalError is not null) return $"HOT CARGO ERROR\n{FatalError}";
        if (Run.Current is not { } run || run.Mode != mode) return "";
        var s = run.State;
        var t = run.T;
        float mult = HeatModel.Multiplier(t.Heat.HeatMultiplierByUnits, s.FleetTech);
        float perMinute = mult * t.Heat.HeatPerGameMinute;
        string next = s.StagesStarted < t.Heat.Stages.Length
            ? $"{t.Heat.Stages[s.StagesStarted].Name} at {t.Heat.Stages[s.StagesStarted].Heat:0}"
            : "max";
        string stage = s.StagesStarted == 0 ? "calm" : t.Heat.Stages[s.StagesStarted - 1].Name;
        string result = s.Result switch
        {
            RunResult.Won => "\nRUN WON - heat off",
            RunResult.Lost => "\nRUN LOST",
            _ => s.HeatEnabled ? "" : "\nheat off",
        };
        string error = TunablesFile.LastError is { } e ? $"\ntunables error: {e}" : "";
        string mp = run.IsMultiplayer ? $"  (mp, no reload)" : "";
        return
            $"HOT CARGO  sector {s.SectorIndex}/{t.Journey.RunSectorCount}\n" +
            $"heat {s.Heat:0.0} / 100   +{perMinute:0.0}/min\n" +
            $"stage {stage}   next {next}\n" +
            $"tech {s.FleetTech}   x{mult:0.00}   value {t.Tech.Value(s.SectorIndex, s.GameSeconds):N0}\n" +
            $"tunables {s.TunablesHash}{mp}" + result + error;
    }
}

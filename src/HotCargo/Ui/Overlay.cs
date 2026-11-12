using Cosmoteer.Gui;
using Cosmoteer.Modes.Career;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Input;
using HotCargo.Core;
using HotCargo.Tech;
using HotCargo.Ux;

namespace HotCargo.Ui;

/// <summary>
/// Hot Cargo status on the HUD (spec requirement 55). F8 toggles it.
/// Ux.HeatPanel on: the heat panel at the top of the left HUD column. Off: the plain text block, top right.
/// The text block also shows lifecycle and tunables errors, so that a failure is never silent.
/// </summary>
public static class Overlay
{
    /// <summary>The newest failure that Errors.Report saw. Shown in red so the failure is not silent. It stays until the game restarts.</summary>
    public static string? FatalError;

    internal static bool Visible = true;

    internal static void Attach(CareerGameModeManager mode)
    {
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
        label.AutoSize.Anchor = new Vector2(1f, 0f);
        label.BeforeFrameDraw += (_, _) =>
        {
            if (App.Keyboard.HotkeyPressed(ViKey.F8)) Visible = !Visible;
            bool panel = Run.Current is { } r && r.Mode == mode && r.T.Ux.HeatPanel;
            bool error = FatalError is not null || TunablesFile.LastError is not null;
            label.SelfActive = (error || (Visible && !panel)) && mode.Game.MapDisplayFactor < 1f;
            if (label.SelfActive) label.Text = panel ? ErrorText() : Text(mode);
        };
        mode.Game.Gui.RootWidget.AddChild(label);
        mode.Game.Gui.TopLeftHudBox.Children.Insert(0, new HeatPanel(mode));
    }

    static string ErrorText()
    {
        if (FatalError is not null) return $"HOT CARGO ERROR\n{FatalError}";
        return TunablesFile.LastError is { } e ? $"tunables error: {e}" : "";
    }

    static string Text(CareerGameModeManager mode)
    {
        if (FatalError is not null) return $"HOT CARGO ERROR\n{FatalError}";
        if (Run.Current is not { } run || run.Mode != mode) return "";
        var s = run.State;
        var t = run.T;
        float mult = run.HeatMultiplier(s.FleetTech);
        float open = Contracts.ContractBook.OpenWeight(run);
        float trail = Contracts.ContractBook.TrailPerMinute(run, open);
        float perMinute = mult * t.Heat.HeatPerGameMinute + trail;
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
        // Spec 60: the tunables hash shows in multiplayer, so players can compare it, and with debug keys on.
        string hash = run.IsMultiplayer ? $"\ntunables {s.TunablesHash}  (mp, no reload)" : t.Debug.DebugKeys ? $"\ntunables {s.TunablesHash}" : "";
        return
            $"HOT CARGO  sector {s.SectorIndex}/{t.Journey.RunSectorCount}\n" +
            $"heat {s.Heat:0.0} / 100   +{perMinute:0.0}/min\n" +
            $"stage {stage}   next {next}\n" +
            $"tech {s.FleetTech}   x{mult:0.00}   value {run.TechValue():N0}   notoriety {s.Notoriety:0}\n" +
            $"contracts {open:0.#} open   trail +{trail:0.0}/min" +
            hash + result + error;
    }
}

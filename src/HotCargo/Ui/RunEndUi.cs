using Cosmoteer.Gui;
using Cosmoteer.Modes.Career;
using Halfling.Gui.Dialogs;
using HotCargo.Core;
using HotCargo.Tech;
using HotCargo.Ux;

namespace HotCargo.Ui;

/// <summary>
/// Shows the run intro, and the win or loss UI once per game. Runs from the frame update, never from sim logic.
/// Ux.RunEndPanel on: a win panel, and the loss on the game-over screen (see GameOverPatch). Off: plain dialogs.
/// </summary>
public static class RunEndUi
{
    static Run? _shownFor;
    static Run? _introFor;

    /// <summary>Set by the NewRunStarted hook. The intro shows on the next frame, when the HUD exists.</summary>
    internal static Run? IntroPending;

    internal static void Update(Run run)
    {
        ShowIntro(run);
        if (run.State.Result == RunResult.Running || _shownFor == run) return;
        _shownFor = run;
        bool panel = run.T.Ux.RunEndPanel;
        if (run.State.Result == RunResult.Lost)
        {
            var gui = new GameOverGui(run.Game);
            run.Game.Gui.RootWidget.Children.InsertBelow(run.Game.Gui.FloatingWindows, gui);
            if (!panel) _ = OneButtonDialog.Show($"{UxKit.Text("LossTitle")}\n\n{run.State.EndReason}");
        }
        else if (panel)
        {
            var st = run.State;
            string body = $"<s24>{st.EndReason}</s24>\n\n{UxKit.Format("WinScoreFmt", st.Score.ToString("N0"), st.DeliveredUnits)}\n\n" +
                          $"{StatsLine(run)}\n<gray>{UxKit.Text("WinContinue")}</gray>";
            CenterPanel.Show(run.Game, UxKit.Text("WinTitle"), WidgetRules.Instance.GoodTextColor, body, UxKit.Text("WinButton"));
        }
        else
        {
            _ = OneButtonDialog.Show($"{UxKit.Text("WinTitle")}\n\n{run.State.EndReason}\n{UxKit.Format("ScoreFmt", run.State.Score.ToString("N0"), run.State.DeliveredUnits)}\n\n{UxKit.Text("WinContinue")}");
        }
    }

    static void ShowIntro(Run run)
    {
        if (IntroPending != run || _introFor == run) return;
        IntroPending = null;
        _introFor = run;
        if (!run.T.Ux.RunIntro) return;
        var t = run.T;
        string body = UxKit.Format("IntroBodyFmt", t.Journey.RunSectorCount, t.Heat.Stages.Length, t.Heat.HeatCarryOverFraction);
        // Wider than the win panel, so each rule fits on one line at 1280 x 720.
        CenterPanel.Show(run.Game, UxKit.Text("IntroTitle"), WidgetRules.Instance.FameTextColor, body, UxKit.Text("IntroButton"), width: 760f);
    }

    /// <summary>One line of run numbers for the end screens.</summary>
    internal static string StatsLine(Run run)
    {
        var s = run.State;
        var time = TimeSpan.FromSeconds(s.GameSeconds);
        long value = run.T.Tech.Value(s);
        return UxKit.Format("RunStatsFmt", s.SectorIndex, run.T.Journey.RunSectorCount, $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}",
            s.FleetTech, value.ToString("N0"));
    }
}

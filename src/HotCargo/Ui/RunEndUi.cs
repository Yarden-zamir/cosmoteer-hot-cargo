using Cosmoteer.Modes.Career;
using Halfling.Gui.Dialogs;
using HotCargo.Core;

namespace HotCargo.Ui;

/// <summary>Shows the win or loss UI once per game, from the frame update (not from sim logic).</summary>
public static class RunEndUi
{
    static Run? _shownFor;

    internal static void Update(Run run)
    {
        if (run.State.Result == RunResult.Running || _shownFor == run) return;
        _shownFor = run;
        if (run.State.Result == RunResult.Lost)
        {
            var gui = new GameOverGui(run.Game);
            run.Game.Gui.RootWidget.Children.InsertBelow(run.Game.Gui.FloatingWindows, gui);
            _ = OneButtonDialog.Show($"The corporation recovered the tech.\n\n{run.State.EndReason}");
        }
        else
        {
            _ = OneButtonDialog.Show($"You got the tech out.\n\n{run.State.EndReason}\n\nHeat is off. You can keep playing this career.");
        }
    }
}

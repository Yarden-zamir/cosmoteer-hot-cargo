using Cosmoteer.Game;
using Cosmoteer.Modes.Career;
using Halfling.Gui;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.RunModes;

/// <summary>
/// Spec 685: a Hot Cargo loss in an Ironman career deletes the Ironman save, like the vanilla game over
/// (CareerGameModeManager.Update). Only the host has the save. Runs from the frame update: a file operation, no sim state.
/// The save cannot come back: CareerPatches.AllowSaveGame is false after a loss, so the exit save and the autosave do not run.
/// </summary>
static class Ironman
{
    static Run? _handled;

    /// <summary>Saves deleted since game start. For autotests.</summary>
    internal static int Deleted;

    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.Update))]
    static class FramePatch
    {
        static void Postfix(CareerGameModeManager __instance) => Errors.Guard("ironman loss", () =>
        {
            if (Run.Current is not { } run || run.Mode != __instance || run.State.Result != RunResult.Lost || _handled == run) return;
            _handled = run;
            if (!__instance.IsIronmanModeEnabled || !run.Game.NetManager.IsHost) return;
            string path = __instance.IronmanSavePath;
            // An Ironman career saves for the first time at an autosave or at exit, so an early loss can have no file yet.
            bool existed = File.Exists(path);
            File.Delete(path);
            run.Game.ClearLastSave();
            Deleted++;
            Log.Info($"ironman: run lost, {(existed ? "deleted the save" : "no save to delete at")} {Path.GetFileName(path)}");
        });
    }

    /// <summary>
    /// The Ironman line on the game-over screen. High priority, so it runs before the Ux patch moves the content into its panel;
    /// the line goes above the button row, and the Ux patch wraps it like its own lines.
    /// </summary>
    [HarmonyPatch(typeof(GameOverGui), MethodType.Constructor, typeof(GameRoot))]
    static class GameOverLine
    {
        [HarmonyPriority(Priority.High)]
        static void Postfix(GameOverGui __instance, GameRoot game) => Errors.Guard("ironman game over line", () =>
        {
            if (!game.Mode.IsIronmanModeEnabled || Run.Current?.Game != game) return;
            var box = (LayoutBox)__instance.Children[0];
            var line = new Label { Text = $"<bad>{Difficulty.Text("IronmanLoss")}</bad>" };
            line.TextRenderer.Font = Cosmoteer.Gui.WidgetRules.Instance.ShadowFont;
            line.TextRenderer.FontSize = 18;
            line.TextRenderer.XmlFormatting = true;
            line.TextRenderer.HAlignment = Halfling.Graphics.Text.HAlignment.Center;
            box.Children.Insert(box.Children.Count - 1, line);
            LossLinesShown++;
        });
    }

    /// <summary>Game-over Ironman lines shown since game start. For autotests.</summary>
    internal static int LossLinesShown;

    /// <summary>The Ironman line for the win screen, or "" outside Ironman.</summary>
    internal static string WinLine(Run run) =>
        run.Mode.IsIronmanModeEnabled ? $"\n\n<fame_color>{Difficulty.Text("IronmanWin")}</fame_color>" : "";
}

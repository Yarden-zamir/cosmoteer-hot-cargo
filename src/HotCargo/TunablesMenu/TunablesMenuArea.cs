using System.Globalization;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Localization;
using Halfling;
using Halfling.Gui;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.TunablesMenu;

/// <summary>The "Hot Cargo settings" button in the escape menu and the test commands of the settings menu (spec 500 to 506).</summary>
public static class TunablesMenuArea
{
    public static void Register()
    {
        // A stale entry is a bug that the tests must catch, but it must not stop the game from starting.
        Errors.Guard("tunables timing", TunableTiming.Check);
        Run.Hooks.Frame += TuneRequest.Poll;
        Autotest.Commands["menu"] = MenuCommand;
        Autotest.Fields["menu_ready"] = () => Autotest.RequireRun() is { HostTunablesPending: false, HostSendsInFlight: 0 } ? 1 : 0;
        Autotest.Fields["menu_open"] = () => TunablesDialog.Open is null ? 0 : 1;
        Autotest.Fields["menu_rows"] = () => Dialog().Rows.Count;
        Autotest.Fields["menu_sections"] = () => Dialog().SectionCount;
        Autotest.Fields["menu_error"] = () => Dialog().Error is null ? 0 : 1;
        Autotest.Fields["menu_readonly"] = () => Dialog().ReadOnly ? 1 : 0;
        // spawnstageindex N: starts the hunter response at index N of Hunters.Stages, so a test needs no stage name. Test only.
        Autotest.Commands["spawnstageindex"] = arg =>
        {
            var run = Autotest.RequireRun();
            Hunters.HunterSpawner.StartStage(run, run.Game.Sim, run.T.Hunters.Stages[int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture)].Name);
        };
        Autotest.Fields["tunables_error"] = () => TunablesFile.LastError is null ? 0 : 1;
        // tunerequest LINE | LINE: writes tune.request like scripts/tune.sh. tuneresult has|lacks TEXT: checks tune.result.
        Autotest.Commands["tunerequest"] = arg =>
        {
            File.WriteAllText(Path.Combine(Mod.ModRoot, "tune.request"), string.Join("\n", arg.Split('|', StringSplitOptions.TrimEntries)));
            // The next frame reads it: the mod polls every 0.5 real seconds, and the sim's game clock can run faster.
            TuneRequest.PollNow();
        };
        Autotest.Commands["tuneresult"] = arg =>
        {
            var parts = arg.Split(' ', 2);
            string path = Path.Combine(Mod.ModRoot, "tune.result");
            string content = File.Exists(path) ? File.ReadAllText(path) : "";
            bool want = parts[0] == "has";
            Autotest.Check(content.Contains(parts[1]) == want, $"tune.result {parts[0]} '{parts[1]}' (content: {content.Trim()})");
        };
    }

    /// <summary>Opens the window over the escape menu, as the button does.</summary>
    internal static void OpenDialog(Run run) => App.Director.PushState(new TunablesDialog(run));

    static TunablesDialog Dialog() => TunablesDialog.Open ?? throw new InvalidOperationException("the Hot Cargo settings window is not open");

    /// <summary>
    /// menu escape: opens the escape menu. menu open: opens it and clicks its Hot Cargo settings button.
    /// menu search TEXT: types TEXT in the search box.
    /// menu set PATH VALUE: enters VALUE in the row of PATH, like a player, and fails if the window rejects it.
    /// menu reject PATH VALUE: the same, and fails unless the window rejects it.
    /// menu reset PATH | menu resetsection SECTION: clicks a reset button.
    /// menu value PATH OP NUMBER: checks the value in effect. menu file has|lacks TEXT: checks tunables.rules.
    /// menu shows PATH TEXT: checks the text that the row of PATH shows.
    /// menu close: closes the window and the escape menu.
    /// </summary>
    static void MenuCommand(string arg)
    {
        var parts = arg.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        var run = Autotest.RequireRun();
        switch (parts)
        {
            case ["escape"]:
                App.Director.PushState(new GameMenu(run.Game));
                break;
            case ["search", .. var words]:
                Dialog().Search(string.Join(' ', words));
                break;
            case ["open"]:
            {
                var menu = App.Director.States.OfType<GameMenu>().FirstOrDefault();
                if (menu is null) App.Director.PushState(menu = new GameMenu(run.Game));
                var button = Walk(menu.RootWidget).OfType<Button>().FirstOrDefault(b => b.Text == Strings.GetText("HotCargoTunablesMenu/Button"))
                    ?? throw new InvalidOperationException("the escape menu has no Hot Cargo settings button");
                button.ClickController.InvokeClicked();
                Autotest.Check(TunablesDialog.Open is not null, "the Hot Cargo settings button opens the window");
                Log.Info($"tunables menu: {Dialog().SectionCount} sections, {Dialog().Rows.Count} rows");
                break;
            }
            case ["set", var path, var value]:
            {
                string? error = Row(path).Enter(value);
                Autotest.Check(error is null, $"menu sets {path} = {value}{(error is null ? "" : $" (rejected: {error})")}");
                break;
            }
            case ["reject", var path, var value]:
            {
                object? before = TunablePath.Get(TunablesEditor.Current(run), path);
                string? error = Row(path).Enter(value);
                Log.Info($"tunables menu: rejection message: {error}");
                Autotest.Check(error is not null && Equals(TunablePath.Get(TunablesEditor.Current(run), path), before),
                    $"menu rejects {path} = {value} and keeps {TunablesEditor.Format(before)}");
                break;
            }
            case ["reset", var path]:
                Row(path).Reset.ClickController.InvokeClicked();
                break;
            case ["resetsection", var section]:
                Dialog().ResetSection(section);
                break;
            case ["value", var path, var rest]:
            {
                var opValue = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                double actual = Convert.ToDouble(TunablePath.Get(run.T, path), CultureInfo.InvariantCulture);
                double expected = double.Parse(opValue[1], CultureInfo.InvariantCulture);
                bool ok = opValue[0] switch
                {
                    "==" => Math.Abs(actual - expected) < 1e-4, ">=" => actual >= expected, "<=" => actual <= expected,
                    _ => throw new InvalidOperationException($"unknown op {opValue[0]}"),
                };
                Autotest.Check(ok, $"{path} {rest} (actual {actual})");
                break;
            }
            case ["shows", var path, var text]:
            {
                string shown = Row(path).Shown();
                Autotest.Check(shown == text, $"the row {path} shows '{text}' (shows '{shown}')");
                break;
            }
            case ["file", "has" or "lacks", var text]:
            {
                string content = File.ReadAllText(TunablesFile.FilePath);
                bool has = content.Contains(text);
                Autotest.Check(has == (parts[1] == "has"), $"tunables.rules {parts[1]} '{text}'");
                if (has != (parts[1] == "has")) Log.Info($"tunables.rules:\n{content}");
                break;
            }
            case ["close"]:
                Dialog().Close();
                if (App.Director.States.OfType<GameMenu>().FirstOrDefault() is { } open) App.Director.PopState(open);
                break;
            default:
                throw new InvalidOperationException("menu needs: escape | open | search TEXT | set PATH VALUE | reject PATH VALUE | reset PATH | resetsection NAME | value PATH OP N | shows PATH TEXT | file has|lacks TEXT | close");
        }
    }

    static TunablesDialog.Row Row(string path) =>
        Dialog().Rows.TryGetValue(path, out var row) ? row : throw new InvalidOperationException($"the settings window has no row '{path}'");

    static IEnumerable<Widget> Walk(Widget w) => w.GetChainedWidgets(recursive: true).Prepend(w);
}

/// <summary>Adds the Hot Cargo settings button under the game's Settings button in the escape menu, while a Hot Cargo run is active.</summary>
[HarmonyPatch(typeof(GameMenu), MethodType.Constructor, typeof(GameRoot))]
static class GameMenuPatch
{
    static void Postfix(GameMenu __instance, GameRoot game) => Errors.Guard("tunables menu button", () =>
    {
        if (Run.Current is not { } run || run.Game != game) return;
        string settings = Strings.GetText("GameMenu/Settings");
        var settingsButton = __instance.RootWidget.GetChainedWidgets(recursive: true).OfType<Button>().FirstOrDefault(b => b.Text == settings)
            ?? throw new InvalidOperationException("the escape menu has no Settings button");
        if (settingsButton.Parent?.Widget is not LayoutBox column) throw new InvalidOperationException("the Settings button is not in a layout box");
        var button = new Button
        {
            Height = settingsButton.Height,
            Text = Strings.GetText("HotCargoTunablesMenu/Button"),
        };
        button.ToolTips.Text = Strings.GetText("HotCargoTunablesMenu/ButtonTip");
        button.Clicked += (_, _) => Errors.Guard("tunables menu open", () => TunablesMenuArea.OpenDialog(run));
        column.Children.Insert(column.Children.IndexOf(settingsButton) + 1, button);
    });
}

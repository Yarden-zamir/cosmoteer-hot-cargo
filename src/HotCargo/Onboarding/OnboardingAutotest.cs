using Cosmoteer;
using Cosmoteer.Codex;
using Cosmoteer.Modes.Career.Missions;
using Halfling.Gui;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Onboarding;

/// <summary>Autotest commands and fields of the Onboarding area. Test only.</summary>
static class OnboardingAutotest
{
    static Dictionary<string, CodexPageState>? _savedStates;
    static bool _savedShowTutorials;

    internal static void Register()
    {
        // codex_reset: clears every codex page state, like a new player. codex_restore puts back the player's states and the
        // "Show tutorials" setting. Use both in the main slot, so a screenshot session leaves the player's settings as they were.
        Autotest.Commands["codex_reset"] = _ =>
        {
            if (_savedStates is null)
            {
                _savedStates = new(Settings.CodexPageStates);
                _savedShowTutorials = Settings.ShowTutorials;
            }
            Settings.CodexPageStates.Clear();
        };
        Autotest.Commands["codex_restore"] = _ =>
        {
            if (_savedStates is null) throw new InvalidOperationException("codex_restore needs codex_reset first");
            Settings.CodexPageStates.Clear();
            foreach (var (id, state) in _savedStates) Settings.CodexPageStates[id] = state;
            Settings.ShowTutorials = _savedShowTutorials;
            _savedStates = null;
        };
        // tutorials on|off: the game's "Show tutorials" setting. Use codex_reset first in the main slot, so codex_restore puts it back.
        Autotest.Commands["tutorials"] = arg => Settings.ShowTutorials = arg != "off";
        // codex_read <page|all>: marks a page as opened, or every Hot Cargo page in the How to Play list.
        Autotest.Commands["codex_read"] = arg =>
        {
            var pages = arg == "all" ? GameApp.Rules.Codex.CodexPages.Where(p => Hints.IsHotCargo(p) && p.IsVisible) : [Hints.Page(arg)];
            foreach (var p in pages) p.IsSeen = true;
        };
        // expectpage <page> shown|unshown: checks the page state that the game keeps in its settings.
        Autotest.Commands["expectpage"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || parts[1] is not ("shown" or "unshown")) throw new InvalidOperationException("expectpage needs: page shown|unshown");
            bool shown = Hints.Page(parts[0]).IsShown;
            Autotest.Check(shown == (parts[1] == "shown"), $"page {parts[0]} is {(shown ? "shown" : "unshown")}, expected {parts[1]}");
        };
        // codex_open <page>: opens the page window from its How to Play button, like a click.
        Autotest.Commands["codex_open"] = arg =>
        {
            var hud = Hud();
            var item = hud._codexButtons.GetValueOrDefault(Hints.Page(arg)) ?? throw new InvalidOperationException($"page {arg} has no How to Play button");
            item.Open();
        };
        // careerlog: opens the career log window on the Hot Cargo run tab.
        Autotest.Commands["careerlog"] = _ =>
        {
            var mode = Autotest.RequireRun().Mode;
            if (mode._missionLog is not { SelfActive: true }) mode.OnMissionLogClicked(null, EventArgs.Empty);
            var window = mode._missionLog ?? throw new InvalidOperationException("career log window did not open");
            var tabs = window.Children.OfType<TabBox>().First();
            var buttons = tabs.TabsListBox.Children;
            ((ListItem)buttons[buttons.Count - 1]).IsSelected = true;
        };
        // onboarding <Field> <value>: sets one Onboarding tunable for this run.
        Autotest.Commands["onboarding"] = arg => Autotest.SetField(Autotest.RequireRun().T.Onboarding, arg);

        Autotest.Fields["hints_unread"] = () => Hints.AnyUnread() ? 1 : 0;
        Autotest.Fields["codex_hud_buttons"] = () => Hud().Children.OfType<CodexPageWindowItem>().Count();
        // 1 when the first How to Play button is a Hot Cargo page, 0 when it is vanilla, -1 with no buttons.
        Autotest.Fields["codex_hud_first_hc"] = () => Hud().Children.OfType<CodexPageWindowItem>().FirstOrDefault() is { } first ? (Hints.IsHotCargo(first._page) ? 1 : 0) : -1;
        // Vanilla tutorial pages that the game shows now. The test slot starts with no seen pages, so some always want to show.
        Autotest.Fields["codex_vanilla_shown"] = () => GameApp.Rules.Codex.CodexPages.Count(p => !Hints.IsHotCargo(p) && p.TabNameKey.Key == "Codex/Tutorials" && p.IsShown);
        Autotest.Fields["runlog_count"] = () => Autotest.RequireRun().State.Log.Count;
        Autotest.Fields["runlog_rows"] = () => RunLogTab.Rows;
        foreach (var kind in Enum.GetValues<RunLogKind>())
            Autotest.Fields[$"runlog_{kind.ToString().ToLowerInvariant()}"] = () => Autotest.RequireRun().State.Log.Count(e => e.Kind == kind);
    }

    static CodexHudGui Hud() => Autotest.RequireRun().Game.Gui.CodexHudGui;
}

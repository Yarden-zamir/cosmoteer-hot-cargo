using Cosmoteer.Codex;
using Cosmoteer.Game;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Onboarding;

/// <summary>
/// Spec 723: Hot Cargo pages come before vanilla tutorials. Players of the mod likely know the base game, so vanilla
/// tutorials wait: during the first VanillaTutorialDelaySeconds of a run, and while a Hot Cargo page is new.
/// They are never removed. Without a Hot Cargo run, nothing changes.
/// </summary>
static class HintPriority
{
    /// <summary>The vanilla tab of tutorial pages. Lore and tips have other tabs and never wait.</summary>
    const string TutorialsTab = "Codex/Tutorials";

    /// <summary>Computed once per HUD update, before the pages update their state.</summary>
    static bool _hold;

    [HarmonyPatch(typeof(CodexHudGui), nameof(CodexHudGui.OnUpdatingUIState))]
    static class Hud
    {
        static void Prefix(CodexHudGui __instance)
        {
            _hold = false;
            Errors.Guard("tutorial order", () =>
            {
                if (Run.Current is not { } run || run.Game != __instance._game) return;
                _hold = run.State.GameSeconds < run.T.Onboarding.VanillaTutorialDelaySeconds || Hints.AnyUnread();
            });
        }

        /// <summary>Hot Cargo buttons move to the top of the How to Play list, in the order they came.</summary>
        static void Postfix(CodexHudGui __instance) => Errors.Guard("tutorial order", () =>
        {
            if (Run.Current?.Game != __instance._game) return;
            var children = __instance.Children;
            int slot = 0;
            foreach (var item in children.OfType<CodexPageWindowItem>().Where(i => Hints.IsHotCargo(i._page)).ToList())
            {
                if (children.IndexOf(item) != slot) children.MoveChild(item, slot);
                slot++;
            }
        });
    }

    /// <summary>A vanilla tutorial that waits skips its condition check, so it shows later when the hold ends.</summary>
    [HarmonyPatch(typeof(CodexPageRules), nameof(CodexPageRules.UpdateState))]
    static class Defer
    {
        static bool Prefix(CodexPageRules __instance, GameRoot? game) => !Waits(__instance, game);
    }

    /// <summary>A page that shows for now (TempShowCondition) keeps its update, so it can hide again.</summary>
    internal static bool Waits(CodexPageRules page, GameRoot? game) =>
        _hold && game is not null && Run.Current?.Game == game && !page.IsShown && !page.IsTemporarilyShown
        && !Hints.IsHotCargo(page) && page.TabNameKey.Key == TutorialsTab;
}

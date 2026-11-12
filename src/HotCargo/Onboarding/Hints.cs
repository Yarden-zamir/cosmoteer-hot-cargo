using Cosmoteer;
using Cosmoteer.Codex;
using Cosmoteer.Localization;
using Halfling;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Onboarding;

/// <summary>
/// Spec 720 to 726: Hot Cargo pages in the game's How to Play list and codex. A page is a vanilla codex page in rules
/// (ID prefix "hc_"); its trigger is a condition here. The first frame the condition holds, the page shows in the
/// How to Play list, like a vanilla tutorial. The game stores the page state in its settings, so each page shows once per player.
/// </summary>
public static class Hints
{
    /// <summary>Codex page IDs of Hot Cargo pages start with this.</summary>
    public const string PagePrefix = "hc_";

    sealed record Hint(string PageId, Func<Run, bool> Due);

    static readonly List<Hint> All = [];
    static Dictionary<string, CodexPageRules>? _pages;
    static double _nextCheck;
    static bool _validated;

    /// <summary>Times a trigger showed each page since game start. For autotests; a page shows at most once.</summary>
    internal static readonly Dictionary<string, int> Shown = [];

    /// <summary>
    /// Adds the trigger of a Hot Cargo page. Call it from an area's Register. The condition runs in UI frames, about 4 times
    /// a second, while the page is not shown yet. It must only read state.
    /// </summary>
    public static void Add(string pageId, Func<Run, bool> due)
    {
        if (!pageId.StartsWith(PagePrefix, StringComparison.Ordinal)) throw new ArgumentException($"Hot Cargo page IDs start with '{PagePrefix}': {pageId}");
        if (All.Any(h => h.PageId == pageId)) throw new ArgumentException($"page {pageId} already has a trigger");
        All.Add(new Hint(pageId, due));
        Autotest.Fields[$"hint_{pageId}"] = () => Shown.GetValueOrDefault(pageId);
    }

    public static bool IsHotCargo(CodexPageRules page) => page.ID.StartsWith(PagePrefix, StringComparison.Ordinal);

    static Dictionary<string, CodexPageRules> Pages => _pages ??= GameApp.Rules.Codex.CodexPages.ToDictionary(p => p.ID);

    internal static CodexPageRules Page(string id) =>
        Pages.TryGetValue(id, out var page) ? page : throw new KeyNotFoundException($"no codex page '{id}'");

    /// <summary>True while a Hot Cargo page is in the How to Play list and the player did not open it yet.</summary>
    internal static bool AnyUnread() => All.Any(h => Pages.TryGetValue(h.PageId, out var p) && p.IsVisible && !p.IsSeen);

    /// <summary>Spec 722: tutorials off in the game settings means no Hot Cargo pages either.</summary>
    internal static bool TutorialsOn(Run run) => Settings.ShowTutorials && run.Mode.ShowTutorials;

    /// <summary>UI frame. Shows each page whose condition holds.</summary>
    internal static void OnFrame(Run run)
    {
        if (!_validated) { _validated = true; Errors.Guard("onboarding pages", Validate); }
        if (!TutorialsOn(run) || App.Clock.Time.Seconds < _nextCheck) return;
        _nextCheck = App.Clock.Time.Seconds + 0.25;
        foreach (var hint in All)
        {
            var page = Page(hint.PageId);
            if (page.IsShown) continue;
            bool due = false;
            Errors.Guard($"hint {hint.PageId}", () => due = hint.Due(run));
            if (!due) continue;
            page.IsShown = true;
            Shown[hint.PageId] = Shown.GetValueOrDefault(hint.PageId) + 1;
            Log.Info($"hint shown: {hint.PageId}");
        }
    }

    /// <summary>
    /// Spec 724, 725: each trigger has a page, each Hot Cargo page has a trigger, and the Hot Cargo pages come before
    /// the vanilla pages in the codex list (the AddBase action with Index 0 in mod/onboarding/actions.rules).
    /// </summary>
    static void Validate()
    {
        var problems = new List<string>();
        problems.AddRange(All.Where(h => !Pages.ContainsKey(h.PageId)).Select(h => $"trigger '{h.PageId}' has no codex page"));
        var list = GameApp.Rules.Codex.CodexPages;
        problems.AddRange(list.Where(p => IsHotCargo(p) && All.All(h => h.PageId != p.ID)).Select(p => $"page '{p.ID}' has no trigger"));
        int firstVanilla = Array.FindIndex(list, p => !IsHotCargo(p));
        int lastHotCargo = Array.FindLastIndex(list, IsHotCargo);
        if (firstVanilla >= 0 && lastHotCargo > firstVanilla) problems.Add($"page '{list[lastHotCargo].ID}' comes after vanilla pages; add it with AddBase and Index = 0");
        foreach (var page in list.Where(IsHotCargo))
            foreach (string key in page.Entries.Select(e => e.TextKey?.Key).Prepend(page.TitleKey.Key).Append(page.TabNameKey.Key).OfType<string>())
                if (!Strings.TryGetText(key, out _)) problems.Add($"page '{page.ID}' uses the missing string '{key}'");
        if (problems.Count > 0) throw new InvalidDataException(string.Join("; ", problems));
        Log.Info($"onboarding: {All.Count} Hot Cargo pages, first in the codex list");
    }
}

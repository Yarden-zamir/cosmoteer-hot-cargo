using System.Runtime.CompilerServices;
using Cosmoteer.Game;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.AI.CommsModules;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Ui;

/// <summary>
/// One option row of a hail page (spec 591): the text, an optional slider that belongs to the option, and what a click does.
/// Toggle rows select an option of the page's multiselect draft (<see cref="HailPage.Multi"/>): Option is the multiselect option ID,
/// Selected and Value come from the draft. Other rows are shared choices of <see cref="HailPage.Shared"/>: Option is the shared choice
/// option for the slider value that the provider read with <see cref="CommsHail.SliderValue"/>.
/// </summary>
public sealed record HailRow(string Key, string Text, int Option, SliderSpec? Slider = null, bool Active = true, string? Tooltip = null,
    bool Toggle = false, bool Selected = false, int Value = 0);

/// <summary>
/// A page of the comms dialogue tab: the vanilla text and buttons, then the lines, option rows and buttons of each Hot Cargo provider.
/// Rows show above the buttons.
/// </summary>
public sealed class HailPage : AIDialogueModule.DialogueExchange
{
    /// <summary>Adds a paragraph under the text that is there.</summary>
    public void AddText(string xml) => PrimaryDialogue = PrimaryDialogue.Length == 0 ? xml : PrimaryDialogue + "\n\n" + xml;

    /// <summary>
    /// The shared choice domain and page serial of the page's choice buttons and action rows (spec 600 and later), or null when no
    /// provider set it. The first provider that sets it owns the page's shared options.
    /// </summary>
    public (string Domain, int Serial)? Shared { get; set; }

    /// <summary>The multiselect domain and page serial of the page's toggle rows (spec 590 and later), or null.</summary>
    public (string Domain, int Serial)? Multi { get; set; }

    public List<HailRow> Rows { get; } = [];

    public void AddRow(HailRow row)
    {
        if (row.Toggle ? Multi is null : Shared is null)
            throw new InvalidOperationException($"set HailPage.{(row.Toggle ? "Multi" : "Shared")} before adding the row {row.Key}");
        Rows.Add(row);
    }

    /// <summary>A choice that goes through the shared choice input of <see cref="Shared"/>: co-op players see hovers and can vote.</summary>
    /// <param name="submit">The choice submits the page's multiselect draft: Enter picks the first active one (UX V15).</param>
    public HailChoice AddChoice(string xml, int option, bool active = true, string? tooltip = null, bool close = false, bool submit = false)
    {
        if (Shared is null) throw new InvalidOperationException("set HailPage.Shared before adding a shared choice");
        var b = new HailChoice(option) { Text = xml, Tooltip = tooltip, SelfInputActive = active, CloseOnClick = close, Submit = submit };
        // The comms tab replaces this click with the shared choice panel's. A click that still lands here is a bug: show it, never do nothing.
        b.OnClick = (_, _) => Errors.Report("hail choice", $"choice {option} of {Shared.Value.Domain} has no shared choice panel");
        Buttons.Add(b);
        return b;
    }

    /// <summary>A local button: navigation inside the page, no sim change.</summary>
    public AIDialogueModule.DialogueButton AddButton(string xml, Action? onClick, bool active = true, string? tooltip = null, bool close = false)
    {
        var b = new AIDialogueModule.DialogueButton { Text = xml, Tooltip = tooltip, SelfInputActive = active, CloseOnClick = close };
        if (onClick is not null) b.OnClick = (_, _) => Errors.Guard("hail button", onClick);
        Buttons.Add(b);
        return b;
    }
}

/// <summary>A button that stands for a shared choice option. CommsHail sets its click to the shared choice panel.</summary>
public sealed class HailChoice(int option) : AIDialogueModule.DialogueButton
{
    public int Option { get; } = option;
    public bool Submit { get; init; }
}

/// <summary>
/// One area's options in ship comms. <see cref="Answers"/> runs in the sim (the game asks it when it checks open comms and hails),
/// so it reads sim state only and must give the same answer on every machine. <see cref="Build"/> runs in the UI each frame.
/// </summary>
public interface IHailProvider
{
    bool Answers(Ship caller, Ship callee);
    void Build(Ship caller, Ship callee, HailPage page);
}

/// <summary>
/// The one Hot Cargo hook into ship comms (spec 560). Providers register at boot. A ship with options from any provider answers
/// calls and shows a dialogue tab; the page starts with the vanilla dialogue (surrender, bounty), then each provider adds its part.
/// Choice buttons and action rows go through the shared choice panel and input (co-op hovers and votes); toggle rows change the
/// shared multiselect draft through its input. This hook changes no sim state.
/// Limits: one shared choice domain and one multiselect domain per page, providers in registration order. Revisit when a page needs
/// choices of two domains or an order rule.
/// </summary>
public static class CommsHail
{
    public static readonly List<IHailProvider> Providers = [];

    /// <summary>Slider values by callee and slider key. Local UI state, never saved and never sent except inside an input.</summary>
    static readonly Dictionary<(ObjectID, string), int> Values = new();
    static readonly ConditionalWeakTable<CommDialogueTab, OptionRows> Rows = new();
    static readonly ConditionalWeakTable<CommDialogueTab, SharedState> Panels = new();

    /// <summary>
    /// The shared choice panel of a dialogue tab and the lock after a first pick, like the event window. The panel is null until the
    /// tab shows a page with shared choices.
    /// </summary>
    sealed class SharedState(Halfling.Gui.ScrollBox pageBox)
    {
        public readonly Halfling.Gui.ScrollBox PageBox = pageBox;
        public SharedChoice.SharedChoicePanel? Panel;
        public string? Domain;
        public int LockedSerial;
        public double LockedUntil;
    }

    /// <summary>Real seconds that the buttons stay locked after a first pick: longer than the MP input delay, like the event window.</summary>
    const double LockSeconds = 3.0;

    public static int SliderValue(Ship callee, SliderSpec spec) =>
        spec.Snap(Values.TryGetValue((callee.UniqueID, spec.Key), out int v) ? v : spec.Default);

    /// <summary>Sets a slider value as if the player moved it. For autotests.</summary>
    public static void SetSliderValue(Ship callee, string key, int value) => Values[(callee.UniqueID, key)] = value;

    static bool AnyAnswers(Ship caller, Ship callee)
    {
        if (Run.Current is null || caller.Sim is null || !caller.IsHumanPlayerShip || callee.IsHumanPlayerShip) return false;
        foreach (var p in Providers)
        {
            try { if (p.Answers(caller, callee)) return true; }
            catch (Exception ex) { Errors.Report($"hail {p.GetType().Name}", ex.Message, ex); }
        }
        return false;
    }

    /// <summary>The page for a caller and callee, or null when no provider has options. Used by the dialogue tab and the autotests.</summary>
    public static HailPage? Page(Ship caller, Ship callee, AIDialogueModule.DialogueExchange? vanilla)
    {
        if (!AnyAnswers(caller, callee)) return null;
        var page = new HailPage();
        if (vanilla is not null)
        {
            page.PrimaryDialogue = vanilla.PrimaryDialogue;
            page.OverridePrimaryDialogueIcon = vanilla.OverridePrimaryDialogueIcon;
            page.Buttons.AddRange(vanilla.Buttons);
        }
        foreach (var p in Providers)
        {
            try { if (p.Answers(caller, callee)) p.Build(caller, callee, page); }
            catch (Exception ex) { Errors.Report($"hail page {p.GetType().Name}", ex.Message, ex); }
        }
        return page;
    }

    [HarmonyPatch(typeof(ShipAI), nameof(ShipAI.WillAnswerCallFrom))]
    static class AnswerPatch
    {
        static void Postfix(ShipAI __instance, Ship caller, ref bool __result)
        {
            if (!__result && !__instance.Ship.Ftl.IsPreparingToJump && AnyAnswers(caller, __instance.Ship)) __result = true;
        }
    }

    [HarmonyPatch(typeof(ShipAI), nameof(ShipAI.WillProvideDialogue))]
    static class DialoguePatch
    {
        static void Postfix(ShipAI __instance, Ship caller, ref bool __result)
        {
            if (!__result && AnyAnswers(caller, __instance.Ship)) __result = true;
        }
    }

    [HarmonyPatch(typeof(ShipAI), nameof(ShipAI.GetDialogueExchange))]
    static class ExchangePatch
    {
        static void Postfix(ShipAI __instance, Ship caller, ref AIDialogueModule.DialogueExchange? __result)
        {
            if (Page(caller, __instance.Ship, __result) is { } page) __result = page;
        }
    }

    /// <summary>Every dialogue tab gets an option row list above its buttons, empty until a page has rows.</summary>
    [HarmonyPatch(typeof(CommDialogueTab), MethodType.Constructor, typeof(Halfling.Gui.ScrollBox), typeof(GameRoot), typeof(Ship), typeof(Ship), typeof(CommWindow))]
    static class RowsPatch
    {
        static void Postfix(CommDialogueTab __instance, Halfling.Gui.ScrollBox pageBox) => Errors.Guard("hail rows", () =>
        {
            var rows = new OptionRows();
            var tab = __instance;
            rows.Clicked += view => Errors.Guard("hail row", () => RowClicked(tab, view));
            rows.ValueChanged += (view, value) => Errors.Guard("hail row value", () => RowValue(tab, view, value));
            pageBox.Children.Insert(pageBox.Children.IndexOf(__instance._dialogueButtonsBox), rows.Box);
            Rows.AddOrUpdate(__instance, rows);
            // A Hot Cargo page has more rows than the vanilla surrender page (option rows, co-op bar, buttons), so the window grows
            // with the page (ShowRows), the rows stack from the top, and a page that is still too long scrolls (spec 816).
            if (__instance._currentDialogue is HailPage && __instance._window is { } window)
            {
                window.Size = new Halfling.Geometry.Vector2(Math.Max(window.Size.X, 820f), Math.Max(window.Size.Y, 720f));
                pageBox.ScrollBarController.ShowVertical = Halfling.Gui.ScrollBarShowMode.WhenNeeded;
                pageBox.Children.LayoutAlgorithm = Halfling.Gui.LayoutAlgorithms.StretchTopToBottom;
                pageBox.Children.BorderPadding = new Halfling.Gui.Borders(40f, 30f, 40f, 30f);
                pageBox.Children.WidgetPadding = new Halfling.Geometry.Vector2(12f);
            }
            Panels.AddOrUpdate(__instance, new SharedState(pageBox));
            ShowRows(__instance, rows);
            UpdateShared(__instance);
        });
    }

    /// <summary>Shows the page's option rows each frame, and sends the draft values that wait for the throttle.</summary>
    [HarmonyPatch(typeof(CommDialogueTab), nameof(CommDialogueTab.OnUpdatingUIState))]
    static class RowsFramePatch
    {
        static void Postfix(CommDialogueTab __instance) => Errors.Guard("hail rows", () =>
        {
            if (Rows.TryGetValue(__instance, out var rows)) ShowRows(__instance, rows);
            UpdateShared(__instance);
            MultiChoice.DraftSender.Flush();
        });
    }

    static void ShowRows(CommDialogueTab tab, OptionRows rows)
    {
        var page = tab._currentDialogue as HailPage;
        // A listener sees the shared draft but cannot change it (spec 1134).
        bool listening = SharedChoice.ListenIn.SpeakerOf(tab._window) is not null;
        var views = new List<OptionRowView>();
        if (page is not null)
            foreach (var r in page.Rows)
            {
                int value = r.Slider is not { } spec ? 0 : r.Toggle && r.Selected ? spec.Snap(r.Value) : SliderValue(tab.Callee, spec);
                bool readOnly = listening && r.Toggle;
                views.Add(new OptionRowView(r.Key, r.Text, r.Active && !readOnly, r.Tooltip, r.Toggle, r.Selected, r.Slider, value, SliderActive: !readOnly && (!r.Toggle || r.Selected)));
            }
        rows.Show(views);
        if (page is not null && tab._window is { Parent: { } screen } window && Panels.TryGetValue(tab, out var shared)) Fit(window, shared.PageBox, screen.RectProvider.ScreenRect);
    }

    /// <summary>
    /// Spec 816: the window grows until the whole page fits (the measured contents of the page box), up to Ux.CommsMaxScreenShare of
    /// the screen height, and moves up so that it stays on the screen. It shrinks only to the cap, so it does not jump while the page changes.
    /// </summary>
    static void Fit(CommWindow window, Halfling.Gui.ScrollBox pageBox, Halfling.Geometry.Rect screen)
    {
        float content = pageBox.ScrollBarController.ContentsBoundsProvider.ContentsBounds.Bottom + pageBox.Children.BorderPadding.Bottom;
        float chrome = window.Size.Y - pageBox.ScreenRect.Height;
        float share = Ux.UxKit.Switches?.CommsMaxScreenShare ?? throw new InvalidOperationException("comms page without Hot Cargo switches");
        // Grow to the page, never shrink, but never above the cap: on a small screen the cap also cuts the starting height.
        float wanted = Math.Min(share * screen.Height, Math.Max(window.Size.Y, chrome + content));
        if (wanted != window.Size.Y) window.Size = new Halfling.Geometry.Vector2(window.Size.X, wanted);
        var rect = window.ScreenRect;
        if (rect.Bottom > screen.Bottom) window.ScreenRect = rect with { Y = Math.Max(screen.Y, screen.Bottom - rect.Height) };
    }

    static HailRow? RowOf(CommDialogueTab tab, string key) => (tab._currentDialogue as HailPage)?.Rows.FirstOrDefault(r => r.Key == key);

    static void RowClicked(CommDialogueTab tab, OptionRowView view)
    {
        if (Run.Current is not { } run || tab._currentDialogue is not HailPage page || RowOf(tab, view.Key) is not { } row) return;
        if (row.Toggle)
        {
            if (page.Multi is not { } multi || SharedChoice.ListenIn.SpeakerOf(tab._window) is not null) return;
            int value = row.Slider is { } spec ? SliderValue(tab.Callee, spec) : 0;
            MultiChoice.DraftSender.Toggle(run, multi.Domain, multi.Serial, row.Option, !row.Selected, value);
            return;
        }
        if (page.Shared is not { } s || !Panels.TryGetValue(tab, out var shared) || shared.Panel is null || shared.Domain != s.Domain) return;
        if (shared.LockedSerial == s.Serial && Halfling.App.Clock.Time.Seconds < shared.LockedUntil) return;
        if (!shared.Panel.Click(s.Serial, row.Option)) return;
        shared.LockedSerial = s.Serial;
        shared.LockedUntil = Halfling.App.Clock.Time.Seconds + LockSeconds;
    }

    static void RowValue(CommDialogueTab tab, OptionRowView view, int value)
    {
        if (RowOf(tab, view.Key) is not { } row || row.Slider is null) return;
        Values[(tab.Callee.UniqueID, row.Slider.Key)] = value;
        if (!row.Toggle || !row.Selected || tab._currentDialogue is not HailPage { Multi: { } multi }) return;
        MultiChoice.DraftSender.Value(multi.Domain, multi.Serial, row.Option, value);
    }

    /// <summary>
    /// Wires the choice buttons of a shared page to its panel: the click goes through the panel, the panel draws hovers and votes,
    /// and a first pick locks the buttons until the result arrives or LockSeconds pass. A page without shared choices hides the panel.
    /// </summary>
    static void UpdateShared(CommDialogueTab tab)
    {
        if (Run.Current is not { } run || !Panels.TryGetValue(tab, out var shared)) return;
        var page = tab._currentDialogue as HailPage;
        var buttons = tab._dialogueButtonsBox.Children;
        // A listener only votes (spec 1134): vanilla buttons and hail lines are not shared choices, so they are off.
        string? speaker = SharedChoice.ListenIn.SpeakerOf(tab._window);
        if (speaker is not null && tab._currentDialogue is { } shown)
            for (int i = 0; i < shown.Buttons.Count && i < buttons.Count; i++)
                if (shown.Buttons[i] is not HailChoice) buttons[i].SelfInputActive = false;
        if (page?.Shared is not { } s) { shared.Panel?.Hide(buttons); return; }
        // The page of an open tab changes: a hail line opens an event page in the same tab. The first page with shared choices,
        // or a page of another domain, gets a new panel in place of the old one.
        if (shared.Panel is null || shared.Domain != s.Domain)
        {
            var box = shared.PageBox.Children;
            if (shared.Panel is not null) box.Remove(shared.Panel.Bar);
            shared.Panel = new SharedChoice.SharedChoicePanel(run, s.Domain) { ListeningTo = speaker };
            shared.Domain = s.Domain;
            shared.LockedUntil = 0;
            box.Insert(box.IndexOf(tab._dialogueButtonsBox), shared.Panel.Bar);
        }
        var panel = shared.Panel;
        bool locked = shared.LockedSerial == s.Serial && Halfling.App.Clock.Time.Seconds < shared.LockedUntil;
        var slots = new List<SharedChoice.OptionSlot>();
        for (int i = 0; i < page.Buttons.Count && i < buttons.Count; i++)
        {
            if (page.Buttons[i] is not HailChoice choice) continue;
            var widget = buttons[i];
            if (locked) widget.SelfInputActive = false;
            slots.Add(new SharedChoice.OptionSlot(widget, choice.Option));
            // A vote (vote mode, or any click of a listener) is not a pick: the window stays open, so the voter sees the votes.
            if (speaker is not null || SharedChoice.SharedChoices.Voting(run)) choice.CloseOnClick = false;
            choice.OnClick = (_, _) => Errors.Guard("hail choice", () =>
            {
                if (!panel.Click(s.Serial, choice.Option)) return;
                shared.LockedSerial = s.Serial;
                shared.LockedUntil = Halfling.App.Clock.Time.Seconds + LockSeconds;
            });
        }
        if (Rows.TryGetValue(tab, out var rows))
            foreach (var (button, view) in rows.Shown)
                if (!view.Toggle && RowOf(tab, view.Key) is { } row)
                {
                    if (locked) button.SelfInputActive = false;
                    slots.Add(new SharedChoice.OptionSlot(button, row.Option));
                }
        panel.Update(s.Serial, slots);
    }

    /// <summary>Unlocks the buttons of every tab with this page, when its result arrived. Called by providers after a result changes.</summary>
    public static void Unlock(string domain, int serial)
    {
        foreach (var (_, shared) in Panels)
            if (shared.Domain == domain && shared.LockedSerial == serial) shared.LockedUntil = 0;
    }

    /// <summary>The dialogue tabs of every open comms window. For autotests.</summary>
    internal static List<CommDialogueTab> OpenTabs() => Rows.Where(kv => kv.Value.Box.Root is not null).Select(kv => kv.Key).ToList();

    /// <summary>The dialogue tabs of a comms window.</summary>
    internal static IEnumerable<CommDialogueTab> TabsOf(CommWindow window) => Rows.Select(kv => kv.Key).Where(t => t._window == window).ToList();

    /// <summary>The option rows of a dialogue tab, or null before the tab has them. For the dialogue keys.</summary>
    internal static OptionRows? RowsOf(CommDialogueTab tab) => Rows.TryGetValue(tab, out var rows) ? rows : null;

    /// <summary>The open dialogue tab with this callee, or null. For autotests.</summary>
    internal static CommDialogueTab? OpenTab(Ship callee)
    {
        foreach (var (tab, rows) in Rows)
            if (tab.Callee == callee && rows.Box.Root is not null) return tab;
        return null;
    }

    /// <summary>
    /// Spec 816, for autotests: the number of open comms windows with a Hot Cargo page, and the number of them that fit: the window is
    /// on the screen, and it shows the whole page or its vertical scroll bar.
    /// </summary>
    internal static (int Open, int Fit) PageFit()
    {
        int open = 0, fit = 0;
        foreach (var (tab, shared) in Panels)
        {
            var box = shared.PageBox;
            if (box.Root is null || tab._currentDialogue is not HailPage || tab._window is not { Parent: { } parent } window) continue;
            open++;
            var screen = parent.RectProvider.ScreenRect;
            var rect = window.ScreenRect;
            bool onScreen = rect.Y >= screen.Y - 1 && rect.Bottom <= screen.Bottom + 1;
            float content = box.ScrollBarController.ContentsBoundsProvider.ContentsBounds.Bottom + box.Children.BorderPadding.Bottom;
            bool whole = content <= box.ScreenRect.Height + 1;
            if (onScreen && (whole || box.VerticalScrollBar.SelfActive)) fit++;
            else Log.Info($"comms page does not fit: window {rect}, screen {screen}, content {content:0}, box {box.ScreenRect.Height:0}, scroll bar {box.VerticalScrollBar.SelfActive}");
        }
        return (open, fit);
    }

    /// <summary>Scrolls the page of every open comms tab with a Hot Cargo page by this amount (down is positive). Returns the count. For autotests.</summary>
    internal static int ScrollOpenPages(float amount)
    {
        int count = 0;
        foreach (var (tab, shared) in Panels)
        {
            if (shared.PageBox.Root is null || tab._currentDialogue is not HailPage) continue;
            shared.PageBox.ScrollBarController.ScrollVertical(amount);
            count++;
        }
        return count;
    }

    /// <summary>The option rows of an open dialogue tab with this callee, or null. For autotests.</summary>
    internal static OptionRows? OpenRows(Ship callee)
    {
        foreach (var (tab, rows) in Rows)
            if (tab.Callee == callee && rows.Box.Root is not null) return rows;
        return null;
    }
}

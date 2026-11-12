using System.Runtime.CompilerServices;
using Cosmoteer.Game;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.AI.CommsModules;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Ui;

/// <summary>A page of the comms dialogue tab: the vanilla text and buttons, then the lines and buttons of each Hot Cargo provider, and an optional slider.</summary>
public sealed class HailPage : AIDialogueModule.DialogueExchange
{
    /// <summary>At most one slider per page. The first provider that sets it wins.</summary>
    public SliderSpec? Slider { get; set; }

    /// <summary>Adds a paragraph under the text that is there.</summary>
    public void AddText(string xml) => PrimaryDialogue = PrimaryDialogue.Length == 0 ? xml : PrimaryDialogue + "\n\n" + xml;

    /// <summary>
    /// The shared choice domain and page serial of the page's choice buttons (spec 600 and later), or null when no provider set it.
    /// The first provider that sets it owns the page's shared options.
    /// </summary>
    public (string Domain, int Serial)? Shared { get; set; }

    /// <summary>A choice that goes through the shared choice input of <see cref="Shared"/>: co-op players see hovers and can vote.</summary>
    public HailChoice AddChoice(string xml, int option, bool active = true, string? tooltip = null, bool close = false)
    {
        if (Shared is null) throw new InvalidOperationException("set HailPage.Shared before adding a shared choice");
        var b = new HailChoice(option) { Text = xml, Tooltip = tooltip, SelfInputActive = active, CloseOnClick = close };
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
/// Choice buttons go through the shared choice panel and input (co-op hovers and votes); this hook changes no sim state.
/// Limits: one slider and one shared choice domain per page, providers in registration order. Revisit when a page needs two
/// sliders, choices of two domains or an order rule.
/// </summary>
public static class CommsHail
{
    public static readonly List<IHailProvider> Providers = [];

    /// <summary>Slider values by callee and slider key. Local UI state, never saved and never sent except inside an input.</summary>
    static readonly Dictionary<(ObjectID, string), int> Values = new();
    static readonly ConditionalWeakTable<CommDialogueTab, SliderRow> Rows = new();
    static readonly ConditionalWeakTable<CommDialogueTab, SharedState> Panels = new();

    /// <summary>The shared choice panel of a dialogue tab and the lock after a first pick, like the event window.</summary>
    sealed class SharedState(SharedChoice.SharedChoicePanel panel, string domain)
    {
        public readonly SharedChoice.SharedChoicePanel Panel = panel;
        public readonly string Domain = domain;
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

    /// <summary>Every dialogue tab gets a slider row above its buttons, hidden until a page has a slider.</summary>
    [HarmonyPatch(typeof(CommDialogueTab), MethodType.Constructor, typeof(Halfling.Gui.ScrollBox), typeof(GameRoot), typeof(Ship), typeof(Ship), typeof(CommWindow))]
    static class SliderRowPatch
    {
        static void Postfix(CommDialogueTab __instance, Halfling.Gui.ScrollBox pageBox) => Errors.Guard("hail slider", () =>
        {
            var row = new SliderRow();
            var callee = __instance.Callee;
            row.Changed += v => { if (__instance._currentDialogue is HailPage { Slider: { } s }) Values[(callee.UniqueID, s.Key)] = v; };
            pageBox.Children.Insert(pageBox.Children.IndexOf(__instance._dialogueButtonsBox), row.Row);
            Rows.AddOrUpdate(__instance, row);
            // A Hot Cargo page has more rows than the vanilla surrender page (slider, co-op bar, up to six buttons), and the tab
            // does not scroll, so the window grows to fit them.
            if (__instance._currentDialogue is HailPage && __instance._window is { } window)
            {
                window.Size = new Halfling.Geometry.Vector2(Math.Max(window.Size.X, 760f), Math.Max(window.Size.Y, 840f));
                pageBox.Children.BorderPadding = new Halfling.Gui.Borders(40f, 30f, 40f, 30f);
                pageBox.Children.WidgetPadding = new Halfling.Geometry.Vector2(12f);
            }
            Show(__instance, row);
            UpdateShared(__instance, pageBox);
        });
    }

    /// <summary>Shows the page's slider, or hides the row on a page without one.</summary>
    [HarmonyPatch(typeof(CommDialogueTab), nameof(CommDialogueTab.OnUpdatingUIState))]
    static class SliderPatch
    {
        static void Postfix(CommDialogueTab __instance) => Errors.Guard("hail slider", () =>
        {
            if (Rows.TryGetValue(__instance, out var row)) Show(__instance, row);
            UpdateShared(__instance, null);
        });
    }

    /// <summary>
    /// Wires the choice buttons of a shared page to its panel: the click goes through the panel, the panel draws hovers and votes,
    /// and a first pick locks the buttons until the result arrives or LockSeconds pass. A page without shared choices hides the panel.
    /// </summary>
    static void UpdateShared(CommDialogueTab tab, Halfling.Gui.ScrollBox? pageBox)
    {
        if (Run.Current is not { } run) return;
        var page = tab._currentDialogue as HailPage;
        var buttons = tab._dialogueButtonsBox.Children;
        if (!Panels.TryGetValue(tab, out var shared))
        {
            if (page?.Shared is not { } first || pageBox is null) return;
            shared = new SharedState(new SharedChoice.SharedChoicePanel(run, first.Domain), first.Domain);
            pageBox.Children.Insert(pageBox.Children.IndexOf(tab._dialogueButtonsBox), shared.Panel.Bar);
            Panels.AddOrUpdate(tab, shared);
        }
        if (page?.Shared is not { } s || s.Domain != shared.Domain) { shared.Panel.Hide(buttons); return; }
        bool locked = shared.LockedSerial == s.Serial && Halfling.App.Clock.Time.Seconds < shared.LockedUntil;
        var slots = new List<SharedChoice.OptionSlot>();
        for (int i = 0; i < page.Buttons.Count && i < buttons.Count; i++)
        {
            if (page.Buttons[i] is not HailChoice choice) continue;
            var widget = buttons[i];
            if (locked) widget.SelfInputActive = false;
            slots.Add(new SharedChoice.OptionSlot(widget, choice.Option));
            choice.OnClick = (_, _) => Errors.Guard("hail choice", () =>
            {
                if (!shared.Panel.Click(s.Serial, choice.Option)) return;
                shared.LockedSerial = s.Serial;
                shared.LockedUntil = Halfling.App.Clock.Time.Seconds + LockSeconds;
            });
        }
        shared.Panel.Update(s.Serial, slots);
    }

    /// <summary>Unlocks the buttons of every tab with this page, when its result arrived. Called by providers after a result changes.</summary>
    public static void Unlock(string domain, int serial)
    {
        foreach (var (_, shared) in Panels)
            if (shared.Domain == domain && shared.LockedSerial == serial) shared.LockedUntil = 0;
    }

    static void Show(CommDialogueTab tab, SliderRow row)
    {
        var spec = (tab._currentDialogue as HailPage)?.Slider;
        row.Row.SelfActive = spec is not null;
        if (spec is not null) row.Show(spec, SliderValue(tab.Callee, spec));
    }

    /// <summary>The slider row of an open dialogue tab with this callee, or null. For autotests.</summary>
    internal static SliderRow? OpenRow(Ship callee)
    {
        foreach (var (tab, row) in Rows)
            if (tab.Callee == callee && row.Row.Root is not null) return row;
        return null;
    }
}

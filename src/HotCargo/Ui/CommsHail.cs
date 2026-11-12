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

    public AIDialogueModule.DialogueButton AddButton(string xml, Action? onClick, bool active = true, string? tooltip = null, bool close = false)
    {
        var b = new AIDialogueModule.DialogueButton { Text = xml, Tooltip = tooltip, SelfInputActive = active, CloseOnClick = close };
        if (onClick is not null) b.OnClick = (_, _) => Errors.Guard("hail button", onClick);
        Buttons.Add(b);
        return b;
    }
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
/// Buttons send MP inputs; this hook changes no sim state.
/// Limit: one slider per page and providers in registration order. Revisit when a page needs two sliders or an order rule.
/// </summary>
public static class CommsHail
{
    public static readonly List<IHailProvider> Providers = [];

    /// <summary>Slider values by callee and slider key. Local UI state, never saved and never sent except inside an input.</summary>
    static readonly Dictionary<(ObjectID, string), int> Values = new();
    static readonly ConditionalWeakTable<CommDialogueTab, SliderRow> Rows = new();

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
            Show(__instance, row);
        });
    }

    /// <summary>Shows the page's slider, or hides the row on a page without one.</summary>
    [HarmonyPatch(typeof(CommDialogueTab), nameof(CommDialogueTab.OnUpdatingUIState))]
    static class SliderPatch
    {
        static void Postfix(CommDialogueTab __instance) => Errors.Guard("hail slider", () =>
        {
            if (Rows.TryGetValue(__instance, out var row)) Show(__instance, row);
        });
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

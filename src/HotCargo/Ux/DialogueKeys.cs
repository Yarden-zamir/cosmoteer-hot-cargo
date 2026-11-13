using System.Runtime.CompilerServices;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Modes.Career.Comms;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Children;
using Halfling.Gui.Components.Graphics;
using Halfling.Input;
using HarmonyLib;
using HotCargo.Events;
using HotCargo.Ui;

namespace HotCargo.Ux;

/// <summary>
/// V15. Keys 1 to 9 pick the options of a dialogue: the comms dialogue tab and the Hot Cargo event window.
/// Option rows (multiselect and action rows, spec 591) come first, then the buttons, in the order they show.
/// A digit on a multiselect row toggles it; Enter (single player) picks the first active submit button of a page with rows.
/// A small number left of each option shows its key. The key calls the same handler as a click, and only for an option
/// that accepts input. The event window comes first, because it holds the game until the player chooses.
/// The event window is not changed: Harmony reads its private buttons and calls its private click handler.
/// Limit: Enter opens the chat in multiplayer, so there only the digits submit.
/// Limits: the digit keys also select part groups (vanilla SHIP_CONTROL binding) while a dialogue is open; that is harmless.
/// A digit typed into the multiplayer chat does not pick an option (the chat field has focus).
/// </summary>
static class DialogueKeys
{
    static readonly ViKey[] Keys = [ViKey.D1, ViKey.D2, ViKey.D3, ViKey.D4, ViKey.D5, ViKey.D6, ViKey.D7, ViKey.D8, ViKey.D9];
    static readonly ConditionalWeakTable<ImageButton, WidgetTextRenderer> Badges = new();
    static readonly List<WeakReference<CommDialogueTab>> CommTabs = [];

    static readonly AccessTools.FieldRef<EventDialog?> OpenEvent = AccessTools.StaticFieldRefAccess<EventDialog?>(AccessTools.Field(typeof(EventDialog), "_open"));
    static readonly AccessTools.FieldRef<EventDialog, LayoutBox<ImageButton, Widget>> EventButtons = AccessTools.FieldRefAccess<EventDialog, LayoutBox<ImageButton, Widget>>("_buttons");
    static readonly Action<EventDialog, int> EventClick = AccessTools.MethodDelegate<Action<EventDialog, int>>(AccessTools.Method(typeof(EventDialog), "OnClicked"));
    static readonly AccessTools.FieldRef<EventDialog, EventPage?> EventPageOf = AccessTools.FieldRefAccess<EventDialog, EventPage?>("_page");

    /// <summary>Options picked by key since start. For autotests.</summary>
    internal static int Picks;

    internal static void OnFrame(GameRoot game)
    {
        if (!UxKit.On(s => s.DialogueKeys) || ChatHasFocus(game)) return;
        int key = Array.FindIndex(Keys, k => App.Keyboard.HotkeyPressed(k));
        if (key >= 0) Pick(key);
        else if (!game.NetManager.IsMultiplayer && App.Keyboard.HotkeyPressed(ViKey.Enter)) Submit();
    }

    /// <summary>Picks the first active submit button of the frontmost dialogue, when that page has option rows. False when none.</summary>
    internal static bool Submit()
    {
        if (OpenEvent() is { Parent: not null } ev)
        {
            if (ev.Rows.Shown.Count() == 0 || EventPageOf(ev) is not { } page) return false;
            var buttons = EventButtons(ev).Children;
            int slot = page.Choices.ToList().FindIndex(c => c.Submit && c.Enabled);
            if (slot < 0 || slot >= buttons.Count || !buttons[slot].IsInputActive) return false;
            Picks++;
            EventClick(ev, slot);
            return true;
        }
        if (ActiveCommTab() is not { } tab || tab._currentDialogue is not Ui.HailPage hail || hail.Rows.Count == 0) return false;
        var commButtons = tab._dialogueButtonsBox.Children;
        for (int i = 0; i < hail.Buttons.Count && i < commButtons.Count; i++)
        {
            if (hail.Buttons[i] is not Ui.HailChoice { Submit: true } || !commButtons[i].IsInputActive) continue;
            Picks++;
            tab.ButtonPressed(i);
            return true;
        }
        return false;
    }

    /// <summary>Picks option <paramref name="index"/> (0-based) of the frontmost dialogue. Returns false when no option takes it.</summary>
    internal static bool Pick(int index)
    {
        if (OpenEvent() is { Parent: not null } ev)
        {
            int rows = ev.Rows.Shown.Count();
            if (index < rows) return Count(ev.Rows.Click(index));
            var buttons = EventButtons(ev).Children;
            index -= rows;
            if (index >= buttons.Count || !buttons[index].IsInputActive) return false;
            Picks++;
            EventClick(ev, index);
            return true;
        }
        if (ActiveCommTab() is not { } tab) return false;
        if (CommsHail.RowsOf(tab) is { } commRows)
        {
            int rows = commRows.Shown.Count();
            if (index < rows) return Count(commRows.Click(index));
            index -= rows;
        }
        var commButtons = tab._dialogueButtonsBox.Children;
        if (index >= commButtons.Count || !commButtons[index].IsInputActive) return false;
        Picks++;
        tab.ButtonPressed(index);
        return true;
    }

    static bool Count(bool picked)
    {
        if (picked) Picks++;
        return picked;
    }

    static CommDialogueTab? ActiveCommTab()
    {
        CommTabs.RemoveAll(w => !w.TryGetTarget(out var t) || t._dialogueButtonsBox.Root is null);
        foreach (var w in CommTabs)
            if (w.TryGetTarget(out var t) && t._dialogueButtonsBox.IsActive) return t;
        return null;
    }

    static bool ChatHasFocus(GameRoot game) =>
        game.NetManager is IChatBoxProvider p && p.ChatBox._textField.FocusController.IsFocused;

    /// <summary>
    /// Shows the key number left of each option button: a second text renderer on the button, drawn in the left padding of
    /// the button box (both button boxes do not clip). The button text stays as the game sets it.
    /// </summary>
    static void Number(IEnumerable<ImageButton> buttons, int first = 0)
    {
        bool on = UxKit.On(s => s.DialogueKeys);
        int i = first - 1;
        foreach (var b in buttons)
        {
            i++;
            if (!Badges.TryGetValue(b, out var badge))
            {
                badge = new WidgetTextRenderer(b)
                {
                    Font = WidgetRules.Instance.ShadowFont,
                    FontSize = 18,
                    Color = WidgetRules.Instance.FameTextColor,
                    HAlignment = HAlignment.Left,
                    VAlignment = VAlignment.Center,
                    OversizeMode = OversizeMode.None,
                    Clip = false,
                    Padding = new Borders(-24f, 0f, 0f, 0f),
                };
                b.Components.Add(badge);
                Badges.AddOrUpdate(b, badge);
            }
            badge.Text = on && i < Keys.Length ? (i + 1).ToString() : "";
        }
    }

    [HarmonyPatch(typeof(CommDialogueTab), MethodType.Constructor, typeof(ScrollBox), typeof(GameRoot), typeof(Cosmoteer.Ships.Ship), typeof(Cosmoteer.Ships.Ship), typeof(CommWindow))]
    static class TrackCommTab
    {
        static void Postfix(CommDialogueTab __instance) => CommTabs.Add(new WeakReference<CommDialogueTab>(__instance));
    }

    [HarmonyPatch(typeof(CommDialogueTab), nameof(CommDialogueTab.OnUpdatingUIState))]
    static class NumberCommButtons
    {
        static void Postfix(CommDialogueTab __instance) => UxKit.Guard("dialogue keys", () =>
        {
            int rows = 0;
            if (CommsHail.RowsOf(__instance) is { } optionRows)
            {
                var shown = optionRows.Shown.Select(r => r.Button).ToList();
                Number(shown);
                rows = shown.Count;
            }
            Number(__instance._dialogueButtonsBox.Children, rows);
        });
    }

    /// <summary>The event view writes no number into the choice text (EventView.ChoiceText), so only the gold number shows.</summary>
    [HarmonyPatch(typeof(EventDialog), "Show")]
    static class NumberEventButtons
    {
        static void Postfix(EventDialog __instance) => UxKit.Guard("dialogue keys", () =>
        {
            var rows = __instance.Rows.Shown.Select(r => r.Button).ToList();
            Number(rows);
            Number(EventButtons(__instance).Children, rows.Count);
        });
    }

    /// <summary>True when a text starts with "N. " (also inside a leading gray tag): a second number next to the gold one.</summary>
    static bool HasNumber(string text, int n) =>
        text.StartsWith($"{n}. ", StringComparison.Ordinal) || text.StartsWith($"<gray>{n}. ", StringComparison.Ordinal);

    /// <summary>Event buttons whose text still starts with its number. For autotests.</summary>
    internal static int EventTextNumbers()
    {
        if (OpenEvent() is not { Parent: not null } ev) throw new InvalidOperationException("no event window is open");
        var buttons = EventButtons(ev).Children;
        int count = 0;
        for (int i = 0; i < buttons.Count; i++)
            if (HasNumber(buttons[i].Text ?? "", i + 1)) count++;
        return count;
    }
}

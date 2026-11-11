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

namespace HotCargo.Ux;

/// <summary>
/// V15. Keys 1 to 9 pick the options of a dialogue: the comms dialogue tab and the Hot Cargo event window.
/// A small number left of each option shows its key. The key calls the same handler as a click, and only for an option
/// that accepts input. The event window comes first, because it holds the game until the player chooses.
/// The event window is not changed: Harmony reads its private buttons and calls its private click handler.
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

    /// <summary>Options picked by key since start. For autotests.</summary>
    internal static int Picks;

    internal static void OnFrame(GameRoot game)
    {
        if (!UxKit.On(s => s.DialogueKeys) || ChatHasFocus(game)) return;
        int key = Array.FindIndex(Keys, k => App.Keyboard.HotkeyPressed(k));
        if (key >= 0) Pick(key);
    }

    /// <summary>Picks option <paramref name="index"/> (0-based) of the frontmost dialogue. Returns false when no option takes it.</summary>
    internal static bool Pick(int index)
    {
        if (OpenEvent() is { Parent: not null } ev)
        {
            var buttons = EventButtons(ev).Children;
            if (index >= buttons.Count || !buttons[index].IsInputActive) return false;
            Picks++;
            EventClick(ev, index);
            return true;
        }
        if (ActiveCommTab() is not { } tab) return false;
        var commButtons = tab._dialogueButtonsBox.Children;
        if (index >= commButtons.Count || !commButtons[index].IsInputActive) return false;
        Picks++;
        tab.ButtonPressed(index);
        return true;
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
    static void Number(WidgetLayoutChildren<ImageButton> buttons)
    {
        bool on = UxKit.On(s => s.DialogueKeys);
        for (int i = 0; i < buttons.Count; i++)
        {
            var b = buttons[i];
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
        static void Postfix(CommDialogueTab __instance) => UxKit.Guard("dialogue keys", () => Number(__instance._dialogueButtonsBox.Children));
    }

    /// <summary>
    /// The event view writes "1. " at the start of each choice text (EventView.ChoiceText), so that the number shows without
    /// this feature. With DialogueKeys on, the gold number replaces it: this strips the text number after each refresh.
    /// </summary>
    [HarmonyPatch(typeof(EventDialog), "Show")]
    static class NumberEventButtons
    {
        static void Postfix(EventDialog __instance) => UxKit.Guard("dialogue keys", () =>
        {
            var buttons = EventButtons(__instance).Children;
            if (UxKit.On(s => s.DialogueKeys))
                for (int i = 0; i < buttons.Count; i++) buttons[i].Text = StripNumber(buttons[i].Text ?? "", i + 1);
            Number(buttons);
        });
    }

    /// <summary>Removes a leading "N. " (also inside a leading gray tag). Text without it stays as it is.</summary>
    internal static string StripNumber(string text, int n)
    {
        string plain = $"{n}. ";
        const string gray = "<gray>";
        if (text.StartsWith(plain, StringComparison.Ordinal)) return text[plain.Length..];
        if (text.StartsWith(gray + plain, StringComparison.Ordinal)) return gray + text[(gray.Length + plain.Length)..];
        return text;
    }

    /// <summary>Event buttons whose text still starts with its number. For autotests.</summary>
    internal static int EventTextNumbers()
    {
        if (OpenEvent() is not { Parent: not null } ev) throw new InvalidOperationException("no event window is open");
        var buttons = EventButtons(ev).Children;
        int count = 0;
        for (int i = 0; i < buttons.Count; i++)
            if (StripNumber(buttons[i].Text ?? "", i + 1) != (buttons[i].Text ?? "")) count++;
        return count;
    }
}

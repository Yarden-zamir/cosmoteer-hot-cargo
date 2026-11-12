using System.Runtime.CompilerServices;
using Cosmoteer.Modes.Career.Comms;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Ux;

/// <summary>
/// At stations the "Exchange Dialogue" tab goes last, and the window opens on the first other tab (missions or trade).
/// Hot Cargo gives most stations dialogue options (diplomacy, event hail lines), and vanilla puts that tab first and opens on it.
/// Ships keep the vanilla order: a ship's hail is usually the reason to open comms.
/// Limit: a station event page that waits in the dialogue tab is then one click away. Revisit if players miss station events.
/// </summary>
[HarmonyPatch(typeof(CommWindow), nameof(CommWindow.UpdateTabs))]
static class CommTabOrder
{
    static readonly ConditionalWeakTable<CommWindow, object> Placed = new();

    static void Postfix(CommWindow __instance, bool __result) => Errors.Guard("comm tab order", () =>
    {
        if (!__result || __instance._dialogueTab is not { } dialogue || !Diplomacy.Situation.IsStation(__instance.Callee)) return;
        var list = __instance._tabs.TabsListBox;
        if (list.Children.Count < 2) return;
        // Vanilla moves the dialogue tab back to the front on every update, so it moves to the end every time.
        list.Children.MoveChild(dialogue, list.Children.Count - 1);
        if (Placed.TryGetValue(__instance, out _)) return;
        Placed.Add(__instance, new object());
        if (list.SelectedWidget == dialogue) list.SelectedWidget = list.Children[0];
    });
}

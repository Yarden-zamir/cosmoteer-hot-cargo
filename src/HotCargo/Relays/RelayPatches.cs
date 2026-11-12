using System.Text;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Creative.MPInputs;
using Halfling.Gui;
using Halfling.Network;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Ux;

namespace HotCargo.Relays;

/// <summary>Spec 954: a ready click does nothing while the relays block relay jumps. Runs in lockstep, so every machine drops it.</summary>
[HarmonyPatch(typeof(MPReadyFtlJumpInput), nameof(MPReadyFtlJumpInput.Execute))]
static class ReadyBlockPatch
{
    [HarmonyPriority(Priority.High)]
    static bool Prefix(GameRoot game, MessengerID playerID)
    {
        if (Run.Current is not { } run || run.Game != game || !RelaysArea.Blocks(run)) return true;
        Log.Info("relays: a relay jump is blocked by the lockdown");
        if (playerID == game.NetManager.LocalPlayerID)
        {
            string line = RelaysArea.Text("Blocked" + RelaysArea.ActiveHere(run)!.Kind);
            Run.OnNextFrame(() => UxKit.Guard("relay blocked line", () => game.Gui.ShowAreaName(line)));
        }
        return false;
    }
}

/// <summary>Spec 954: the spin-up window greys out ENGAGE while the relays block relay jumps, and a line under it says why and how out.</summary>
static class JumpWindowPatches
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FtlJumpGui, Button> Lines = new();

    [HarmonyPatch(typeof(FtlJumpGui), MethodType.Constructor, typeof(GameRoot), typeof(GameGui))]
    static class Build
    {
        static void Postfix(FtlJumpGui __instance) => UxKit.Guard("relay jump line", () =>
        {
            if (__instance._jumpBtn.Parent is not LayoutBox column) return;
            var line = new Button { Height = 30f, SelfInputActive = false, SelfActive = false };
            foreach (var r in new[] { line.StateNormalTextRenderer, line.StateDisabledTextRenderer }) r.XmlFormatting = true;
            column.AddChild(line);
            Lines.Add(__instance, line);
        });
    }

    [HarmonyPatch(typeof(FtlJumpGui), "OnUpdatingUIState")]
    static class Frame
    {
        [HarmonyPriority(Priority.Low)]
        static void Postfix(FtlJumpGui __instance)
        {
            if (!__instance.SelfActive || Run.Current is not { } run || run.Game != __instance.Game) return;
            UxKit.Guard("relay jump window", () =>
            {
                bool blocked = RelaysArea.Blocks(run);
                if (blocked) __instance._jumpBtn.SelfInputActive = false;
                if (!Lines.TryGetValue(__instance, out var line)) return;
                line.SelfActive = blocked;
                if (blocked) line.Text = $"<bad>{RelaysArea.Text("Blocked" + RelaysArea.ActiveHere(run)!.Kind)}</bad>";
            });
        }
    }
}

/// <summary>Spec 953: the galaxy map shows a lockdown before the players jump in: a tag under the system name and a tooltip line. UI only.</summary>
static class MapPatches
{
    static Run? For(CareerGameModeManager mode) => Run.Current is { PressureActive: true } run && run.Mode == mode ? run : null;

    /// <summary>The lockdown kind that the map shows for a node: the marked one, or the active one of the current node while it blocks.</summary>
    internal static LockKind Shown(Run run, MapNode node)
    {
        if (RelaysArea.MarkedKind(run, node) is var k and not LockKind.None) return k;
        return node == run.Game.CurrentMapNode && RelaysArea.ActiveHere(run) is { Open: false } a ? a.Kind : LockKind.None;
    }

    /// <summary>The tooltip line of a node with a leading line break, or "". Tests log it.</summary>
    internal static string TooltipLine(Run run, MapNode node)
    {
        var kind = Shown(run, node);
        if (kind == LockKind.None) return "";
        int credits = kind == LockKind.Fee ? RelaysArea.FeeCredits(run) : RelaysArea.BribePrice(run);
        return "\n" + RelaysArea.Format("Tip" + kind + "Fmt", RelaysArea.Cost(run, credits), (int)(run.T.Relays.ClosedSeconds / 60f));
    }

    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.GetMapNodeToolTip))]
    static class Tooltip
    {
        [HarmonyPriority(Priority.Low)]
        static void Postfix(CareerGameModeManager __instance, MapNode node, ref string? __result)
        {
            if (__result is null || For(__instance) is not { } run) return;
            string? result = __result;
            UxKit.Guard("relay tooltip", () => result += TooltipLine(run, node));
            __result = result;
        }
    }

    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.GetMapNodeText))]
    static class NodeText
    {
        static void Postfix(CareerGameModeManager __instance, MapNode node, StringBuilder sb)
        {
            if (For(__instance) is not { } run) return;
            UxKit.Guard("relay map tag", () =>
            {
                var kind = Shown(run, node);
                if (kind != LockKind.None) sb.Append("\n<s16><bad>").Append(RelaysArea.Text("Tag" + kind)).Append("</bad></s16>");
            });
        }
    }
}

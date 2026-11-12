using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Gui;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships.Parts.Ftl;
using Halfling.Gui;
using Halfling.Network;
using Halfling.Serialization;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Ux;

namespace HotCargo.Factions;

/// <summary>
/// Spec 650 to 652: relay jumps and blind jumps. Vanilla has one way to change system: the ships fly to a Hyper-Jump Relay
/// and pay hyperium (GameRoot.DoHumanPlayersFtlJump). A blind jump is the same jump with BlindFuelFactor more fuel, and it keeps
/// less heat. The switch (FactionsState.BlindJump) is sim state, set through BlindJumpInput, so every machine charges the same fuel.
/// </summary>
static class Jumps
{
    /// <summary>
    /// The kind of the jump that the next system switch completes. Set in the JumpToNode prefix, which runs from MP input execution
    /// behind the jump barriers on every machine. Not saved: a save never holds a jump in flight.
    /// </summary>
    static (Run Run, JumpKind Kind)? _pending;

    /// <summary>The kind that DoHumanPlayersFtlJump charged fuel for. JumpToNode runs later, after a barrier, and must not see a newer switch.</summary>
    static JumpKind? _charged;

    /// <summary>Tests: the kind of the next JumpToNode call, for jumps without the FTL animation.</summary>
    internal static JumpKind? TestOverride;

    internal static JumpKind PendingKind(Run run) => _pending is { } p && p.Run == run ? p.Kind : JumpKind.View;

    static JumpKind SwitchKind(Run run) => run.PressureActive && run.State.Factions.BlindJump ? JumpKind.Blind : JumpKind.Relay;

    [HarmonyPatch(typeof(GameRoot), nameof(GameRoot.DoHumanPlayersFtlJump))]
    static class ChargePatch
    {
        static void Prefix(GameRoot __instance)
        {
            if (Run.Current is { } run && run.Game == __instance) _charged = SwitchKind(run);
        }
    }

    /// <summary>Only DoHumanPlayersFtlJump passes doFtlAnimation = true. A false call is a view switch (MPJumpToNodeInput) or a test jump.</summary>
    [HarmonyPatch(typeof(GameRoot), nameof(GameRoot.JumpToNode))]
    static class SwitchPatch
    {
        static void Prefix(GameRoot __instance, MapNode node, bool doFtlAnimation)
        {
            if (Run.Current is not { } run || run.Game != __instance || node == __instance.CurrentMapNode) return;
            var kind = TestOverride ?? (doFtlAnimation ? _charged ?? SwitchKind(run) : JumpKind.View);
            TestOverride = null;
            _charged = null;
            _pending = (run, kind);
            Log.Info($"jump to {node.Name}: {kind}");
        }
    }

    /// <summary>Spec 651: the per-ship fuel of a map jump, the one source of the fuel labels, the spin-up check and the charge.</summary>
    [HarmonyPatch(typeof(FtlManager), nameof(FtlManager.GetTotalFtlMapJumpFuelCost))]
    static class FuelPatch
    {
        static void Postfix(FtlManager __instance, ref int __result)
        {
            if (__result <= 0 || !__instance.Ship.IsHumanPlayerShip || Run.Current is not { } run || run.Game.Sim != __instance.Ship.Sim) return;
            if (SwitchKind(run) == JumpKind.Blind) __result = FactionsModel.Scale(__result, run.T.Factions.BlindFuelFactor);
        }
    }

    /// <summary>Spec 652: a blind jump switch under the ENGAGE button of the spin-up window, with the heat of both kinds in its text and tooltip.</summary>
    [HarmonyPatch(typeof(FtlJumpGui), MethodType.Constructor, typeof(GameRoot), typeof(GameGui))]
    static class GuiPatch
    {
        static void Postfix(FtlJumpGui __instance, GameRoot game) => UxKit.Guard("blind jump button", () =>
        {
            if (__instance._jumpBtn.Parent is not LayoutBox column) return;
            var button = new Button { Height = 30f };
            foreach (var r in new[] { button.StateNormalTextRenderer, button.StateHighlightedTextRenderer, button.StatePressedTextRenderer, button.StateDisabledTextRenderer })
                r.XmlFormatting = true;
            button.ToolTips.TextProvider = new LiveText(() => Run.Current is { } run && run.Game == game ? Tooltip(run) : "");
            button.Clicked += (_, _) =>
            {
                if (Run.Current is { PressureActive: true } run && run.Game == game) game.NetManager.EnqueueInput(new BlindJumpInput(!run.State.Factions.BlindJump));
            };
            column.AddChild(button);
            Buttons.Add(__instance, button);
        });
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FtlJumpGui, Button> Buttons = new();

    [HarmonyPatch(typeof(FtlJumpGui), "OnUpdatingUIState")]
    static class GuiUpdatePatch
    {
        static void Postfix(FtlJumpGui __instance)
        {
            if (!__instance.SelfActive || !Buttons.TryGetValue(__instance, out var button)) return;
            UxKit.Guard("blind jump button", () =>
            {
                bool active = Run.Current is { PressureActive: true } run && run.Game == __instance.Game;
                button.SelfActive = active;
                if (active) button.Text = ButtonText(Run.Current!);
            });
        }
    }

    internal static string ButtonText(Run run) => run.State.Factions.BlindJump
        ? $"<good>{FactionsArea.Format("BlindOnFmt", FactionsArea.CarryOver(run, JumpKind.Blind) * 100f)}</good>"
        : FactionsArea.Format("BlindOffFmt", FactionsArea.CarryOver(run, JumpKind.Relay) * 100f);

    internal static string Tooltip(Run run) => FactionsArea.Format("BlindTipFmt",
        FactionsArea.CarryOver(run, JumpKind.Relay) * 100f, FactionsArea.CarryOver(run, JumpKind.Blind) * 100f, run.T.Factions.BlindFuelFactor);
}

/// <summary>Spec 650: any player switches the next jump between relay and blind. Runs in lockstep on every machine.</summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoBlindJump")]
internal sealed class BlindJumpInput : MPInput
{
    [Serialize] public bool On { get; private set; }

    public BlindJumpInput(bool on) => On = on;

    BlindJumpInput() { }

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false)
    {
        if (Run.Current is not { } run || run.Game != game) return;
        run.State.Factions.BlindJump = On;
        Log.Info($"blind jump {(On ? "on" : "off")} (player {playerIndex})");
    }
}

[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.AllowMPInput))]
static class AllowBlindJumpPatch
{
    static void Postfix(MPInput mpInput, ref bool __result)
    {
        if (mpInput is BlindJumpInput) __result = true;
    }
}

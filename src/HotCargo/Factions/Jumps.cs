using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Gui;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts.Ftl;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Halfling.Gui;
using Halfling.Network;
using Halfling.Random;
using Halfling.Serialization;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Ux;

namespace HotCargo.Factions;

/// <summary>
/// Spec 650 to 653: relay jumps, blind jumps and jumps in combat. Vanilla has one way to change system: the ships fly to a
/// Hyper-Jump Relay, spin up their hyperdrives and pay hyperium (GameRoot.DoHumanPlayersFtlJump). Every jump removes a share of the heat:
/// less when hunters are near the relay at the jump start, more when none are, most for a blind jump. A blind jump costs more fuel,
/// spins up longer and can damage parts on arrival. The switch (FactionsState.BlindJump) is sim state, set through BlindJumpInput.
/// </summary>
static class Jumps
{
    /// <summary>
    /// The jump that the next system switch completes: its kind and the ships that jump. Set in the JumpToNode prefix, which runs from
    /// MP input execution behind the jump barriers on every machine. Not saved: a save never holds a jump in flight.
    /// </summary>
    static (Run Run, JumpKind Kind, ObjectID[] Ships)? _pending;

    /// <summary>The kind fixed when DoHumanPlayersFtlJump charges the fuel. JumpToNode runs later, after a barrier, and must not see a newer switch.</summary>
    static JumpKind? _charged;

    /// <summary>A jump waits for the vanilla save block to clear (spec 653). One waiter at a time.</summary>
    static bool _waiting;

    /// <summary>Tests: the kind of the next JumpToNode call, for jumps without the FTL animation.</summary>
    internal static JumpKind? TestOverride;

    internal static JumpKind PendingKind(Run run) => _pending is { } p && p.Run == run ? p.Kind : JumpKind.View;

    static bool BlindOn(Run run) => run.PressureActive && run.State.Factions.BlindJump;

    /// <summary>Spec 650: hunters (live or in stasis) within RelayWatchRadius of the relay that leads toward the destination.</summary>
    internal static int HuntersNearRelay(Run run, SimRoot sim, MapNode destination)
    {
        if (run.Game.Map.Nodes.GetFirstNodeInPathTo(destination, requireJumpable: true) is not { } first) return 0;
        if (FtlGate.GetGateForDestination(sim, first) is not { } gate) return 0;
        var at = gate.DetWorldCenter;
        float r = run.T.Factions.RelayWatchRadius;
        return SimQueries.TaggedShips(sim, HunterSpawner.HunterTag).Count(h => h.Location.DistanceTo(at) <= r);
    }

    /// <summary>Spec 650: the kind of a jump to the destination if it started now. Sim state only.</summary>
    internal static JumpKind KindNow(Run run, SimRoot sim, MapNode destination) =>
        BlindOn(run) ? JumpKind.Blind : HuntersNearRelay(run, sim, destination) > 0 ? JumpKind.RelayWatched : JumpKind.RelayClear;

    /// <summary>
    /// Spec 653: vanilla drops a jump while game saving is blocked (multi-step explosions, self-destructing parts, jump-in invulnerability),
    /// and greys out ENGAGE. In a fight that is most of the time. Here the jump waits until the block clears, like the vanilla
    /// MPJumpToNodeInput, and the fight goes on meanwhile. The jump kind is fixed when the jump really starts.
    /// </summary>
    [HarmonyPatch(typeof(GameRoot), nameof(GameRoot.DoHumanPlayersFtlJump))]
    static class ChargePatch
    {
        static bool Prefix(GameRoot __instance)
        {
            if (Run.Current is not { } run || run.Game != __instance || __instance.DestinationMapNode.Confirmed is not { } dest || __instance.IsDoingNodeJump) return true;
            if (__instance.IsGameSavingBlocked)
            {
                if (!_waiting) WaitThenJump(run);
                return false;
            }
            _charged = KindNow(run, __instance.Sim, dest);
            JumpStarts++;
            Log.Info($"jump starts to {dest.Name}: {_charged}");
            return true;
        }
    }

    /// <summary>Jumps that started (the vanilla jump ran past its save check). For tests.</summary>
    internal static int JumpStarts;

    static async void WaitThenJump(Run run)
    {
        _waiting = true;
        var game = run.Game;
        Log.Info("jump waits for the save block to clear (combat effects)");
        try
        {
            while (game.IsGameSavingBlocked)
            {
                await game.NetManager.UntilTick();
                if (Run.Current != run || game.DestinationMapNode.Confirmed is null || game.IsDoingNodeJump) return;
            }
        }
        finally { _waiting = false; }
        Errors.Guard("jump after save block", game.DoHumanPlayersFtlJump);
    }

    /// <summary>Only DoHumanPlayersFtlJump passes doFtlAnimation = true. A false call is a view switch (MPJumpToNodeInput) or a test jump.</summary>
    [HarmonyPatch(typeof(GameRoot), nameof(GameRoot.JumpToNode))]
    static class SwitchPatch
    {
        static void Prefix(GameRoot __instance, MapNode node, bool doFtlAnimation, List<Ship>? shipsToJump)
        {
            if (Run.Current is not { } run || run.Game != __instance || node == __instance.CurrentMapNode) return;
            var kind = TestOverride ?? (doFtlAnimation ? _charged ?? KindNow(run, __instance.Sim, node) : JumpKind.View);
            TestOverride = null;
            _charged = null;
            _pending = (run, kind, shipsToJump?.Select(s => s.UniqueID).ToArray() ?? []);
            Log.Info($"jump to {node.Name}: {kind}, {_pending.Value.Ships.Length} ships");
        }
    }

    /// <summary>
    /// Spec 651: after a blind jump, each jumping ship rolls BlindDamageChance. A hit takes BlindDamageFraction of the maximum health
    /// from BlindDamageParts random parts, never below 1 health. The roll is seeded by the ship and the sector index. Runs in the system switch.
    /// </summary>
    internal static void DamageOnArrival(Run run)
    {
        if (_pending is not { } p || p.Run != run) return;
        var t = run.T.Factions;
        var hurt = new List<string>();
        foreach (var id in p.Ships.Order())
        {
            if (run.Game.Sim.Ships.AllShips.FirstOrDefault(s => s.UniqueID.Equals(id)) is not { } ship) continue;
            var rand = new FastRand(0x424C_494E_4444UL ^ (uint)id.GetHashCode(), (ulong)run.State.SectorIndex);
            if (rand.Single() >= t.BlindDamageChance) continue;
            var parts = ship.Parts.OrderBy(x => x.Location.Y).ThenBy(x => x.Location.X).ToList();
            for (int i = 0; i < t.BlindDamageParts && parts.Count > 0; i++)
            {
                var part = parts[rand.Int32(new Halfling.Range<int>(0, parts.Count - 1))];
                part.Health = Math.Max(1, part.Health - (int)(part.Rules.MaxHealth * t.BlindDamageFraction));
            }
            hurt.Add(ship.Metadata.XmlShipName);
            Log.Info($"blind jump damaged {t.BlindDamageParts} parts of {ship.Name}");
        }
        if (hurt.Count == 0) return;
        string line = FactionsArea.Format("BlindDamageFmt", string.Join(", ", hurt));
        Run.OnNextFrame(() => UxKit.Guard("blind damage line", () => { run.Game.Gui.ShowAreaName(line); UxKit.ChatLine(run.Game, line); }));
    }

    /// <summary>
    /// Spec 651: the blind spin-up clock of each human ship, in the sim tick. It runs while the ship prepares a relay jump with all drives on,
    /// and notes when the drives are full (the vanilla spin-up). A drive that goes off (damage, no power) resets the clock.
    /// </summary>
    internal static void OnTick(Run run, SimRoot sim, double dt)
    {
        var s = run.State.Factions;
        if (!BlindOn(run))
        {
            if (s.SpinSeconds.Count > 0) { s.SpinSeconds.Clear(); s.FullAtSeconds.Clear(); }
            return;
        }
        var spinning = new HashSet<ObjectID>();
        foreach (var ship in sim.Ships.HumanPlayerSignificantShips)
        {
            var ftl = ship.Ftl;
            if (!ftl.HasFtlDrives || !ftl.IsPreparingToGateJump || !ftl.All(d => d.IsToggleOn)) continue;
            var id = ship.UniqueID;
            spinning.Add(id);
            float spin = s.SpinSeconds.GetValueOrDefault(id) + (float)dt;
            s.SpinSeconds[id] = spin;
            if (!s.FullAtSeconds.ContainsKey(id) && ftl.All(d => d.IsReadyToJump)) s.FullAtSeconds[id] = spin;
        }
        foreach (var id in s.SpinSeconds.Keys.Where(k => !spinning.Contains(k)).ToList())
        {
            s.SpinSeconds.Remove(id);
            s.FullAtSeconds.Remove(id);
        }
    }

    static (float Spin, float? FullAt) Spin(Run run, Ship ship)
    {
        var s = run.State.Factions;
        return (s.SpinSeconds.GetValueOrDefault(ship.UniqueID), s.FullAtSeconds.TryGetValue(ship.UniqueID, out float at) ? at : null);
    }

    static Run? BlindRunFor(FtlManager ftl) =>
        ftl.Ship.IsHumanPlayerShip && Run.Current is { } run && run.Game.Sim == ftl.Ship.Sim && BlindOn(run) ? run : null;

    /// <summary>Spec 651: with the blind switch on, a ship is ready only after BlindSpinUpFactor × its vanilla spin-up time.</summary>
    [HarmonyPatch(typeof(FtlManager), nameof(FtlManager.IsReadyToJump), MethodType.Getter)]
    static class ReadyPatch
    {
        static void Postfix(FtlManager __instance, ref bool __result)
        {
            if (!__result || BlindRunFor(__instance) is not { } run) return;
            var (spin, fullAt) = Spin(run, __instance.Ship);
            __result = FactionsModel.BlindSpinDone(spin, fullAt, run.T.Factions.BlindSpinUpFactor);
        }
    }

    [HarmonyPatch(typeof(FtlManager), nameof(FtlManager.FtlReadinessProgress), MethodType.Getter)]
    static class ProgressPatch
    {
        static void Postfix(FtlManager __instance, ref float __result)
        {
            if (BlindRunFor(__instance) is not { } run) return;
            var (spin, fullAt) = Spin(run, __instance.Ship);
            __result = FactionsModel.BlindSpinProgress(__result, spin, fullAt, run.T.Factions.BlindSpinUpFactor);
        }
    }

    /// <summary>Spec 651: the per-ship fuel of a map jump, the one source of the fuel labels, the spin-up check and the charge.</summary>
    [HarmonyPatch(typeof(FtlManager), nameof(FtlManager.GetTotalFtlMapJumpFuelCost))]
    static class FuelPatch
    {
        static void Postfix(FtlManager __instance, ref int __result)
        {
            if (__result <= 0 || BlindRunFor(__instance) is not { } run) return;
            __result = FactionsModel.Scale(__result, run.T.Factions.BlindFuelFactor);
        }
    }

    /// <summary>Spec 652: a blind jump switch under the ENGAGE button of the spin-up window, with the heat cut in its text and the risks in its tooltip.</summary>
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
            if (!__instance.SelfActive || Run.Current is not { } run || run.Game != __instance.Game) return;
            UxKit.Guard("jump window", () =>
            {
                var game = __instance.Game;
                // Spec 653: ENGAGE stays active while game saving is blocked. The jump waits for the block in ChargePatch.
                var ready = game.Sim.Ships.LocalPlayerSignificantShips.Where(s => s.Ftl.IsReadyToJump).ToList();
                if (game.DestinationMapNode.Confirmed is { } dest)
                {
                    game.Map.GetTotalFtlJumpFuelCost(ready, dest, out bool canAfford, out _);
                    __instance._jumpBtn.SelfInputActive = canAfford && !game.PlayersReadyToJump.Contains(game.NetManager.LocalPlayerID)
                        && game.Sim.Ships.HumanPlayerSignificantShips.Any(s => s.Ftl.IsReadyToJump);
                }
                if (!Buttons.TryGetValue(__instance, out var button)) return;
                button.SelfActive = run.PressureActive;
                if (button.SelfActive) button.Text = ButtonText(run);
            });
        }
    }

    internal static string ButtonText(Run run)
    {
        if (run.State.Factions.BlindJump) return $"<good>{FactionsArea.Format("BlindOnFmt", FactionsArea.HeatCut(run, JumpKind.Blind) * 100f)}</good>";
        var kind = run.Game.DestinationMapNode.Confirmed is { } dest ? KindNow(run, run.Game.Sim, dest) : JumpKind.RelayClear;
        return FactionsArea.Format(kind == JumpKind.RelayWatched ? "RelayWatchedFmt" : "RelayClearFmt", FactionsArea.HeatCut(run, kind) * 100f);
    }

    internal static string Tooltip(Run run)
    {
        var t = run.T.Factions;
        return FactionsArea.Format("BlindTipFmt",
            FactionsArea.HeatCut(run, JumpKind.RelayWatched) * 100f, FactionsArea.HeatCut(run, JumpKind.RelayClear) * 100f,
            FactionsArea.HeatCut(run, JumpKind.Blind) * 100f, t.BlindFuelFactor, t.BlindSpinUpFactor,
            t.BlindDamageChance * 100f, t.BlindDamageParts, t.BlindDamageFraction * 100f);
    }
}

/// <summary>
/// Spec 650, 651: any player switches the next jump between relay and blind. Runs in lockstep on every machine.
/// Switching to blind turns the drives of the human ships off for one tick, so a spin-up starts again and takes the blind time.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoBlindJump")]
internal sealed class BlindJumpInput : MPInput
{
    [Serialize] public bool On { get; private set; }

    public BlindJumpInput(bool on) => On = on;

    BlindJumpInput() { }

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false)
    {
        if (Run.Current is not { } run || run.Game != game || game.Sim is not { } sim) return;
        bool was = run.State.Factions.BlindJump;
        run.State.Factions.BlindJump = On;
        if (On && !was)
            foreach (var ship in sim.Ships.HumanPlayerSignificantShips)
                foreach (var drive in ship.Ftl)
                    drive.IsToggleOn = false;
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

using System.Reflection;
using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Modes;
using Cosmoteer.Modes.Career;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Commands;
using Cosmoteer.Ships.Parts.Crew;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Tech;

namespace HotCargo.Autonomy;

/// <summary>Spec 1050 to 1061: which ships are autonomous, which parts their Computers man, and the vanilla rules they bend.</summary>
internal static class Autonomous
{
    internal static readonly ID<PartRules> ComputerId = new("yarden_zamir.computer");
    internal static readonly ID<PartRules> FunnelId = new("yarden_zamir.funnel");
    internal static readonly ID<PartRules> TargetedReactorId = new("yarden_zamir.targeted_reactor");

    static bool IsComputer(Part p) => p.Rules.ID == ComputerId;

    /// <summary>A part that gives command points: a control room or a Computer.</summary>
    static bool GivesCommand(Part p) => p.Components.OfType<CommandProvider>().Any();

    static bool Works(Part computer) => computer.Components.OfType<CommandProvider>().Any(c => c.IsOperational);

    /// <summary>Spec terms: a human player ship with a Computer and no other part that gives command points.</summary>
    internal static bool IsAutonomous(Ship ship)
    {
        if (!ship.IsHumanPlayerShip) return false;
        bool computer = false;
        foreach (var part in ship.Parts)
        {
            if (IsComputer(part)) computer = true;
            else if (GivesCommand(part)) return false;
        }
        return computer;
    }

    internal static bool HasWorkingComputer(Ship ship)
    {
        foreach (var part in ship.Parts)
            if (IsComputer(part) && Works(part)) return true;
        return false;
    }

    /// <summary>Spec terms: a human ship that is not autonomous and has crew (a working control room ship).</summary>
    internal static bool IsCrewed(Ship ship) => ship.IsHumanPlayerShip && !ship.IsJunk && ship.Crew.Assigned.Count > 0 && !IsAutonomous(ship);

    /// <summary>The owner's first crewed ship (lowest ID) in the same system, or null.</summary>
    internal static Ship? FirstCrewed(Ship self) => self.Sim?.Ships.HumanPlayerShips
        .Where(s => s != self && s.Metadata.PlayerIndex == self.Metadata.PlayerIndex && IsCrewed(s)).MinBy(s => s.UniqueID);

    internal static Box BoxOf(Part p) => new(p.Rect.X, p.Rect.Y, p.Rect.Width, p.Rect.Height);

    /// <summary>Spec 1051: a part with a vanilla crew slot that gives no command points.</summary>
    static bool CanBeManned(Part p) => !GivesCommand(p) && p.Components.OfType<PartCrew>().Any();

    /// <summary>Spec 1051: the part that a working Computer mans, or null.</summary>
    internal static Part? MannedBy(Part computer)
    {
        if (!IsComputer(computer) || !Works(computer) || computer.Ship is not { } ship) return null;
        var parts = new List<Part>();
        int? index = AutonomyModel.MannedPart(BoxOf(computer), computer.Rotation, cell =>
        {
            var p = ship.Parts[new IntVector2(cell.X, cell.Y), PartRectType.Normal];
            if (p is null || !CanBeManned(p)) return null;
            parts.Add(p);
            return parts.Count - 1;
        });
        return index is { } i ? parts[i] : null;
    }

    /// <summary>Spec 1051: whether a working Computer next to this part mans it. Reads sim state only, so every machine agrees at once.</summary>
    internal static bool IsManned(Part part)
    {
        if (part.Ship is not { } ship || !CanBeManned(part)) return false;
        foreach (var adj in ship.Parts.GetAdjacentParts(part.Rect, PartRectType.Normal, AdjacencyFlags.Sides))
            if (IsComputer(adj) && MannedBy(adj) == part) return true;
        return false;
    }

    /// <summary>
    /// Spec 1051: the crew slot that a Computer fills: the first PartCrew of the part it mans (component order). Null when no
    /// working Computer mans the part.
    /// </summary>
    internal static PartCrew? ServedSlot(Part part) => IsManned(part) ? part.Components.OfType<PartCrew>().FirstOrDefault() : null;

    static int ComputerCrew(PartCrew slot) => slot.Part is { } p && ServedSlot(p) == slot ? AutonomyModel.ComputerCrew : 0;

    // ---- Served slot changes: vanilla toggles only re-read PartCrew when it raises ToggleChanged, and vanilla raises it only when
    // the full crew arrives or one leaves. A served slot turns on one crew member earlier, so the tick raises it on each change.

    static readonly ConditionalWeakTable<Ship, Dictionary<PartCrew, bool>> SlotsBefore = new();
    static readonly FieldInfo ToggleChangedField = AccessTools.Field(typeof(PartCrew), nameof(PartCrew.ToggleChanged))
        ?? throw new MissingFieldException(nameof(PartCrew), nameof(PartCrew.ToggleChanged));

    /// <summary>
    /// Spec 1051, 1061. Each sim tick: raises ToggleChanged on each crew slot that a Computer starts or stops serving, and on each
    /// served slot whose crewed state changed; gives a new autonomous ship Auto on. Ships and parts go in sim order. The table only
    /// remembers what was announced; the toggles read the sim, so a loaded game needs no state.
    /// </summary>
    internal static void Tick(SimRoot sim)
    {
        for (int i = 0; i < sim.Ships.Count; i++)
        {
            var ship = sim.Ships[i];
            if (!ship.IsHumanPlayerShip) continue;
            var now = new Dictionary<PartCrew, bool>();
            foreach (var part in ship.Parts)
                if (IsComputer(part) && MannedBy(part) is { } manned && ServedSlot(manned) is { } slot) now[slot] = slot.IsToggleOn;
            var before = SlotsBefore.GetValue(ship, _ => []);
            var changed = now.Where(kv => !before.TryGetValue(kv.Key, out bool on) || on != kv.Value).Select(kv => kv.Key)
                .Concat(before.Keys.Where(k => !now.ContainsKey(k)))
                .OrderBy(c => c.Part?.Location.X ?? 0).ThenBy(c => c.Part?.Location.Y ?? 0).ToList();
            foreach (var slot in changed) Announce(slot);
            before.Clear();
            foreach (var kv in now) before[kv.Key] = kv.Value;
            if (IsAutonomous(ship) && ShipAi.ShipTactics.Of(ship) is null) ShipAi.ShipTactics.For(ship).Auto = true;
        }
    }

    static void Announce(PartCrew slot)
    {
        if (slot.Part?.Ship is null) return;
        (ToggleChangedField.GetValue(slot) as EventHandler<EventArgs>)?.Invoke(slot, EventArgs.Empty);
    }

    // ---- Patches

    /// <summary>Spec 1051: the Computer counts as one crew member in the slot it serves.</summary>
    [HarmonyPatch(typeof(PartCrew))]
    static class MannedCrewPatch
    {
        [HarmonyPostfix, HarmonyPatch(nameof(PartCrew.IsToggleOn), MethodType.Getter)]
        static void On(PartCrew __instance, ref bool __result)
        {
            if (!__result && ComputerCrew(__instance) is var c and > 0)
                __result = AutonomyModel.CrewMet(__instance._crewJob?.ArrivedCrew.Count ?? 0, __instance.Rules.Crew, c);
        }

        [HarmonyPostfix, HarmonyPatch(nameof(PartCrew.IsToggleOnAnticipated), MethodType.Getter)]
        static void Anticipated(PartCrew __instance, ref bool __result)
        {
            if (!__result && ComputerCrew(__instance) is var c and > 0)
                __result = AutonomyModel.CrewMet(__instance._crewJob?.AssignedCrew.Count ?? 0, __instance.Rules.Crew, c);
        }
    }

    /// <summary>Spec 1051: vanilla crew fill only the rest of a served slot.</summary>
    [HarmonyPatch(typeof(PartCrewJob), nameof(PartCrewJob.DesiredCrew), MethodType.Getter)]
    static class MannedJobPatch
    {
        static void Postfix(PartCrewJob __instance, ref int __result)
        {
            if (__result > 0 && ComputerCrew(__instance._partCrew) is var c and > 0) __result = AutonomyModel.CrewStillWanted(__result, c);
        }
    }

    /// <summary>
    /// Spec 1058: the sim refuses player orders and weapon targets for autonomous ships. While one of these inputs runs, the vanilla
    /// permission check says no for them, so the vanilla code also removes the local copy of the order.
    /// </summary>
    [HarmonyPatch]
    static class RefuseOrdersPatch
    {
        [ThreadStatic] internal static bool Active;

        static IEnumerable<MethodBase> TargetMethods() =>
        [
            AccessTools.Method(typeof(MPEnqueueCommandsInput), nameof(MPEnqueueCommandsInput.Execute)),
            AccessTools.Method(typeof(MPReplaceCommandInput), nameof(MPReplaceCommandInput.Execute)),
            AccessTools.Method(typeof(MPEnqueueTargetsInput), nameof(MPEnqueueTargetsInput.Execute)),
        ];

        static void Prefix(bool force) => Active = !force;

        static void Finalizer() => Active = false;
    }

    [HarmonyPatch(typeof(CommandManager), nameof(CommandManager.CanExecuteCommandsFrom))]
    static class RefusePermissionPatch
    {
        static void Postfix(CommandManager __instance, ref bool __result)
        {
            if (__result && RefuseOrdersPatch.Active && IsAutonomous(__instance.Ship)) __result = false;
        }
    }

    /// <summary>Spec 1058: no order lines, halt or drag orders for autonomous ships in the UI.</summary>
    [HarmonyPatch(typeof(PlayerInputHandler), nameof(PlayerInputHandler.SelectedCommandExecutableShips), MethodType.Getter)]
    static class SelectionPatch
    {
        static void Postfix(ref IEnumerable<Ship> __result) => __result = __result.Where(s => !IsAutonomous(s));
    }

    /// <summary>Spec 1059: a ship with a working Computer is significant without crew.</summary>
    [HarmonyPatch(typeof(SimModeManager), nameof(SimModeManager.IsShipSignificant))]
    static class SignificancePatch
    {
        static void Postfix(Ship ship, ref bool __result)
        {
            if (!__result && ship.IsHumanPlayerShip && !ship.IsJunk && HasWorkingComputer(ship)) __result = true;
        }
    }

    /// <summary>Spec 1059: losing a part, crew or a split keeps the owner while a Computer works.</summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.GetAllegianceForReducedShip))]
    static class AllegiancePatch
    {
        static bool Prefix(Ship ship, ref int? updateCrewAllegiance, ref int __result)
        {
            if (!ship.IsHumanPlayerShip || !HasWorkingComputer(ship)) return true;
            updateCrewAllegiance = null;
            __result = ship.Metadata.PlayerIndex;
            return false;
        }
    }
}

/// <summary>Spec 1062, 1063: cargo text with the game's resource list.</summary>
internal static class Cargo
{
    /// <summary>A resource that a cargo list may hold: a stackable resource that is not tech.</summary>
    internal static bool IsCargoResource(string id) =>
        id.Length > 0 && GameApp.Rules.HasResourceRules(new ID<ResourceRules>(id)) && id != Vaults.TechId.ToString()
        && new ID<ResourceRules>(id).GetResourceRules().IsStackable;

    internal static CargoSettings Decode(string text) => AutonomyModel.Decode(text, IsCargoResource);
}

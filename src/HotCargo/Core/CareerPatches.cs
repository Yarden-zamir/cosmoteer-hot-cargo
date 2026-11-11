using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Modes.Career;
using Cosmoteer.Simulation;
using HarmonyLib;
using Halfling.Serialization.Generic;
using HotCargo.Ui;

namespace HotCargo.Core;

/// <summary>Career lifecycle hooks. Each patch stays thin and calls into Run or the UI.</summary>
[HarmonyPatch(typeof(CareerGameModeManager))]
static class CareerPatches
{
    const string SavePath = "HotCargo";

    [HarmonyPostfix, HarmonyPatch(nameof(CareerGameModeManager.OnGameStarting))]
    static void OnGameStarting(CareerGameModeManager __instance, bool loaded) => Guard("OnGameStarting", () => Run.Start(__instance, loaded));

    [HarmonyPostfix, HarmonyPatch(MethodType.Constructor, typeof(GenericSerialReader))]
    static void LoadConstructor(GenericSerialReader reader) => Guard("load", () =>
    {
        Run.PendingLoadedState = reader.TryReadFromPath<RunState>(SavePath, out var state) ? state : null;
    });

    [HarmonyPostfix, HarmonyPatch("WriteTo")]
    static void WriteTo(CareerGameModeManager __instance, GenericSerialWriter writer)
    {
        if (Run.Current is { } run && run.Mode == __instance) writer.WriteToPath(SavePath, run.State);
    }

    [HarmonyPrefix, HarmonyPatch(nameof(CareerGameModeManager.OnSwitchingSimulation))]
    static void BeforeSwitch(CareerGameModeManager __instance)
    {
        if (Run.Current is { } run && run.Mode == __instance) run.BeforeSystemSwitch();
    }

    [HarmonyPostfix, HarmonyPatch(nameof(CareerGameModeManager.OnSwitchedSimulation))]
    static void AfterSwitch(CareerGameModeManager __instance)
    {
        if (Run.Current is { } run && run.Mode == __instance) run.AfterSystemSwitch();
    }

    [HarmonyPostfix, HarmonyPatch("CreateModeSpecificGui")]
    static void CreateGui(CareerGameModeManager __instance) => Guard("overlay", () => Overlay.Attach(__instance));

    [HarmonyPostfix, HarmonyPatch(nameof(CareerGameModeManager.Update))]
    static void FrameUpdate(CareerGameModeManager __instance)
    {
        if (Run.Current is not { } run || run.Mode != __instance) return;
        if (!run.IsMultiplayer && TunablesFile.ChangedSinceLastRead()) run.ReloadTunables();
        DebugKeys.Handle(run);
        RunEndUi.Update(run);
    }

    [HarmonyPostfix, HarmonyPatch(nameof(CareerGameModeManager.AllowSaveGame), MethodType.Getter)]
    static void AllowSaveGame(CareerGameModeManager __instance, ref bool __result)
    {
        if (Run.Current is { } run && run.Mode == __instance && run.State.Result == RunResult.Lost) __result = false;
    }

    [HarmonyPostfix, HarmonyPatch(nameof(CareerGameModeManager.GetIntegrityHash))]
    static void IntegrityHash(CareerGameModeManager __instance, IntegrityHash iHash)
    {
        if (Run.Current is not { } run || run.Mode != __instance) return;
        iHash.AddHash((int)(run.State.Heat * 1000));
        iHash.AddHash(run.State.SectorIndex);
        iHash.AddHash((int)(run.State.Notoriety * 100));
        iHash.AddHash(run.State.PeakTier);
        // string.GetHashCode is random per process, so it would differ between clients. This hash is stable.
        int tunables = 0;
        foreach (char c in run.State.TunablesHash) tunables = tunables * 31 + c;
        iHash.AddHash(tunables);
    }

    /// <summary>A failure in a lifecycle hook leaves Hot Cargo inactive for this game instead of crashing the game.</summary>
    static void Guard(string what, Action action) => Errors.Guard(what, action);
}

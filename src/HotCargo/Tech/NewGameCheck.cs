using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Halfling.Gui.Dialogs;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Tech;

/// <summary>
/// Spec 1010-1013: a new career needs a starter ship with Tech Vaults for the starting tech.
/// The vanilla career ship check (CareerModeRules.IsShipDesignLegal) runs in the new-game screens and again when the game creates the
/// player ships (CareerGameModeManager.FinishInitNewGame), so one postfix covers every path, like PvpModeRules.RequiredPartCategories.
/// </summary>
[HarmonyPatch]
static class NewGameCheck
{
    /// <summary>Set while a co-op check runs. Co-op runs start with the default tunables (Run.Start), so the check uses them too.</summary>
    [ThreadStatic] static bool _coop;

    /// <summary>The text for the next vanilla "invalid ship" dialog, from the last refused ship. Null when the last check passed.</summary>
    static string? _refusal;

    /// <summary>Test only (autotest "newgamecheck off"): starts a game with a vanilla starter, to build the preset designs (spec 1017).</summary>
    internal static bool Off;

    /// <summary>Tech units that a new run puts on one starter ship at most: all of StartingTechUnits (a single player gets all of it).</summary>
    internal static int RequiredUnits(bool coop) =>
        coop ? new TechTunables().StartingTechUnits : TunablesFile.Read()?.Values.Tech.StartingTechUnits ?? new TechTunables().StartingTechUnits;

    /// <summary>Tech units that the vaults of the design hold.</summary>
    internal static int VaultCapacity(Ship ship) =>
        ship.BlueprintParts.Count(p => p.Rules.ID == Vaults.VaultId) * StartingVault.Capacity(ship.Rules.PartsByID[Vaults.VaultId]);

    [HarmonyPostfix, HarmonyPatch(typeof(CareerModeRules), nameof(CareerModeRules.IsShipDesignLegal))]
    static void Check(Ship ship, ref bool __result)
    {
        if (!__result || Off || !Activation.IsModEnabled()) return;
        int need = RequiredUnits(_coop), have = VaultCapacity(ship);
        if (have >= need) { _refusal = null; return; }
        __result = false;
        int perVault = StartingVault.Capacity(ship.Rules.PartsByID[Vaults.VaultId]);
        _refusal = Strings.FormatText("HotCargoTech/NeedVaultsFmt", need, (need + perVault - 1) / perVault, have);
        Log.Info($"new-game check: {ship.Metadata.PlainShipName} refused, vaults hold {have} of {need} starting tech");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(CareerMPModeConfigGui), nameof(CareerMPModeConfigGui.IsShipLegal))]
    static void CoopScreenStart() => _coop = true;

    [HarmonyFinalizer, HarmonyPatch(typeof(CareerMPModeConfigGui), nameof(CareerMPModeConfigGui.IsShipLegal))]
    static Exception? CoopScreenEnd(Exception? __exception) { _coop = false; return __exception; }

    [HarmonyPrefix, HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.FinishInitNewGame))]
    static void InitStart(CareerGameModeManager __instance) => _coop = __instance.Game.NetManager.IsMultiplayer;

    [HarmonyFinalizer, HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.FinishInitNewGame))]
    static Exception? InitEnd(Exception? __exception) { _coop = false; return __exception; }

    /// <summary>The vanilla dialog only says the ship is invalid. After a vault refusal it says what is missing.</summary>
    [HarmonyPrefix, HarmonyPatch(typeof(OneButtonDialog), nameof(OneButtonDialog.Show), typeof(string), typeof(string))]
    static void ExplainRefusal(ref string message)
    {
        if (_refusal is null || message != Strings.GetText("GameSetupScreen/InvalidShip")) return;
        message = _refusal;
        _refusal = null;
    }
}

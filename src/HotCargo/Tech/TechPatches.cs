using System.Reflection;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Modes.Career;
using Cosmoteer.Resources;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Simulation.Stasis;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Tech;

/// <summary>Spec 19-22: the tech value replaces the station price. All inputs are sim state, so every client computes the same price.</summary>
[HarmonyPatch(typeof(CareerSimModeManager))]
static class TechPricePatches
{
    /// <summary>The player sells. The station pays the tech value.</summary>
    [HarmonyPostfix, HarmonyPatch(nameof(CareerSimModeManager.GetResourceSellRefund))]
    static void SellRefund(CareerSimModeManager __instance, ID<ResourceRules> resourceType, ref int __result)
    {
        if (resourceType == Vaults.TechId && TechPrice.For(__instance) is { } run) __result = TechPrice.Sell(run);
    }

    /// <summary>The player buys back tech that a station holds. The factor above 1 stops a sell-and-buy-back money loop.</summary>
    [HarmonyPostfix, HarmonyPatch(nameof(CareerSimModeManager.GetResourceBuyCost))]
    static void BuyCost(CareerSimModeManager __instance, ID<ResourceRules> resourceType, ref int __result)
    {
        if (resourceType == Vaults.TechId && TechPrice.For(__instance) is { } run) __result = TechPrice.Buy(run);
    }
}

public static class TechPrice
{
    internal static Run? For(CareerSimModeManager mode) => Run.Current is { } run && run.Game == mode.Game ? run : null;

    public static int Sell(Run run) => (int)Math.Clamp(run.TechValue(), 0, int.MaxValue);

    public static int Buy(Run run) => (int)Math.Clamp(Math.Ceiling(Sell(run) * (double)run.T.Tech.BuyBackFactor), 0, int.MaxValue);
}

/// <summary>Spec 14: on human player ships only Tech Vaults store tech. Stations keep normal storage, so they can take tech that players sell.</summary>
[HarmonyPatch]
static class FlexGridBlocksTechPatch
{
    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(FlexResourceGrid.ResourceTile), "Cosmoteer.Ships.Resources.IResourceSink.GetRemainingCapacity")
        ?? throw new MissingMethodException("FlexResourceGrid.ResourceTile", "IResourceSink.GetRemainingCapacity");

    // Every sink search, manual transfer and AddResources call checks capacity first, so 0 here is enough to keep tech out.
    static void Postfix(FlexResourceGrid.ResourceTile __instance, ID<ResourceRules> resourceType, ref int __result)
    {
        if (__result > 0 && resourceType == Vaults.TechId && __instance.Grid.Ship is { IsHumanPlayerShip: true }) __result = 0;
    }
}

/// <summary>
/// Spec 449: a derelict ship that holds tech stays live. Vanilla puts a junk ship out of sight into stasis (CareerSimModeManager.GetShipStasisMode,
/// SimStasisManager.UpdateStasis), where it leaves the sim: its tech would drop out of the fleet count and its blip would go.
/// Exempt keeps it live without keeping the space around it live. The check reads sim data only, so every client agrees.
/// </summary>
[HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.GetShipStasisMode))]
static class DerelictVaultStasisPatch
{
    static void Postfix(Ship ship, ref StasisMode __result)
    {
        if (__result == StasisMode.Normal && ship.IsJunk && Vaults.ShipTech(ship) > 0) __result = StasisMode.Exempt;
    }
}

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
/// Spec 448: vaults resist heat. The heat status (sun heat, also thermal missiles and overclock heat) is the only source of "thermal"
/// damage, so a vault takes VaultSunHeatFactor of it. Limit: the status does not know where its heat came from, so non-sun heat
/// is reduced too. Revisit if a weapon gets direct thermal damage.
/// </summary>
[HarmonyPatch(typeof(Part), nameof(Part.GetDamageResistance))]
static class VaultHeatPatch
{
    static readonly ID<DamageType> Thermal = new("thermal");

    static void Postfix(Part __instance, ID<DamageType> damageType, ref float __result)
    {
        if (damageType != Thermal || __instance.Rules.ID != Vaults.VaultId || Run.Current is not { } run) return;
        __result = 1f - (1f - __result) * run.T.Tech.VaultSunHeatFactor;
    }
}

/// <summary>
/// Spec 65, 66: damage to a vault destroys tech inside it. Repair raises health, which costs nothing and brings nothing back.
/// Salvage (crew salvage a derelict, or take a vault apart for a move) is not damage here: the tech drops as crates when the vault goes.
/// </summary>
[HarmonyPatch(typeof(Part), "OnHealthChanged")]
static class VaultDamagePatch
{
    static void Postfix(Part __instance, int oldHealth, int newHealth, int oldSalvageDamage, int newSalvageDamage) => Errors.Guard("vault damage", () =>
    {
        if (newHealth >= oldHealth || newHealth == 0 || __instance.Rules.ID != Vaults.VaultId) return;
        // A salvage hit adds its damage to SalvageDamage too (Part.OnHit with DamageType.Salvage).
        if (newSalvageDamage - oldSalvageDamage >= oldHealth - newHealth) return;
        if (__instance.Ship?.Sim is null || Run.Current is not { } run) return;
        var grid = __instance.Components.OfType<TypedResourceGrid>().FirstOrDefault(g => g.ResourceType == Vaults.TechId);
        if (grid is null || grid.Resources == 0) return;
        int max = __instance.Rules.MaxHealth;
        int lost = TechModel.UnitsLostToDamage(max, oldHealth, newHealth, run.T.Tech.VaultDamagePerUnitLost * max);
        if (lost == 0) return;
        int removed = Math.Min(lost, grid.Resources);
        grid.AddResources(-removed);
        // The hit that crosses a step is often small: earlier hits (fire, weapons) took the health down without a loss.
        Log.Info($"vault at {__instance.Location} took {oldHealth - newHealth} damage, health {oldHealth} -> {newHealth} of {max}: lost {removed} tech, {grid.Resources} left");
        Onboarding.RunLog.TechLost(run, removed, Vaults.CountFleetTech(__instance.Ship.Sim));
    });
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

using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Game;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI.CommsModules;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Tech;

namespace HotCargo.Factions;

/// <summary>
/// Spec 642, 644, 655, 656: prices by faction role and heat. Each patched method is the one price source of its UI and of its
/// MP input, so the UI shows the price that the sim charges. The inputs are sim state only (stepped heat, roles, reputation).
/// Resource trades carry the price in the input and the sim drops a trade whose price changed (MPMoveResourcesInput).
/// Limit: a click in the moment that heat crosses a PriceHeatStep can be dropped. Revisit if players report lost trades.
/// </summary>
static class PricePatches
{
    static Run? For(GameRoot game) => Run.Current is { } run && run.Game == game ? run : null;

    /// <summary>Only faction player indexes are stations and trade ships. Null, human and Neutral (the Buyer) indexes keep vanilla prices.</summary>
    static FactionRules? Station(int? playerIndex) => playerIndex is { } i ? FactionRules.GetFaction(i) : null;

    /// <summary>Runs after the Tech area's tech price postfix (lower priority), so it scales the final price.</summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.GetResourceBuyCost))]
    static class BuyCost
    {
        [HarmonyPriority(Priority.Low)]
        static void Postfix(CareerSimModeManager __instance, ID<ResourceRules> resourceType, int? sourcePlayerIndex, ref int __result)
        {
            if (For(__instance.Game) is not { } run || Station(sourcePlayerIndex) is not { } faction) return;
            float factor = FactionsArea.Markup(run);
            if (resourceType == Vaults.TechId && run.PressureActive && FactionsArea.IsRival(run, faction)) factor *= run.T.Factions.RivalFencePremium;
            __result = FactionsModel.Scale(__result, factor);
        }
    }

    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.GetResourceSellRefund))]
    static class SellRefund
    {
        [HarmonyPriority(Priority.Low)]
        static void Postfix(CareerSimModeManager __instance, ID<ResourceRules> resourceType, int? destPlayerIndex, ref int __result)
        {
            if (resourceType != Vaults.TechId || For(__instance.Game) is not { PressureActive: true } run) return;
            if (FactionsArea.IsRival(run, Station(destPlayerIndex))) __result = FactionsModel.Scale(__result, run.T.Factions.RivalFencePremium);
        }
    }

    /// <summary>Spec 656: one source for the hire tab labels, the auto-hire total and the charge in MPHireCrewInput.</summary>
    [HarmonyPatch(typeof(AICrewProviderModule), nameof(AICrewProviderModule.GetHireCost))]
    static class HireCost
    {
        static void Postfix(AICrewProviderModule __instance, GameRoot game, ref int __result)
        {
            if (__result <= 0 || For(game) is not { } run) return;
            int multiple = Math.Max(1, __instance.Rules.CrewHireCostRoundToMultiple);
            __result = (int)Math.Min(int.MaxValue, Math.Ceiling(FactionsModel.Scale(__result, FactionsArea.CrewMarkup(run)) / (double)multiple) * multiple);
        }
    }

    /// <summary>Spec 655: blueprints are a station service. One source for the techs tab and MPBuyTechInput.</summary>
    [HarmonyPatch(typeof(CommTechsTab), "GetTechCost")]
    static class TechCost
    {
        static void Postfix(GameRoot game, Ship callee, ref int __result)
        {
            if (__result <= 0 || For(game) is not { } run || FactionRules.GetShipFaction(callee) is null) return;
            __result = FactionsModel.Scale(__result, FactionsArea.Markup(run));
        }
    }

    /// <summary>Spec 642: offers from a hot zone pay more money. One source for the reward text, the sort value and the payout.</summary>
    [HarmonyPatch(typeof(MoneyReward), "GetScaledMoney")]
    static class HotZoneMoney
    {
        static void Postfix(MoneyReward __instance, GameRoot game, ref int __result)
        {
            if (__result <= 0 || For(game) is not { PressureActive: true } run || __instance._mission.SpawnedAt is not { } at) return;
            if (game.Map.Nodes[at.MapNodeID] is { } node && FactionsArea.ZoneOf(run, node) == Zone.Ally)
                __result = FactionsModel.Scale(__result, run.T.Factions.AllyMoneyFactor);
        }
    }
}

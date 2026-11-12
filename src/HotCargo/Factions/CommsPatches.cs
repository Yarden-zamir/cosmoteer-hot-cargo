using Cosmoteer.Factions;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Special;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Journey;
using HotCargo.Ux;

namespace HotCargo.Factions;

/// <summary>Spec 657 to 659: refusals, and the faction and price badges in comms.</summary>
static class CommsPatches
{
    static Run? For(CareerSimModeManager mode) => Run.Current is { } run && run.Game == mode.Game ? run : null;

    /// <summary>
    /// Spec 657: the one choke point of comms. The hail fails with the vanilla "HAIL REJECTED", an open window closes,
    /// and MPOpenCommsInput refuses. It runs in the sim, so Refuses reads sim state only.
    /// </summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.WillShipAnswerCall))]
    static class AnswerCall
    {
        static void Postfix(CareerSimModeManager __instance, Ship caller, Ship callee, ref bool __result)
        {
            if (__result && For(__instance) is { } run && FactionsArea.Refuses(run, caller, callee)) __result = false;
        }
    }

    /// <summary>Spec 657: crew cannot trade with a station that refuses, also for parts that the construction buys there.</summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.AllowResourceTrade))]
    static class Trade
    {
        static void Postfix(CareerSimModeManager __instance, Ship caller, Ship callee, ref bool __result)
        {
            if (__result && For(__instance) is { } run && FactionsArea.Refuses(run, caller, callee)) __result = false;
        }
    }

    /// <summary>Spec 658: the refusal message on a hail. UI only: the vanilla hail goes on and fails through AnswerCall.</summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.TryShipCall))]
    static class Hail
    {
        static void Prefix(CareerSimModeManager __instance, Ship caller, Ship callee) => UxKit.Guard("refusal message", () =>
        {
            if (For(__instance) is not { } run || !FactionsArea.Refuses(run, caller, callee)) return;
            string line = RefusalLine(callee);
            run.Game.Gui.ShowAreaName(line);
            UxKit.ChatLine(run.Game, line);
            LastRefusal = line;
            Log.Info($"factions refusal: {line}");
        });
    }

    /// <summary>The last refusal message, for tests.</summary>
    internal static string LastRefusal = "";

    internal static string RefusalLine(Ship station) => FactionsArea.Format("RefusalFmt", station.Metadata.XmlShipName);

    /// <summary>Spec 659: badges for the fence, shelter, refusal and markup in the comms list, after the UX service badges.</summary>
    [HarmonyPatch(typeof(CommsToolbox.CallShipItem), nameof(CommsToolbox.CallShipItem.Update))]
    static class CommsList
    {
        [HarmonyPriority(Priority.Low)]
        static void Postfix(CommsToolbox.CallShipItem __instance)
        {
            if (__instance.IsHailingPlayer || Run.Current is not { PressureActive: true } run || run.Game != __instance.Ship.Game) return;
            UxKit.Guard("faction badges", () =>
            {
                string badges = Badges(run, __instance._toolbox.GetBestCallerShip(__instance.Ship), __instance.Ship);
                if (badges.Length > 0) __instance._iff.Text += "   " + badges;
            });
        }
    }

    internal static string Badges(Run run, Ship? caller, Ship station)
    {
        if (station.IsHumanPlayerShip || Buyer.IsBuyer(station) || SpaceStation.GetStation(station, onlyOperating: false) is null) return "";
        var faction = FactionRules.GetShipFaction(station);
        var t = run.T.Factions;
        var parts = new List<string>();
        if (FactionsArea.IsRival(run, faction)) parts.Add($"<good>{FactionsArea.Format("BadgeFenceFmt", t.RivalFencePremium)}</good>");
        if (FactionsArea.Shelters(run, faction)) parts.Add($"<good>{FactionsArea.Text("BadgeShelter")}</good>");
        if (FactionsArea.IsAlly(run, faction)) parts.Add($"<bad>{FactionsArea.Text("BadgeAlly")}</bad>");
        if (FactionsArea.Refuses(run, caller, station)) parts.Add($"<bad>{FactionsArea.Text("BadgeRefuses")}</bad>");
        float markup = FactionsArea.Markup(run);
        if (markup > 1f) parts.Add($"<bad>{FactionsArea.Format("BadgeMarkupFmt", markup)}</bad>");
        return string.Join("  ", parts);
    }

    /// <summary>Spec 656: the hire tab says how much heat adds to the crew price.</summary>
    [HarmonyPatch(typeof(CommHireTab), "OnUpdatingUIState")]
    static class HireTab
    {
        static void Postfix(CommHireTab __instance)
        {
            if (Run.Current is not { PressureActive: true } run || run.Game != __instance.Game) return;
            UxKit.Guard("crew price line", () =>
            {
                float f = FactionsArea.CrewMarkup(run);
                if (f > 1f) __instance._fameCountLbl.Text += "\n" + FactionsArea.Format("HireHeatFmt", (f - 1f) * 100f);
            });
        }
    }
}

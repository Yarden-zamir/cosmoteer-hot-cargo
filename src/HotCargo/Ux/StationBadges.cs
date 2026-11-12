using System.Runtime.CompilerServices;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Ships;
using Halfling;
using Halfling.Graphics;
using HarmonyLib;

namespace HotCargo.Ux;

/// <summary>
/// V11. Vanilla shows what a station offers only after you hail it.
/// This adds service badges (trade, crew, techs, missions you can take, rewards to collect) to the comms list
/// and to the hover text of the ship. The values come from the same ShipAI getters that the comms window uses.
/// </summary>
static class StationBadges
{
    sealed class Cached { public double Time = double.MinValue; public string Text = ""; }
    static readonly ConditionalWeakTable<Ship, Cached> Cache = new();

    /// <summary>Badge text for a ship, or "" when it offers nothing to the caller. Cached for one second per ship.</summary>
    public static string For(Ship ship, Ship? caller)
    {
        if (caller is null || ship.AI is not { } ai || ship.IsEnemiesWithLocalPlayer || ship.IsLocalPlayerShip) return "";
        var c = Cache.GetOrCreateValue(ship);
        double now = App.Clock.Time.Seconds;
        if (now - c.Time < 1.0) return c.Text;
        c.Time = now;
        var parts = new List<string>();
        if (ai.WillTradeWith(caller)) parts.Add(UxKit.Text("BadgeTrade"));
        if (ai.WillProvideCrew(caller)) parts.Add(UxKit.Text("BadgeCrew"));
        if (ai.WillProvideTechs(caller)) parts.Add(UxKit.Text("BadgeTechs"));
        if (ai.WillProvideMissions(caller))
        {
            var offers = ai.GetProvidedMissions(caller)?.Where(m => m.CanAccept(ship.Sim)).ToList() ?? [];
            // Spec 804: a station with a story chain offer says so, so the first station of a run shows its chain at once.
            if (UxKit.On(s => s.MissionSelect) && offers.Any(m => MissionSelect.KindOf(m) == MissionKind.Chain))
                parts.Add(MissionSelect.ChainColor.FormatWithXml(UxKit.Text("BadgeChain")));
            if (offers.Count > 0) parts.Add(UxKit.Format("BadgeMissionsFmt", offers.Count));
            // GetPendingMissionRewards throws when no module provides missions, so it stays inside this check.
            int rewards = ai.GetPendingMissionRewards(caller).Count;
            if (rewards > 0) parts.Add(UxKit.Format("BadgeRewardsFmt", rewards));
        }
        c.Text = string.Join(" · ", parts);
        return c.Text;
    }

    [HarmonyPatch(typeof(CommsToolbox.CallShipItem), nameof(CommsToolbox.CallShipItem.Update))]
    static class CommsList
    {
        static void Postfix(CommsToolbox.CallShipItem __instance)
        {
            if (__instance.IsHailingPlayer || !UxKit.On(s => s.StationBadges)) return;
            UxKit.Guard("station badges", () =>
            {
                string badges = For(__instance.Ship, __instance._toolbox.GetBestCallerShip(__instance.Ship));
                if (badges.Length > 0) __instance._iff.Text += "   <gray>" + badges + "</gray>";
            });
        }
    }

    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.GetShipMouseOverText))]
    static class HoverText
    {
        static void Postfix(Ship ship, ref string __result)
        {
            if (!ship.IsWithinSightOfLocalPlayer || !UxKit.On(s => s.StationBadges)) return;
            string result = __result;
            UxKit.Guard("station badges", () =>
            {
                string badges = For(ship, ship.Sim?.Ships.LocalPlayerShips.FirstOrDefault());
                if (badges.Length > 0) result += "\n<s14><gray>" + badges + "</gray></s14>";
            });
            __result = result;
        }
    }
}

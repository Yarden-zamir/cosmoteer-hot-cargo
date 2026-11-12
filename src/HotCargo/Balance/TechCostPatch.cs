using Cosmoteer.Game;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Modes.Career.TechTree;
using Cosmoteer.Ships;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Balance;

/// <summary>
/// Spec 301: blueprint price × Journey.TechCostScale. CommTechsTab.GetTechCost is the one price source of the buy button and of the
/// buy input (MPBuyTechInput), so the shown price and the paid price agree. The scale comes from the run tunables, which all
/// multiplayer clients share.
/// </summary>
[HarmonyPatch(typeof(CommTechsTab), "GetTechCost")]
static class TechCostPatch
{
    static void Postfix(GameRoot game, ref int __result)
    {
        if (__result <= 0 || Run.Current is not { } run || run.Game != game) return;
        __result = Scaled(__result, run.T.Journey.TechCostScale);
    }

    /// <summary>Rounds to 100, like the vanilla prices, and keeps a priced tech above 0.</summary>
    internal static int Scaled(int cost, float scale) => Math.Max(100, (int)Math.Round(cost * (double)scale / 100.0) * 100);
}

using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Hunters;

namespace HotCargo.Ai;

/// <summary>
/// Spec 200. Vanilla AggroEnemies scores each target with SimModeManager.ModifyAIAttackValue. This postfix makes weakened
/// targets score higher, so AI ships finish off a damaged ship instead of spreading fire. Hunters get the same factor in HcHunt.
/// Tell: once one of your ships is badly hurt, the enemies turn on it.
/// </summary>
[HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.ModifyAIAttackValue))]
static class FocusPatch
{
    static void Postfix(Ship sourceShip, Ship targetShip, ref float value)
    {
        if (Run.Current is null) return;
        try
        {
            if (AiGate.Active(Behavior.Focus, AiGate.IsHunterAi(sourceShip)) is { } t)
                value *= AiModel.FocusFactor(HunterOrders.HealthFraction(targetShip), t.FocusWeakFactor);
        }
        catch (Exception ex) { Errors.Report("focus fire", ex.Message, ex); }
    }
}

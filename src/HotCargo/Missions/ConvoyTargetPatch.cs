using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Data;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Missions;

/// <summary>
/// Spec 36. Vanilla AggroEnemies scores a target by its firepower, and CareerSimModeManager.ModifyAIAttackValue divides
/// the score of civilian ships by 10,000, so AI never picks an unarmed freighter while a warship is in sight.
/// Convoy raiders value the convoy freighter like a ship with MissionTunables.EscortRaiderFocus times their own firepower.
/// Leftover raiders keep this after the mission ends. That is harmless: the freighter is then a normal civilian ship.
/// </summary>
[HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.ModifyAIAttackValue))]
static class ConvoyTargetPatch
{
    static readonly ID<SimObjectSpawner> RaiderTag = new("hc_escort_raider"), FreighterTag = new("hc_escort_ship");

    static void Postfix(Ship sourceShip, Ship targetShip, ref float value)
    {
        if (Run.Current is null) return;
        try
        {
            if (sourceShip.Tags?.Contains(RaiderTag) != true || targetShip.Tags?.Contains(FreighterTag) != true) return;
            float focus = MissionArea.TunablesFor(sourceShip.Sim.RequireGame()).EscortRaiderFocus;
            value = Math.Max(value, sourceShip.Weapons.GetTotalFirePower() * focus);
        }
        catch (Exception ex) { Errors.Report("convoy raider target", ex.Message, ex); }
    }
}

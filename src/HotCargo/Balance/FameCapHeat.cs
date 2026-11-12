using Cosmoteer.Game;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Cosmoteer.Ships;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Journey;

namespace HotCargo.Balance;

/// <summary>
/// Spec 302: deeds add heat even when the vanilla fame cap swallows their fame. A mission or a kill pays fame only up to the MaxFame of
/// its tier, and the fame heat of spec 25 counts the fame that the players gained. Without this, missions above the cap added no heat,
/// so a player at the cap of the system could take missions without limit. These patches add the heat of the fame that the cap took.
/// They run in the sim (mission rewards and ship junking), so they are deterministic.
/// </summary>
static class FameCapHeat
{
    static void AddCappedHeat(GameRoot game, int uncapped, int gained)
    {
        int missing = uncapped - Math.Max(0, gained);
        if (missing <= 0 || Run.Current is not { } run || run.Game != game || !run.PressureActive) return;
        float mult = run.HeatMultiplier(run.State.FleetTech);
        int standard = (int)Math.Round(run.StandardMissionFamePaid(run.CurrentTier));
        run.AddHeat(HeatModel.FameHeat(run.T.Heat.HeatPerStandardMission, mult, missing, standard), game.Sim);
    }

    [HarmonyPatch(typeof(FameReward), nameof(FameReward.GiveReward))]
    static class MissionFame
    {
        static void Prefix(GameRoot game, out int __state) => __state = ((CareerGameModeManager)game.Mode).PlayerFame;

        static void Postfix(FameReward __instance, GameRoot game, int __state) => Errors.Guard("fame cap heat", () =>
        {
            var mode = (CareerGameModeManager)game.Mode;
            // The uncapped part of FameReward.GetTotalAmount, with the same factors (RewardScalePatch scales the vanilla read).
            int uncapped = (int)(__instance.Fame * RewardScalePatch.FameFactor(mode.EconDifficulty));
            AddCappedHeat(game, uncapped, mode.PlayerFame - __state);
        });
    }

    [HarmonyPatch(typeof(CareerSimModeManager), "OnShipJunked")]
    static class KillFame
    {
        // Prefix: the vanilla method clears the ship's fame info. Same condition as the vanilla kill fame grant.
        static void Prefix(CareerSimModeManager __instance, Ship ship, int oldAllegiance) => Errors.Guard("fame cap heat", () =>
        {
            var game = __instance.Game;
            if (game is null || !ship.IsWithinSightOfTeam(0) || !game.HumanPlayersIndexes.Any(i => __instance.AreEnemies(oldAllegiance, i))) return;
            __instance.GetShipFameInfo(ship, out int fame, out _, applyDifficultyScaling: true);
            AddCappedHeat(game, fame, __instance.GetShipFameReward(ship, applyDifficultyScaling: true));
        });
    }
}

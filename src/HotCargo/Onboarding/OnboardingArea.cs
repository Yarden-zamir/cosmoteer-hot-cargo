using Cosmoteer.Modes.Career;
using HarmonyLib;
using HotCargo.Chains;
using HotCargo.Contracts;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Journey;
using HotCargo.Tech;

namespace HotCargo.Onboarding;

/// <summary>
/// Entry point of the Onboarding area (spec 720 to 734): Hot Cargo How to Play pages, their order before vanilla tutorials,
/// and the run log in the career log window. Mod.Boot calls Register after every other area.
/// </summary>
public static class OnboardingArea
{
    public static void Register()
    {
        AddPagesOfExistingAreas();
        RunLog.Register();
        OnboardingAutotest.Register();
    }

    /// <summary>
    /// Spec 721: the triggers of the pages for the areas that existed when this area started. A new area adds its own
    /// page and trigger (CONTRIBUTING.md, "How to Play pages").
    /// </summary>
    static void AddPagesOfExistingAreas()
    {
        // The run intro panel shows first; the page waits until the game ran a few seconds.
        Hints.Add("hc_run", run => run.State.GameSeconds >= 5);
        Hints.Add("hc_heat", run => run.State.StagesStarted > 0);
        Hints.Add("hc_vault", run => Vaults.HumanShips(run.Game.Sim).SelectMany(Vaults.VaultParts).Any(p => p.IsDamaged));
        Hints.Add("hc_hunters", run => SimQueries.TaggedShips(run.Game.Sim, HunterSpawner.HunterTag).Count > 0);
        // Comms with a station: the moment a sale is possible. A station in sight is too early: the run starts next to one.
        Hints.Add("hc_selling", run => run.State.FleetTech > 0 && run.Game.ScriptAccess.CommWindowOpen);
        Hints.Add("hc_contracts", run => run.Game.ScriptAccess.CommWindowOpen && run.State.Contracts.Terms.Count > 0);
        Hints.Add("hc_missions", run => run.Game.ScriptAccess.CommWindowOpen
            && ContractBook.Offers(run.Game.Sim).Any(o => IsHotCargoMission(o.Mission.MetatypeID.ToString())));
        Hints.Add("hc_chains", run => run.Mode.Missions.Any(m => m.MetatypeID == ChainLibrary.Metatype)
            || run.Game.ScriptAccess.CommWindowOpen && ContractBook.Offers(run.Game.Sim).Any(o => o.Mission.MetatypeID == ChainLibrary.Metatype));
        Hints.Add("hc_events", run => run.State.Events.Pending is not null);
        // Hot Cargo archetypes are the sector types with the hc_ prefix (mod/worlds/sectors.rules).
        Hints.Add("hc_worlds", run => run.Mode.GetMapNodeData(run.Game.CurrentMapNode).SectorType.ID.ToString().StartsWith("hc_", StringComparison.Ordinal));
        Hints.Add("hc_wanted", run => run.State.Events.WantedWarned);
        Hints.Add("hc_buyer", run => run.State.BuyerSpawned);
        Hints.Add("hc_settings", run => run.State.SectorIndex >= 2);
        // Ship tactics matter most in hard fights (the stage before the last, or later) and with more than one ship for each player.
        Hints.Add("hc_ship_tactics", run => run.T.ShipAi.Enabled && (run.State.StagesStarted >= Math.Max(1, run.T.Heat.Stages.Length - 1)
            || Vaults.HumanShips(run.Game.Sim).Count() > run.Game.HumanPlayersIndexes.Count));
        Hints.Add("hc_coop", run => run.IsMultiplayer && run.Game.HumanPlayersIndexes.Count >= 2);
    }

    /// <summary>Hot Cargo mission types start with "HotCargo". Chains have their own page; the Buyer mission is never an offer.</summary>
    static bool IsHotCargoMission(string metatype) =>
        metatype.StartsWith("HotCargo", StringComparison.Ordinal) && metatype != ChainLibrary.Metatype.ToString() && metatype != Buyer.MissionType.ToString();

    /// <summary>UI frame of a career game: the page triggers.</summary>
    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.Update))]
    static class Frame
    {
        static void Postfix(CareerGameModeManager __instance)
        {
            if (Run.Current is not { } run || run.Mode != __instance) return;
            Errors.Guard("onboarding frame", () => Hints.OnFrame(run));
        }
    }
}

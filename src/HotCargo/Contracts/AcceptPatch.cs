using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Modes.Career.Missions;
using Halfling.Network;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Contracts;

/// <summary>
/// Spec 102 to 104 and 106: the game's own accept input carries the fee, the flat heat, the rival dismissal and the informant roll.
/// The input runs in lockstep on every client, so all of it is deterministic in multiplayer.
/// </summary>
[HarmonyPatch(typeof(CommMissionsTab.MPAcceptMissionInput), nameof(CommMissionsTab.MPAcceptMissionInput.Execute))]
static class AcceptPatch
{
    static bool Prefix(CommMissionsTab.MPAcceptMissionInput __instance, GameRoot game, int playerIndex, out Mission? __state)
    {
        __state = null;
        Mission? m = null;
        bool allow = true;
        Errors.Guard("contract accept", () =>
        {
            if (Run.Current is not { } run || run.Game != game || !run.PressureActive || game.Sim is not { } sim) return;
            m = ContractBook.Offers(sim).Select(o => o.Mission).FirstOrDefault(x => x.MissionID.Equals(__instance.MissionID));
            if (m is null || ContractBook.CanPay(run, m, playerIndex)) return;
            allow = false;
            Log.Info($"contracts: player {playerIndex} cannot pay the fee of {m.MetatypeID} {m.MissionID}");
            // UI only, for the player who clicked.
            if (playerIndex == game.LocalPlayerIndex)
                game.Gui.ShowAreaName(Strings.FormatText("HotCargoContracts/CannotPayFmt", ContractBook.Preview(run, m)?.Fee ?? 0));
        });
        __state = allow ? m : null;
        return allow;
    }

    static void Postfix(GameRoot game, int playerIndex, Mission? __state)
    {
        if (__state is not { } m) return;
        Errors.Guard("contract accept", () =>
        {
            if (Run.Current is not { } run || run.Game != game || game.Sim is not { } sim) return;
            // The vanilla input can still refuse (prerequisites). Only a mission that reached the log is accepted.
            if (!run.Mode.Missions.ContainsMission(m)) return;
            ContractBook.OnAccepted(run, sim, m, playerIndex);
        });
    }
}

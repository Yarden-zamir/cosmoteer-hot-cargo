using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Modes.Career.Comms;
using HarmonyLib;

namespace HotCargo.Ux;

/// <summary>
/// A comms window with a station or ship pauses the game, and closing the last comms window unpauses it.
/// It unpauses only a pause that it made itself, so a game that the player paused stays paused.
/// It uses the game's own pause input (like the pause key), so it is deterministic in multiplayer.
/// Switches: Ux.PauseOnComms (single player), Ux.PauseOnCommsMultiplayer (multiplayer, where it pauses every player).
/// Limit: if the player unpauses and pauses again by hand while the window is open, the close unpauses. Revisit if players report it.
/// </summary>
static class CommsPause
{
    /// <summary>The game that this feature paused, or null.</summary>
    static GameRoot? _pausedGame;

    static bool Enabled(GameRoot game) =>
        UxKit.Switches is { } s && (game.NetManager.IsMultiplayer ? s.PauseOnCommsMultiplayer : s.PauseOnComms);

    [HarmonyPatch(typeof(CommWindow), nameof(CommWindow.OnParented))]
    static class Open
    {
        static void Postfix(CommWindow __instance) => UxKit.Guard("comms pause", () =>
        {
            var game = __instance.Game;
            // A listen window never pauses (spec 1138).
            if (!Enabled(game) || game.Sim.IsPaused.Displayed || SharedChoice.ListenIn.SpeakerOf(__instance) is not null) return;
            game.NetManager.EnqueueInput(new MPSetPausedInput(paused: true));
            _pausedGame = game;
            Log.Info("ux: comms window paused the game");
        });
    }

    [HarmonyPatch(typeof(CommWindow), nameof(CommWindow.OnUnparenting))]
    static class Close
    {
        // Runs after the window left the open list, so IsCommWindowOpen sees only the other windows.
        static void Postfix(CommWindow __instance) => UxKit.Guard("comms pause", () =>
        {
            var game = __instance.Game;
            if (_pausedGame != game || CommWindow.IsCommWindowOpen(game) || SharedChoice.ListenIn.SpeakerOf(__instance) is not null) return;
            _pausedGame = null;
            if (!game.Sim.IsPaused.Displayed) return;
            game.NetManager.EnqueueInput(new MPSetPausedInput(paused: false));
            Log.Info("ux: comms window closed, game unpaused");
        });
    }
}

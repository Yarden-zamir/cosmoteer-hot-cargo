using Cosmoteer.Modes.Career;
using Cosmoteer.Simulation;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Autonomy;

/// <summary>Autonomous ships (spec 1050 to 1099). Mod.Boot calls Register once at startup.</summary>
public static class AutonomyArea
{
    public static void Register() => AutonomyAutotest.Register();

    /// <summary>
    /// The autonomy tick runs on sim ticks that are a multiple of TickSeconds, read from the sim's own tick counter, so a co-op client
    /// that joins late runs it on the same ticks. Limit: it assumes the default 60 fixed updates per second (SceneRoot); revisit if the
    /// game changes its fixed rate.
    /// </summary>
    static void OnTick(object? sender, SimRoot sim)
    {
        if (Run.Current is not { } run || run.Game.Sim != sim) return;
        var tun = run.T.Autonomy;
        int step = Math.Max(1, (int)MathF.Round(tun.TickSeconds * 60f));
        if (sim.Tick % step != 0) return;
        Errors.Guard("autonomy manning", () => Autonomous.Tick(sim));
        Errors.Guard("autonomy funnels", () => Funnel.Tick(sim, tun));
    }

    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.OnGameStarting))]
    static class TickHook
    {
        static void Postfix(CareerGameModeManager __instance)
        {
            var game = __instance.Game;
            game.EventHub.Tick += OnTick;
            game.Popping += (_, _) => game.EventHub.Tick -= OnTick;
        }
    }
}

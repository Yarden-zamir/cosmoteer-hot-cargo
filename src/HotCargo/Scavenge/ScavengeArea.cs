using Cosmoteer.Modes.Career;
using Cosmoteer.Simulation;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Scavenge;

/// <summary>Scavenging (spec 1330 to 1359). Mod.Boot calls Register once at startup.</summary>
public static class ScavengeArea
{
    public static void Register() => ScavengeAutotest.Register();

    /// <summary>
    /// Spec 1332: the age check (and the provocation end) runs on sim ticks that are a multiple of CheckSeconds, the arrival roll (spec 1343) on multiples of SpawnCheckSeconds, read from the sim's own tick counter, so a co-op
    /// client that joins late runs it on the same ticks. Limit: it assumes the default 60 fixed updates per second (SceneRoot), like
    /// the autonomy tick; revisit if the game changes its fixed rate.
    /// </summary>
    static void OnTick(object? sender, SimRoot sim)
    {
        if (Run.Current is not { } run || run.Game.Sim != sim) return;
        var t = run.T.Scavenge;
        if (!t.Enabled)
        {
            run.State.Scavenge.Seen.Clear();
            return;
        }
        if (sim.Tick % Ticks(t.CheckSeconds) == 0)
        {
            Errors.Guard("scavenge ages", () => Scavenger.CheckAges(run, sim));
            Errors.Guard("scavengers peace", () => Scavengers.CheckPeace(run, sim));
        }
        if (sim.Tick % Ticks(t.SpawnCheckSeconds) == 0) Errors.Guard("scavenger arrivals", () => Scavengers.CheckArrivals(run, sim));
    }

    static int Ticks(float seconds) => Math.Max(1, (int)MathF.Round(seconds * 60f));

    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.OnGameStarting))]
    static class TickHook
    {
        static void Postfix(CareerGameModeManager __instance)
        {
            Scavengers.CheckIndex();
            var game = __instance.Game;
            game.EventHub.Tick += OnTick;
            game.Popping += (_, _) => game.EventHub.Tick -= OnTick;
        }
    }
}

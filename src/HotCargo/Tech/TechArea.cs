using Cosmoteer.Localization;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Input;
using HotCargo.Core;

namespace HotCargo.Tech;

/// <summary>Entry point of the Tech area. Mod.Boot calls Register once at startup, before the game starts.</summary>
public static class TechArea
{
    public static void Register()
    {
        Run.Hooks.CountFleetTech = Vaults.CountFleetTech;
        Run.Hooks.NewRunStarted += StartingVault.OnNewRun;
        Run.Hooks.Tick += LossRule.OnTick;
        DebugKeys.Extra += OnDebugKeys;
        TechAutotest.Register();
    }

    /// <summary>Spec 56: F11 adds one tech unit to a vault, Shift+F11 removes one.</summary>
    static void OnDebugKeys(Run run)
    {
        var kb = App.Keyboard;
        if (kb.HotkeyPressed(ViKey.F11)) Log.Info($"debug: added {Vaults.Add(run.Game.Sim, 1)} tech");
        if (kb.HotkeyPressed(ViKey.Shift, ViKey.F11)) Log.Info($"debug: removed {Vaults.Remove(run.Game.Sim, 1)} tech");
    }
}

/// <summary>Spec 9, 12: the run is lost when the fleet tech count stays at 0 for LossGraceSeconds while the count is certain.</summary>
static class LossRule
{
    internal static void OnTick(Run run, SimRoot sim, double dt)
    {
        var s = run.State;
        if (s.FleetTech > 0 || !CountIsCertain(run, sim)) { s.SecondsWithoutTech = 0; return; }
        s.SecondsWithoutTech += dt;
        if (s.SecondsWithoutTech >= run.T.Tech.LossGraceSeconds) run.Lose(Strings.GetText("HotCargoTech/LossNoTech"));
    }

    /// <summary>
    /// The conditions of the vanilla game-over check in CareerGameModeManager.Update: no jump in progress, no friendly ships in another system.
    /// It checks ships in jump limbo instead of GameRoot.IsDoingNodeJump: that flag follows local animations and the local FTL autosave
    /// setting, so it changes at different ticks on each multiplayer machine. Jump limbo changes only at synchronized jump signals.
    /// </summary>
    static bool CountIsCertain(Run run, SimRoot sim) =>
        !sim.Ships.IsDoingLocalJump && run.Game.ShipsInJumpLimbo.Count == 0 &&
        run.Mode._nodeData.All(kv => kv.Key == run.Game.CurrentMapNode || kv.Value.NumFriendlyShips == 0);
}

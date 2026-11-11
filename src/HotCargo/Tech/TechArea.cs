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
    static Run? _run;
    /// <summary>
    /// Limit: kept in memory, not in the save. A client that joins during the grace time starts it from 0, so its loss can come up to
    /// LossGraceSeconds later than the host's. Revisit if co-op joins mid-loss show a desync.
    /// </summary>
    static double _zeroSeconds;

    internal static void OnTick(Run run, SimRoot sim, double dt)
    {
        if (_run != run) { _run = run; _zeroSeconds = 0; }
        if (run.State.FleetTech > 0 || !CountIsCertain(run, sim)) { _zeroSeconds = 0; return; }
        _zeroSeconds += dt;
        if (_zeroSeconds >= run.T.Tech.LossGraceSeconds) run.Lose("The fleet holds no Stolen Tech.");
    }

    /// <summary>The same conditions as the vanilla game-over check in CareerGameModeManager.Update: no jump in progress, no friendly ships in another system.</summary>
    static bool CountIsCertain(Run run, SimRoot sim) =>
        !sim.Ships.IsDoingLocalJump && !run.Game.IsDoingNodeJump &&
        run.Mode._nodeData.All(kv => kv.Key == run.Game.CurrentMapNode || kv.Value.NumFriendlyShips == 0);
}

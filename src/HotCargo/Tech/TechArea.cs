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
        Run.Hooks.Tick += LossTimer.OnTick;
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

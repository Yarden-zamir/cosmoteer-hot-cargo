using HotCargo.Dev;

namespace HotCargo.Tactics;

/// <summary>Entry point of the Tactics area. Mod.Boot calls Register once at startup, before the game starts.</summary>
public static class TacticsArea
{
    public static void Register()
    {
        foreach (string name in TacticsLog.Names)
            Autotest.Fields[$"tactic_{name}"] = () => TacticsLog.Count(name);
        // tacticlog on|off: turns Debug.TacticsLog on for this run, without a tunables file change. Test only.
        Autotest.Commands["tacticlog"] = arg => Autotest.RequireRun().T.Debug.TacticsLog = arg != "off";
    }
}

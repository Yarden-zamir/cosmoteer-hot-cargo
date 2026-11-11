using Halfling;
using Halfling.Input;

namespace HotCargo.Core;

/// <summary>Single-player debug keys (spec requirement 56). Areas add their own keys through Extra.</summary>
public static class DebugKeys
{
    /// <summary>Area debug keys. Called each frame only when debug keys are active.</summary>
    public static event Action<Run>? Extra;

    internal static void Handle(Run run)
    {
        if (run.IsMultiplayer || !run.T.Debug.DebugKeys) return;
        var kb = App.Keyboard;
        if (kb.HotkeyPressed(ViKey.F9)) run.AddHeat(10f, run.Game.Sim);
        if (kb.HotkeyPressed(ViKey.Shift, ViKey.F9)) run.AddHeat(-10f, run.Game.Sim);
        if (kb.HotkeyPressed(ViKey.F10)) run.ForceNextStage(run.Game.Sim);
        Extra?.Invoke(run);
    }
}

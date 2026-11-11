using Cosmoteer;
using Halfling.IO;

namespace HotCargo.Core;

/// <summary>
/// The startup hook loads this DLL into every Cosmoteer start.
/// The mod only acts when the player enabled the Hot Cargo rules mod in the game's mod list.
/// </summary>
public static class Activation
{
    public static bool IsModEnabled()
    {
        var root = (AbsolutePath)Mod.ModRoot;
        return Settings.EnabledMods.Contains(root);
    }
}

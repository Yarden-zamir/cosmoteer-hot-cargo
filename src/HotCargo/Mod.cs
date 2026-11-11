using HarmonyLib;

namespace HotCargo;

public static class Mod
{
    public const string HarmonyId = "yarden-zamir.hot_cargo";

    public static void Boot()
    {
        try
        {
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(typeof(Mod).Assembly);
            Log.Info($"harmony patched {harmony.GetPatchedMethods().Count()} methods");
        }
        catch (Exception ex)
        {
            Log.Error("boot failed", ex);
            throw;
        }
    }
}

[HarmonyPatch(typeof(Cosmoteer.Gui.TitleScreen), MethodType.Constructor)]
static class HelloTitleScreenPatch
{
    static void Postfix() => Log.Info("title screen constructed — hello from Hot Cargo");
}

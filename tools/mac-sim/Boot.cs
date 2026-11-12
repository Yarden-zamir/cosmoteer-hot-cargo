using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Data;
using Halfling.Platforms;
using Halfling.Serialization;
using HarmonyLib;

/// <summary>
/// Boots the real GameApp without a window: Platform.Create returns MacPlatform, then the asset loader runs on this thread.
/// The process must run with the game folder as working directory and with --headless --rootsavepath on its command line,
/// because GameApp and Paths read those from Environment.GetCommandLineArgs.
/// </summary>
static class Boot
{
    public static Harmony Harmony { get; } = new("mac-sim.host");

    public static void App()
    {
        Harmony.Patch(AccessTools.Method(typeof(Platform), nameof(Platform.Create), Type.EmptyTypes),
            prefix: new HarmonyMethod(typeof(Boot), nameof(CreatePlatformPrefix)));
        NativeSeams.Apply(Harmony);
        GameApp.InitializePhysicsSettings();
        GameApp.Instance = new GameApp();
        NativeSeams.PlaceholderTextures();
    }

    static bool CreatePlatformPrefix(ref Platform __result)
    {
        __result = new MacPlatform();
        return false;
    }

    /// <summary>Runs the body of Assets.LoaderThread on this thread. Skips the Assets constructor (it builds loading screen widgets).</summary>
    public static Rules Rules()
    {
        var assets = (Assets)RuntimeHelpers.GetUninitializedObject(typeof(Assets));
        // Field initializers that the skipped constructor would run.
        var t = Traverse.Create(assets);
        t.Field("_loadedMods").SetValue(new List<Cosmoteer.Mods.ModInfo>());
        t.Field("<LoadedMods>k__BackingField").SetValue(new Dictionary<Cosmoteer.Multiplayer.ModData, Halfling.IO.AbsolutePath>());
        t.Field("<LoadedGameplayMods>k__BackingField").SetValue(new HashSet<Cosmoteer.Multiplayer.ModData>());
        t.Field("_modLoadErrors").SetValue(new List<(string, string)>());
        assets._pt = new ProgressTracker();
        GameApp.s_assets = assets;
        var sw = Stopwatch.StartNew();
        assets.LoaderThread();
        Out.Line($"rules loaded in {sw.Elapsed.TotalSeconds:0.0} s; mod errors: {assets._modLoadErrors!.Count}");
        foreach (var (id, error) in assets._modLoadErrors) Out.Line($"  mod {id}: {error}");
        return assets.Rules;
    }
}

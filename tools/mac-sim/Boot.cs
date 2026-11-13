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

    /// <summary>Does what HotCargo.Loader does in the game: runs the mod's startup hook from the staged mod folder, before GameApp exists.</summary>
    public static bool HotCargo()
    {
        var args = Environment.GetCommandLineArgs();
        var dll = Path.Combine(args[Array.IndexOf(args, "--rootsavepath") + 1], "Mods", "hot_cargo", "csharp", "HotCargo.dll");
        if (!File.Exists(dll)) return false;
        var hook = System.Reflection.Assembly.LoadFrom(dll).GetType("StartupHook") ?? throw new InvalidOperationException($"no StartupHook in {dll}");
        AccessTools.Method(hook, "Initialize").Invoke(null, null);
        return true;
    }

    public static void App()
    {
        Harmony.Patch(AccessTools.Method(typeof(Platform), nameof(Platform.Create), Type.EmptyTypes),
            prefix: new HarmonyMethod(typeof(Boot), nameof(CreatePlatformPrefix)));
        NativeSeams.Apply(Harmony);
        GameApp.InitializePhysicsSettings();
        GameApp.Instance = new GameApp();
        NativeSeams.PlaceholderTextures();
    }

    public const double FrameSeconds = 1.0 / 60;

    /// <summary>
    /// Every clock advances one fixed frame per call instead of the real time that passed. The game then runs as fast as
    /// the CPU allows, and two runs with the same input see the same frame times.
    /// </summary>
    public static void FixedStepClock()
    {
        Harmony.Patch(AccessTools.Method(typeof(Halfling.Timing.RealTimeClock), nameof(Halfling.Timing.RealTimeClock.AdvanceRealTime)),
            prefix: new HarmonyMethod(typeof(Boot), nameof(AdvanceFixed)));
        Harmony.Patch(AccessTools.Method(typeof(Cosmoteer.Game.GameRoot), nameof(Cosmoteer.Game.GameRoot.JumpToNode)),
            postfix: new HarmonyMethod(typeof(Boot), nameof(TrackJump)));
    }

    static int s_jumpsRunning;

    /// <summary>
    /// GameRoot.JumpToNode loads the new system on a background thread, and a test waits for it in game seconds.
    /// The fixed-step clock runs faster than real time, so a "wait 25" ends before the load does. While a jump runs,
    /// every frame takes at least one frame of real time, like on the PC. Limit: the number of frames that a jump takes
    /// varies from run to run. Revisit if a test needs exact sim values across a jump.
    /// </summary>
    static void TrackJump(Task __result)
    {
        Interlocked.Increment(ref s_jumpsRunning);
        __result.ContinueWith(_ => Interlocked.Decrement(ref s_jumpsRunning), TaskContinuationOptions.ExecuteSynchronously);
    }

    static bool AdvanceFixed(Halfling.Timing.RealTimeClock __instance)
    {
        if (Volatile.Read(ref s_jumpsRunning) > 0) Thread.Sleep(TimeSpan.FromSeconds(FrameSeconds));
        __instance.Advance(new Halfling.Timing.Time(FrameSeconds));
        return false;
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

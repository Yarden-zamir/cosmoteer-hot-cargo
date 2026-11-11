using System.Reflection;

/// <summary>
/// .NET startup hook that Cosmoteer.runtimeconfig.json points at. It has no game references and rarely changes,
/// so it can stay in the game Bin folder while several game instances run.
/// It picks the Hot Cargo build to load:
/// - with "--rootsavepath R" on the command line (test instances): R\Mods\hot_cargo\csharp\HotCargo.dll
/// - otherwise: the path in hot_cargo_default.txt next to this DLL (the player's own Mods folder).
/// A missing build means: do nothing, the game runs vanilla.
/// </summary>
internal static class StartupHook
{
    public static void Initialize()
    {
        string here = Path.GetDirectoryName(typeof(StartupHook).Assembly.Location)
            ?? throw new InvalidOperationException("HotCargo.Loader: cannot find own folder");
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "--rootsavepath");
        string? dll = i >= 0 && i + 1 < args.Length
            ? Path.Combine(args[i + 1], "Mods", "hot_cargo", "csharp", "HotCargo.dll")
            : ReadDefault(Path.Combine(here, "hot_cargo_default.txt"));
        if (dll is null || !File.Exists(dll)) return;
        var asm = Assembly.LoadFrom(dll);
        var hook = asm.GetType("StartupHook") ?? throw new InvalidOperationException($"no StartupHook type in {dll}");
        var init = hook.GetMethod("Initialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"no StartupHook.Initialize in {dll}");
        init.Invoke(null, null);
    }

    static string? ReadDefault(string file) => File.Exists(file) ? File.ReadAllText(file).Trim() : null;
}

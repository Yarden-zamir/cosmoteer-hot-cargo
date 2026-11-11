using System.Reflection;
using System.Runtime.Loader;

/// <summary>
/// .NET startup hook entry point. The runtime calls Initialize before the game Main
/// when Cosmoteer.runtimeconfig.json has "STARTUP_HOOKS" pointing at this DLL.
/// Must be a top-level internal class named StartupHook.
/// </summary>
internal static class StartupHook
{
    public static void Initialize()
    {
        string modDir = Path.GetDirectoryName(typeof(StartupHook).Assembly.Location)
            ?? throw new InvalidOperationException("HotCargo: cannot find own folder");
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            string candidate = Path.Combine(modDir, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };
        HotCargo.Log.Init(modDir);
        HotCargo.Log.Info($"startup hook loaded in {Environment.ProcessPath}");
        HotCargo.Mod.Boot();
    }
}

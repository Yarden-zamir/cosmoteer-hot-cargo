using System.Runtime.CompilerServices;
using HarmonyLib;

GameAssemblies.Install();
Console.WriteLine($".NET {Environment.Version} {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
HarmonyProbe.Run();

static class HarmonyProbe
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run()
    {
        var h = new Harmony("mac-sim.probe");
        h.Patch(AccessTools.Method(typeof(Halfling.Mathx), nameof(Halfling.Mathx.Max), [typeof(int), typeof(int)]),
            prefix: new HarmonyMethod(typeof(HarmonyProbe), nameof(Prefix)));
        Console.WriteLine($"Mathx.Max(1,2) patched => {Halfling.Mathx.Max(1, 2)}");
    }

    static bool Prefix(ref int __result) { __result = 42; return false; }
}

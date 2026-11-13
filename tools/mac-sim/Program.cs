using System.Runtime.CompilerServices;

GameAssemblies.Install();
// GameApp redirects Console to its log file. This writer goes to the real stdout.
var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
Out.Writer = stdout;
Out.Line($".NET {Environment.Version} {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}; cwd {Environment.CurrentDirectory}");
try
{
    Run();
}
catch (Exception ex)
{
    Out.Line($"FAILED: {ex}");
    Environment.Exit(1);
}

// Modes: "rules" (default): boot and load the rules, then exit. "autotest <script>": run a tests/autotest script in the real game loop.
[MethodImpl(MethodImplOptions.NoInlining)]
static void Run()
{
    var argv = Environment.GetCommandLineArgs();
    int at = Array.IndexOf(argv, "autotest");
    var modRoot = Path.Combine(argv[Array.IndexOf(argv, "--rootsavepath") + 1], "Mods", "hot_cargo");
    if (at >= 0)
    {
        File.Copy(argv[at + 1], Path.Combine(modRoot, "autotest.txt"), overwrite: true);
    }
    Out.Line($"Hot Cargo loaded: {Boot.HotCargo()}");
    Boot.App();
    Out.Line("GameApp constructed");
    // GameApp.Main reads --cores; the host skips Main. Without it every run starts one spinning helper thread per core,
    // and parallel runs (scripts/autotest-mac.sh) starve each other.
    int cores = Array.IndexOf(argv, "--cores");
    if (cores >= 0) Halfling.Performance.FastParallel.ThreadCount = int.Parse(argv[cores + 1]) - 1;
    if (at < 0)
    {
        var rules = Boot.Rules();
        Out.Line($"ships in rules: {rules.Ships.Length}");
        return;
    }
    // Co-op runs (scripts/autotest-mac-mp.sh) keep the real-time clock: network requests and lockstep timing read the game
    // clock, and the fixed step runs it many times faster than the other process and the network.
    if (Environment.GetEnvironmentVariable("MAC_SIM_REALTIME") != "1") Boot.FixedStepClock();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    // The log decides, like on the PC: an exception after AUTOTEST DONE (shutdown, for example a cancelled stasis preload
    // in StasisDoodadSpawner.CancelPreload) is no failure. An exception during the script leaves no AUTOTEST DONE.
    try { Cosmoteer.GameApp.Instance.ApplicationMain(argv); }
    catch (Exception ex) { Out.Line($"game loop threw: {ex}"); }
    Out.Line($"game loop ended after {sw.Elapsed.TotalSeconds:0.0} s wall time, {Halfling.App.Director?.FrameCount} frames");
    // Same rule as scripts/autotest.sh: pass only with AUTOTEST DONE and no AUTOTEST FAIL or ERROR line.
    var log = File.ReadAllLines(Path.Combine(modRoot, "csharp", "hot_cargo_log.txt"));
    var bad = log.Where(l => l.Contains(" ERROR ") || l.Contains("AUTOTEST FAIL")).ToList();
    bool done = log.Any(l => l.Contains("AUTOTEST DONE"));
    bad.ForEach(Out.Line);
    Out.Line(done && bad.Count == 0 ? "== AUTOTEST PASSED" : "== AUTOTEST FAILED");
    Environment.Exit(done && bad.Count == 0 ? 0 : 1);
}

static class Out
{
    public static TextWriter Writer { get; set; } = Console.Out;
    public static void Line(string text) => Writer.WriteLine(text);
}

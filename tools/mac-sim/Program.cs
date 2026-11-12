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

[MethodImpl(MethodImplOptions.NoInlining)]
static void Run()
{
    Boot.App();
    Out.Line("GameApp constructed");
    var rules = Boot.Rules();
    Out.Line($"ships in rules: {rules.Ships.Length}");
}

static class Out
{
    public static TextWriter Writer { get; set; } = Console.Out;
    public static void Line(string text) => Writer.WriteLine(text);
}

using HotCargo.Core;

namespace HotCargo.TunablesMenu;

/// <summary>
/// Requests from scripts/tune.sh (spec 853). The script writes tune.request in the mod folder: one line per change,
/// "set PATH VALUE" or "reset PATH", with the paths and value texts of the settings window. The mod applies all lines as one
/// change through TunablesEditor (validated, written to tunables.rules, sent to every machine in co-op), and writes tune.result:
/// "ok HASH" and the timing of each path, or "error MESSAGE" with the values in effect unchanged.
/// </summary>
static class TuneRequest
{
    const double PollSeconds = 0.5;
    static DateTime _nextPoll;

    static string RequestPath => Path.Combine(Mod.ModRoot, "tune.request");
    static string ResultPath => Path.Combine(Mod.ModRoot, "tune.result");

    /// <summary>The next Poll reads the request at once. For autotests: the headless sim runs faster than the real-time poll.</summary>
    internal static void PollNow() => _nextPoll = DateTime.MinValue;

    /// <summary>Frame hook: applies a waiting request. UI only; the change goes through the same path as a window edit.</summary>
    internal static void Poll(Run run)
    {
        if (DateTime.UtcNow < _nextPoll) return;
        _nextPoll = DateTime.UtcNow.AddSeconds(PollSeconds);
        if (!File.Exists(RequestPath)) return;
        string[] lines = File.ReadAllLines(RequestPath).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        File.Delete(RequestPath);
        string? error = lines.Length == 0 ? "the request is empty" : TunablesEditor.Apply(run, lines);
        string result = error is not null
            ? $"error {error}"
            : $"ok {TunablesEditor.CurrentHash(run)}\n" + string.Join("\n", lines.Select(Timing));
        File.WriteAllText(ResultPath, result + "\n");
        Log.Info($"tune request: {string.Join(" | ", lines)} -> {result.Split('\n')[0]}");
    }

    static string Timing(string line)
    {
        string path = line.Split(' ', 3)[1];
        return $"{path}: applies {TunableTiming.Of(TunablePath.LastField(typeof(Tunables), path))}";
    }
}

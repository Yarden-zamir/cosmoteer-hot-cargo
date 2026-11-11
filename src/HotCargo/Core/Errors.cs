namespace HotCargo.Core;

/// <summary>
/// Reports failures of mod code that runs inside game callbacks (patches, sim ticks, UI frames).
/// A failure must not crash the game: it logs once for each place, shows in the overlay, and the caller skips its work.
/// In the sim tick every multiplayer client fails the same way, so the sim stays in sync.
/// </summary>
public static class Errors
{
    static readonly HashSet<string> Reported = [];

    /// <summary>Logs the first report for each place and shows the newest one in the overlay. Errors stay until the game restarts.</summary>
    public static void Report(string what, string message, Exception? ex = null)
    {
        lock (Reported)
        {
            if (!Reported.Add(what)) return;
        }
        Log.Error($"{what}: {message}", ex);
        Ui.Overlay.FatalError = $"{what}: {message}";
    }

    public static void Guard(string what, Action body)
    {
        try { body(); }
        catch (Exception ex) { Report(what, ex.Message, ex); }
    }
}

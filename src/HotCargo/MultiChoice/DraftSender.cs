using HotCargo.Core;

namespace HotCargo.MultiChoice;

/// <summary>
/// UI side of the shared drafts (spec 592): a select or clear goes out at once; a dragged slider sends at most every
/// <see cref="SendSeconds"/>, then its last value. Dialogs call <see cref="Flush"/> every frame. It only sends MP inputs.
/// </summary>
public static class DraftSender
{
    static readonly Dictionary<(string Domain, int Serial, int Option), int> Pending = new();
    static readonly Dictionary<(string Domain, int Serial, int Option), double> LastSend = new();
    const double SendSeconds = 0.25;

    public static void Toggle(Run run, string domain, int serial, int option, bool select, int value)
    {
        Pending.Remove((domain, serial, option));
        run.Game.NetManager.EnqueueInput(new MultiChoiceInput(domain, serial, option, select, value));
    }

    /// <summary>A new slider value of a selected option. It goes out now or with a later <see cref="Flush"/>.</summary>
    public static void Value(string domain, int serial, int option, int value)
    {
        Pending[(domain, serial, option)] = value;
        Flush();
    }

    public static void Flush()
    {
        if (Pending.Count == 0 || Run.Current is not { } run) return;
        double now = Halfling.App.Clock.Time.Seconds;
        // Old pages leave their send times behind; they matter only for SendSeconds.
        if (LastSend.Count > 256) LastSend.Clear();
        foreach (var (key, value) in Pending.ToList())
        {
            if (now - LastSend.GetValueOrDefault(key, double.MinValue) < SendSeconds) continue;
            run.Game.NetManager.EnqueueInput(new MultiChoiceInput(key.Domain, key.Serial, key.Option, true, value));
            LastSend[key] = now;
            Pending.Remove(key);
        }
    }
}

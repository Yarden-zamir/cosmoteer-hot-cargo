namespace HotCargo.SharedChoice;

/// <summary>
/// One open comms channel: a player ship (caller) talks to a ship (callee). Dialogue: the callee shows a dialogue tab to that caller.
/// OpenHere: this machine has its own comms window with the callee.
/// </summary>
public readonly record struct OpenComm<TShip, TId>(TShip Caller, TShip Callee, TId CallerId, TId CalleeId, int CallerPlayer, bool Dialogue, bool OpenHere)
    where TId : IComparable<TId>;

/// <summary>How the local player takes part in a hail: listen and vote (another player index), or join as a speaker (the same player index).</summary>
public enum HailRole { Listen, Join }

public readonly record struct Hail<TShip, TId>(OpenComm<TShip, TId> Comm, HailRole Role) where TId : IComparable<TId>;

/// <summary>Rules of co-op listen-in (spec 1131 to 1139). Pure, so the unit tests check them.</summary>
public static class ListenInModel
{
    /// <summary>
    /// The hails that the local player can take part in, one per speaker player index and callee (the speaker's lowest ship ID),
    /// in a stable order (callee ID, then caller ID). Channels without a dialogue tab do not count.
    /// Listen: a channel of another player index. Join: a channel of the local player index while another human shares that index
    /// (localIndexShared) and this machine has no comms window with the callee. Without a second human, the local player opened it.
    /// </summary>
    public static List<Hail<TShip, TId>> Hails<TShip, TId>(IEnumerable<OpenComm<TShip, TId>> comms, int localPlayer, bool localIndexShared)
        where TId : IComparable<TId> =>
        comms.Where(c => c.Dialogue && (c.CallerPlayer != localPlayer || localIndexShared && !c.OpenHere))
            .GroupBy(c => (c.CallerPlayer, c.CalleeId))
            .Select(g => g.OrderBy(c => c.CallerId).First())
            .OrderBy(c => c.CalleeId).ThenBy(c => c.CallerId)
            .Select(c => new Hail<TShip, TId>(c, c.CallerPlayer == localPlayer ? HailRole.Join : HailRole.Listen))
            .ToList();

    /// <summary>
    /// True when a listen window closes: lingerSeconds after the speaker's channel closed, so the listener sees the result.
    /// closedAt is null while the speaker's channel is open.
    /// </summary>
    public static bool CloseNow(double? closedAt, double now, float lingerSeconds) => closedAt is { } at && now - at >= lingerSeconds;
}

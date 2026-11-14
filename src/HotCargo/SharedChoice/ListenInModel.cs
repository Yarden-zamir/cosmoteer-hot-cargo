namespace HotCargo.SharedChoice;

/// <summary>One open comms channel: a player ship (caller) talks to a ship (callee). Dialogue: the callee shows a dialogue tab to that caller.</summary>
public readonly record struct OpenComm<TShip, TId>(TShip Caller, TShip Callee, TId CallerId, TId CalleeId, int CallerPlayer, bool Dialogue)
    where TId : IComparable<TId>;

/// <summary>Rules of co-op listen-in (spec 1131 to 1138). Pure, so the unit tests check them.</summary>
public static class ListenInModel
{
    /// <summary>
    /// The hails that the local player can listen to: channels of other players with a dialogue tab, one per speaker player and
    /// callee (the speaker's lowest ship ID), in a stable order (callee ID, then caller ID).
    /// </summary>
    public static List<OpenComm<TShip, TId>> Hails<TShip, TId>(IEnumerable<OpenComm<TShip, TId>> comms, int localPlayer)
        where TId : IComparable<TId> =>
        comms.Where(c => c.Dialogue && c.CallerPlayer != localPlayer)
            .GroupBy(c => (c.CallerPlayer, c.CalleeId))
            .Select(g => g.OrderBy(c => c.CallerId).First())
            .OrderBy(c => c.CalleeId).ThenBy(c => c.CallerId)
            .ToList();

    /// <summary>
    /// True when a listen window closes: lingerSeconds after the speaker's channel closed, so the listener sees the result.
    /// closedAt is null while the speaker's channel is open.
    /// </summary>
    public static bool CloseNow(double? closedAt, double now, float lingerSeconds) => closedAt is { } at && now - at >= lingerSeconds;
}

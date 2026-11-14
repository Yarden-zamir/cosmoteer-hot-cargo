using Halfling.Network;
using Halfling.Serialization;

namespace HotCargo.SharedChoice;

/// <summary>
/// Shared choice state of the run (spec 603, 607). Saved inside RunState, so a resync and a save carry the votes.
/// Hover markers are not here: they are UI state (Presence).
/// </summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class SharedChoiceState
{
    /// <summary>Co-op: true when shared choices go by vote, false when the first pick decides.</summary>
    [Serialize(Optional = true)] public bool VoteMode;
    /// <summary>Votes on open shared choice pages, one box for each page that has votes.</summary>
    [Serialize(Optional = true)] public List<VoteBox> Boxes = new();

    public VoteBox? Box(string domain, int serial) => Boxes.FirstOrDefault(b => b.Domain == domain && b.Serial == serial);
}

[ReflectiveSerialization(Explicit = true)]
public sealed class VoteBox
{
    [Serialize] public string Domain = "";
    [Serialize] public int Serial;
    /// <summary>Game seconds of the first vote, or -1 while nobody voted. The timeout counts from here.</summary>
    [Serialize] public double FirstVoteSeconds = -1;
    /// <summary>The host asked for a decision now. The force input decides by the most votes.</summary>
    [Serialize(Optional = true)] public bool Forced;
    /// <summary>At most one vote per player.</summary>
    [Serialize] public List<Vote> Votes = new();
}

[ReflectiveSerialization(Explicit = true)]
public sealed class Vote
{
    /// <summary>The voter. A messenger ID, not a player index: players that share ships share a player index but vote alone.</summary>
    [Serialize] public MessengerID Player;
    [Serialize] public int Option;
}

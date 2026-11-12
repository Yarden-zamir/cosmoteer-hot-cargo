using Halfling.Serialization;

namespace HotCargo.SharedChoice;

/// <summary>Co-op shared choices: the vote timeout and the hover markers (live reload).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class SharedChoiceTunables
{
    /// <summary>Vote mode: game seconds after the first vote on a page before the choice with the most votes wins. 0 turns the timeout off.</summary>
    [Serialize(Optional = true)] public float VoteTimeoutSeconds = 30f;
    /// <summary>Real seconds between two hover updates that one machine sends, at least. Lower is smoother and sends more.</summary>
    [Serialize(Optional = true)] public float PresenceSendSeconds = 0.15f;
    /// <summary>Real seconds without an update from a player before that player's hover marker goes away.</summary>
    [Serialize(Optional = true)] public float PresenceExpirySeconds = 3f;
}

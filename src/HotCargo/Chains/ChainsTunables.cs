using Halfling.Serialization;

namespace HotCargo.Chains;

/// <summary>Chain timing and frequency (live reload). Chain content is in mod/chains (restart).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ChainsTunables
{
    [Serialize(Optional = true)] public bool Enabled = true;
    /// <summary>Chance that a trade or military station gets a chain when the players first enter its system.</summary>
    [Serialize(Optional = true)] public float StationChance = 0.6f;
    /// <summary>Spec 160: at most this many stations of one system start a chain, so story offers do not crowd the mission boards.</summary>
    [Serialize(Optional = true)] public int MaxNewChainsPerSystem = 2;
    /// <summary>A page stage at a station opens when a player ship comes this close to the station.</summary>
    [Serialize(Optional = true)] public float PageTriggerDistance = 1500f;
    /// <summary>Game seconds after a system switch before a Return or Arrival stage starts, so the players see the system first.</summary>
    [Serialize(Optional = true)] public float LaterDelaySeconds = 8f;
    /// <summary>How many story entries the "story so far" line shows.</summary>
    [Serialize(Optional = true)] public int StoryLines = 2;
}

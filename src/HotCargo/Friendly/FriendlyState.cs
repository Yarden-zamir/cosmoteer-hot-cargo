using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Friendly;

/// <summary>Friendly area state, saved in RunState. Changes only in sim logic (ticks, choice inputs, the ship-junked hook).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class FriendlyState
{
    [Serialize(Optional = true)] public List<ApprenticeState> Apprentices = [];
    [Serialize(Optional = true)] public List<DebtorState> Debtors = [];
    /// <summary>The Wayfarers in this system (spec 1369), by ship ID. Empty while none help.</summary>
    [Serialize(Optional = true)] public List<ObjectID> Wayfarers = [];
    /// <summary>Systems where the Wayfarers' help roll already ran (spec 1370): one roll per system for the run.</summary>
    [Serialize(Optional = true)] public HashSet<ObjectID> HelpRolled = new();
    /// <summary>Total part health of the players' ships at the last help check (spec 1370), or -1 before the first.</summary>
    [Serialize(Optional = true)] public long PlayerHealth = -1;
    /// <summary>True once the Wayfarers came in this run (spec 1370): they come at most once per run.</summary>
    [Serialize(Optional = true)] public bool PayForwardDone;
    /// <summary>Run game seconds since no enemy is near the players while the Wayfarers help, or -1.</summary>
    [Serialize(Optional = true)] public double QuietSince = -1;
    /// <summary>Run game seconds of the Wayfarers' closing hail, or -1 before it.</summary>
    [Serialize(Optional = true)] public double HailedAt = -1;
}

/// <summary>A ship whose debt a player bought (spec 1367).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class DebtorState
{
    [Serialize] public ObjectID Ship;
    [Serialize] public int Player;
    /// <summary>The price that the player paid, in credits. The debtor pays it back in full.</summary>
    [Serialize] public int Credits;
    /// <summary>Run game seconds at which it pays.</summary>
    [Serialize] public double Until;
    /// <summary>The system of the ship. In another system it cannot die, so it pays at its time.</summary>
    [Serialize] public ObjectID Node;
    /// <summary>XML ship name, for messages after the ship is gone.</summary>
    [Serialize] public string Name = "";
}

/// <summary>An apprentice that follows a player (spec 1362).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ApprenticeState
{
    [Serialize] public ObjectID Ship;
    [Serialize] public int Player;
    /// <summary>Run game seconds at which it decides.</summary>
    [Serialize] public double Until;
    /// <summary>Enemy ships that died near it.</summary>
    [Serialize] public int Fights;
}

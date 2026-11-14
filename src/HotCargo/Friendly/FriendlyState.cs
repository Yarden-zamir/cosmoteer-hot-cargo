using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Friendly;

/// <summary>Friendly area state, saved in RunState. Changes only in sim logic (ticks, choice inputs, the ship-junked hook).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class FriendlyState
{
    [Serialize(Optional = true)] public List<ApprenticeState> Apprentices = [];
    [Serialize(Optional = true)] public List<DebtorState> Debtors = [];
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

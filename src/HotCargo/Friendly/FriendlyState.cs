using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Friendly;

/// <summary>Friendly area state, saved in RunState. Changes only in sim logic (ticks, choice inputs, the ship-junked hook).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class FriendlyState
{
    [Serialize(Optional = true)] public List<ApprenticeState> Apprentices = [];
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

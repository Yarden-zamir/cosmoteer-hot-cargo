using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Debris;

/// <summary>Sim state of the debris decay. Saved with the run, so a load and a co-op resync keep each piece's age.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class DebrisState
{
    /// <summary>Spec 1202: run game seconds at which each live junk piece first could decay, by ship ID.</summary>
    [Serialize(Optional = true)] public Dictionary<ObjectID, double> Since = new();
    /// <summary>Junk pieces that burned up in this run.</summary>
    [Serialize(Optional = true)] public int Removed;
}

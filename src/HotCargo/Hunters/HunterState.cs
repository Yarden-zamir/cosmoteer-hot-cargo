using Cosmoteer.Game;
using Halfling.Geometry;
using Halfling.Serialization;

namespace HotCargo.Hunters;

/// <summary>Saved hunter state of the current system visit (spec 191-192). A jump to another system clears it.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class HunterState
{
    /// <summary>Groups that arrive later, ordered by Due.</summary>
    [Serialize(Optional = true)] public List<PendingWave> Pending = new();
    /// <summary>Fuel that did not fit in a hunter's storage. It drops when that hunter is destroyed or becomes derelict.</summary>
    [Serialize(Optional = true)] public List<HeldFuel> HeldFuel = new();
}

[ReflectiveSerialization(Explicit = true)]
public sealed class PendingWave
{
    /// <summary>Run game seconds (RunState.GameSeconds) at which the group arrives.</summary>
    [Serialize] public double Due;
    [Serialize] public string Stage = "";
    [Serialize] public int Count;
    [Serialize] public int Tier;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class HeldFuel
{
    [Serialize] public ObjectID Ship;
    [Serialize] public int Amount;
    /// <summary>Last known location. A hunter destroyed to the last part leaves no ship, so the fuel drops here.</summary>
    [Serialize] public Vector2 Location;
}

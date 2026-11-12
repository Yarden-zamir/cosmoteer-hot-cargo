using Halfling.Serialization;

namespace HotCargo.ShipAi;

/// <summary>Player ship tactics (spec 400 to 412). The rows themselves live on each ship, not here.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ShipAiTunables
{
    /// <summary>Off: no ship runs its tactics. The rows stay on the ships and in saved designs.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;
    /// <summary>Game seconds between two evaluations of a ship's rows.</summary>
    [Serialize(Optional = true)] public float TickSeconds = 1f;
    /// <summary>Kite: hold the target at this fraction of the longest weapon range, edge to edge.</summary>
    [Serialize(Optional = true)] public float KiteRangeFraction = 0.9f;
    /// <summary>Close in: edge-to-edge distance to the target.</summary>
    [Serialize(Optional = true)] public float CloseInDistance = 100f;
    /// <summary>Retreat and repair, flee: the edge distance from the nearest enemy that the ship runs to. Retreat stops there.</summary>
    [Serialize(Optional = true)] public float RetreatDistance = 2000f;
    /// <summary>Hold position: the ship goes back to its spot when it drifts further than this.</summary>
    [Serialize(Optional = true)] public float HoldTolerance = 150f;
    /// <summary>Writes each tactics decision change to hot_cargo_log.txt.</summary>
    [Serialize(Optional = true)] public bool Log;
}

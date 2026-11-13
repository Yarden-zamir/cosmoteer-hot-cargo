using Halfling.Serialization;

namespace HotCargo.Autonomy;

/// <summary>Autonomous ships (spec 1050 to 1099): Funnels, mining and trade of ships that run on their AI rows.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class AutonomyTunables
{
    /// <summary>Game seconds between two Funnel pickups and two checks of the Computer-manned parts.</summary>
    [Serialize(Optional = true)] public float TickSeconds = 0.25f;
    /// <summary>A Funnel takes in loot up to this many tiles outside its own tiles.</summary>
    [Serialize(Optional = true)] public float FunnelReach = 1f;
    /// <summary>Mine and salvage: the edge distance that the ship keeps from its target.</summary>
    [Serialize(Optional = true)] public float MineDistance = 80f;
    /// <summary>Mine and salvage: at most this many open salvage orders on the target at a time.</summary>
    [Serialize(Optional = true)] public int SalvageJobs = 4;
    /// <summary>Buy never spends the owner's money below this amount.</summary>
    [Serialize(Optional = true)] public int BuyReserve = 5000;
    /// <summary>Go to station: the ship stops this far from the station hull, inside the vanilla trade distance (400).</summary>
    [Serialize(Optional = true)] public float DockDistance = 150f;
}

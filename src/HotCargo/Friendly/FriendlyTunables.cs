using Halfling.Serialization;

namespace HotCargo.Friendly;

/// <summary>Friendly encounter numbers (spec 1360 and later). Event text, costs and weights are in mod/events/events.rules.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class FriendlyTunables
{
    /// <summary>Game seconds between a deal and the jump-out of a ship that leaves after it (effect leave, spec 1364).</summary>
    [Serialize(Optional = true)] public float LeaveDelaySeconds = 5f;
    /// <summary>Riddle ship (spec 1366): heat keeps this share after a right answer.</summary>
    [Serialize(Optional = true)] public float RiddleHeatKeep = 0.5f;
    /// <summary>Apprentice (spec 1362): game minutes that it follows the players before it decides.</summary>
    [Serialize(Optional = true)] public float ApprenticeMinutes = 6f;
    /// <summary>Apprentice: enemy ships that must die near it while it follows, so it joins the fleet. Fewer: it thanks the players and leaves.</summary>
    [Serialize(Optional = true)] public int ApprenticeJoinFights = 3;
    /// <summary>Apprentice: an enemy ship that dies within this distance of the apprentice counts as a fight that it saw.</summary>
    [Serialize(Optional = true)] public float ApprenticeWatchRadius = 2500f;
}

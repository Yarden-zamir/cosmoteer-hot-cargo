using Halfling.Serialization;

namespace HotCargo.Friendly;

/// <summary>Friendly encounter numbers (spec 1360 and later). Event text, costs and weights are in mod/events/events.rules.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class FriendlyTunables
{
    /// <summary>Apprentice (spec 1362): game minutes that it follows the players before it decides.</summary>
    [Serialize(Optional = true)] public float ApprenticeMinutes = 6f;
    /// <summary>Apprentice: enemy ships that must die near it while it follows, so it joins the fleet. Fewer: it thanks the players and leaves.</summary>
    [Serialize(Optional = true)] public int ApprenticeJoinFights = 3;
    /// <summary>Apprentice: an enemy ship that dies within this distance of the apprentice counts as a fight that it saw.</summary>
    [Serialize(Optional = true)] public float ApprenticeWatchRadius = 2500f;
}

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
    /// <summary>Debtor (spec 1367): game minutes that it follows the players before it pays back the debt.</summary>
    [Serialize(Optional = true)] public float DebtorMinutes = 30f;
    /// <summary>
    /// Pay it forward (spec 1370): chance per qualifying fight (the first time the players are clearly outclassed in a system) that the
    /// Wayfarers come. They come at most once per run.
    /// </summary>
    [Serialize(Optional = true)] public float PayForwardChance = 0.15f;
    /// <summary>Pay it forward: "clearly outclassed" means enemy strength near the players is at least this many times the players' strength.</summary>
    [Serialize(Optional = true)] public float PayForwardOutclassRatio = 2f;
    /// <summary>
    /// Pay it forward: an actual fight means a player ship fired, or a near enemy fired and the players lost health, within this many
    /// game seconds. Outclassed without a fight (enemies near, nobody shooting) does not roll.
    /// </summary>
    [Serialize(Optional = true)] public float PayForwardFightSeconds = 5f;
    /// <summary>Pay it forward: enemies within this distance of a player ship count for the strength check.</summary>
    [Serialize(Optional = true)] public float PayForwardRadius = 3000f;
    /// <summary>Pay it forward: game seconds between checks.</summary>
    [Serialize(Optional = true)] public float PayForwardCheckSeconds = 2f;
    /// <summary>Pay it forward: game seconds with no enemy near the players before the Wayfarers hail and repair.</summary>
    [Serialize(Optional = true)] public float PayForwardQuietSeconds = 10f;
    /// <summary>Pay it forward: game seconds after the hail before the Wayfarers leave without an answer.</summary>
    [Serialize(Optional = true)] public float PayForwardLeaveSeconds = 60f;
    /// <summary>Pay it forward: the longest the Wayfarers fight for the players, in game minutes (their hire time).</summary>
    [Serialize(Optional = true)] public float PayForwardFightMinutes = 10f;
    /// <summary>Apprentice (spec 1362): game minutes that it follows the players before it decides.</summary>
    [Serialize(Optional = true)] public float ApprenticeMinutes = 6f;
    /// <summary>Apprentice: enemy ships that must die near it while it follows, so it joins the fleet. Fewer: it thanks the players and leaves.</summary>
    [Serialize(Optional = true)] public int ApprenticeJoinFights = 3;
    /// <summary>Apprentice: an enemy ship that dies within this distance of the apprentice counts as a fight that it saw.</summary>
    [Serialize(Optional = true)] public float ApprenticeWatchRadius = 2500f;
}

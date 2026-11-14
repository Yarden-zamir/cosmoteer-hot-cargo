using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Chains;

/// <summary>Chain state of the run. Saved inside RunState, so a save in the middle of a chain loads with it.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ChainsState
{
    /// <summary>One entry per station that got a chain, in the order they got it. The order is stable, so checks run deterministically.</summary>
    [Serialize(Optional = true)] public List<ChainInstance> Instances = new();
    /// <summary>How often the run handed out each chain, by chain ID. The pick prefers chains used least.</summary>
    [Serialize(Optional = true)] public Dictionary<string, int> Used = new();
    /// <summary>Systems whose stations already got their chains.</summary>
    [Serialize(Optional = true)] public HashSet<ObjectID> AssignedNodes = new();
}

/// <summary>The chain of one station: where it is, its current stage and what happened so far.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ChainInstance
{
    [Serialize] public string ChainId = "";
    [Serialize] public ObjectID NodeId;
    [Serialize] public ObjectID StationId;
    /// <summary>Player index of the station, for its faction (reputation results).</summary>
    [Serialize] public int StationPlayerIndex;
    /// <summary>The current stage ID. Empty when the chain ended.</summary>
    [Serialize] public string Stage = "";
    /// <summary>Stages finished so far. The comms label shows Part + 1.</summary>
    [Serialize(Optional = true)] public int Part;
    /// <summary>Chain flags that result rows set. Later result rows read them (IfFlags, NotFlags).</summary>
    [Serialize(Optional = true)] public HashSet<string> Flags = new();
    /// <summary>One line per finished stage. The comms description and chain pages show the last lines.</summary>
    [Serialize(Optional = true)] public List<string> Story = new();
    /// <summary>The stage mission (an offer in the station's list, or a mission in the log), or null.</summary>
    [Serialize(Optional = true)] public ObjectID? MissionId;
    /// <summary>
    /// Spec 1127: ships of the stage offer that would fight the players, held neutral until the players accept the stage. The value is
    /// the hostile allegiance that the accept gives back.
    /// </summary>
    [Serialize(Optional = true)] public Dictionary<ObjectID, int> HeldShips = new();
    /// <summary>Game seconds when the stage mission spawned. A mission that spawns straight into the log arrives a moment later.</summary>
    [Serialize(Optional = true)] public double MissionSince;
    /// <summary>Serial of the chain page that waits for a choice, or 0.</summary>
    [Serialize(Optional = true)] public int AwaitingSerial;
    /// <summary>A stage result that waits to apply (the mission left the log), or empty.</summary>
    [Serialize(Optional = true)] public string PendingResult = "";
    /// <summary>Sector index when the stage began. Arrival stages count sectors from here.</summary>
    [Serialize(Optional = true)] public int StageSector;
    /// <summary>True after the players left the station's system during this stage. Return stages wait for it.</summary>
    [Serialize(Optional = true)] public bool Away;
    /// <summary>Game seconds at which a Return or Arrival stage starts, or -1.</summary>
    [Serialize(Optional = true)] public double DueSeconds = -1;
    /// <summary>True: the stage waits for the next system visit (a declined offer, or a page that leads back to itself).</summary>
    [Serialize(Optional = true)] public bool HoldUntilNextVisit;
    /// <summary>Result text of the last stage, shown above the next page when that page opens right away.</summary>
    [Serialize(Optional = true)] public string Lead = "";

    public bool Ended => Stage.Length == 0;
}

using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Core;

public enum RunResult { Running, Won, Lost }

/// <summary>Everything Hot Cargo saves in the career save, under the "HotCargo" path.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class RunState
{
    [Serialize] public RunResult Result = RunResult.Running;
    [Serialize(Optional = true)] public string EndReason = "";
    /// <summary>False after a win. The career continues without pressure.</summary>
    [Serialize] public bool HeatEnabled = true;
    [Serialize] public float Heat;
    /// <summary>Stages already started in the current system visit.</summary>
    [Serialize] public int StagesStarted;
    [Serialize] public double SecondsSinceStrikeWave;
    /// <summary>Number of distinct systems visited. The first system is 1.</summary>
    [Serialize] public int SectorIndex = 1;
    [Serialize] public HashSet<ObjectID> VisitedNodes = new();
    [Serialize] public Dictionary<ObjectID, float> NodeHeat = new();
    /// <summary>Game time of the run, summed from simulation ticks across systems.</summary>
    [Serialize] public double GameSeconds;
    /// <summary>
    /// Tunables in effect. Saved so that multiplayer clients that join get the host's values with the save.
    /// Single player replaces them when tunables.rules changes.
    /// </summary>
    [Serialize] public Tunables Tunables = new();
    [Serialize(Optional = true)] public string TunablesHash = "";
    /// <summary>Last fleet tech count seen in a tick. Shown in the overlay and used by the heat multiplier.</summary>
    [Serialize(Optional = true)] public int FleetTech;
    /// <summary>Events area state: seen events, flags and the pending event page.</summary>
    [Serialize(Optional = true)] public Events.EventsState Events = new();
}

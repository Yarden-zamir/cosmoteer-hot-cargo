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
    /// <summary>Game seconds that the fleet tech count stayed at 0 while the count was certain (spec 12).</summary>
    [Serialize(Optional = true)] public double SecondsWithoutTech;
    /// <summary>Tech units that crew delivered to the Buyer, summed over all deliveries (spec 7).</summary>
    [Serialize(Optional = true)] public int DeliveredUnits;
    /// <summary>Run score (spec 7). Set at the win and updated by later Buyer deliveries.</summary>
    [Serialize(Optional = true)] public long Score;
    /// <summary>Spec 19: sum of every heat gain in the run. It never goes down.</summary>
    [Serialize(Optional = true)] public float Notoriety;
    /// <summary>Spec 19: highest low tier of a system that the players were in. It never goes down.</summary>
    [Serialize(Optional = true)] public int PeakTier = 1;
    /// <summary>Contract economy state: resolved terms of offers and open contracts (spec 100 to 108).</summary>
    [Serialize(Optional = true)] public Contracts.ContractsState Contracts = new();
    /// <summary>Events area state: seen events, flags and the pending event page.</summary>
    [Serialize(Optional = true)] public Events.EventsState Events = new();
    /// <summary>Chains area state: the chain of each station, its stage, flags and story.</summary>
    [Serialize(Optional = true)] public Chains.ChainsState Chains = new();
    /// <summary>Hunters area state: hunter groups that wait to arrive in the current system (spec 191).</summary>
    [Serialize(Optional = true)] public Hunters.HunterState Hunters = new();
    /// <summary>True after the Buyer station spawned in this run (spec 1), so that a later RunSectorCount change spawns no second Buyer.</summary>
    [Serialize(Optional = true)] public bool BuyerSpawned;
    /// <summary>Multiplies the Buyer price (spec 7). Events lower it (an advance from the Buyer's agent); 1 means no change.</summary>
    [Serialize(Optional = true)] public float BuyerPriceMultiplier = 1f;
}

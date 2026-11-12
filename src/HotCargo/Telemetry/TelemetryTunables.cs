using Halfling.Serialization;

namespace HotCargo.Telemetry;

/// <summary>
/// Telemetry switches (TELEMETRY.md). Telemetry reads game state only and never changes the simulation,
/// so these values can differ between co-op machines without a desync.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class TelemetryTunables
{
    /// <summary>Master switch. Off: no file, no events.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;

    // ---- Categories (event name prefixes in TELEMETRY.md)
    /// <summary>run.*, system.*: session, new run, load, save, jump, system entry, win, loss, quit.</summary>
    [Serialize(Optional = true)] public bool Run = true;
    /// <summary>heat.*: every heat change with its source, stage starts, strike waves, heat accrual sums.</summary>
    [Serialize(Optional = true)] public bool Heat = true;
    /// <summary>tech.*: fleet tech count changes with a cause, tech value steps, vault hits.</summary>
    [Serialize(Optional = true)] public bool Tech = true;
    /// <summary>econ.*: money and fame changes with a source, reputation, station trades with prices.</summary>
    [Serialize(Optional = true)] public bool Economy = true;
    /// <summary>mission.*: offers, accepts with contract terms, results with time to finish.</summary>
    [Serialize(Optional = true)] public bool Missions = true;
    /// <summary>event.*: event pages shown and choices with the rolled outcome. Chain calls come through Trace.</summary>
    [Serialize(Optional = true)] public bool Story = true;
    /// <summary>combat.*: ship spawns, census at system entry, derelict and captured ships, damage sums.</summary>
    [Serialize(Optional = true)] public bool Combat = true;
    /// <summary>ai.*: AI strategy and tactics module switches per ship (rate limited) and module activity sums.</summary>
    [Serialize(Optional = true)] public bool Ai = true;
    /// <summary>snap: a full run state summary every SnapshotSeconds.</summary>
    [Serialize(Optional = true)] public bool Snapshots = true;
    /// <summary>perf: frame time, sim tick time, GC, memory and telemetry overhead every PerfSeconds.</summary>
    [Serialize(Optional = true)] public bool Perf = true;
    /// <summary>mp.*: player count changes and desyncs.</summary>
    [Serialize(Optional = true)] public bool Coop = true;
    /// <summary>log: a copy of every hot_cargo_log.txt line, with the event context.</summary>
    [Serialize(Optional = true)] public bool Log = true;
    /// <summary>trace: calls of the methods in Trace, with their arguments, result and duration.</summary>
    [Serialize(Optional = true)] public bool Trace = true;

    // ---- Rates and limits
    /// <summary>Game seconds between two snapshots.</summary>
    [Serialize(Optional = true)] public float SnapshotSeconds = 30f;
    /// <summary>Real seconds between two perf events.</summary>
    [Serialize(Optional = true)] public float PerfSeconds = 10f;
    /// <summary>Game seconds over which continuous heat (time, trail) and combat damage are summed into one event.</summary>
    [Serialize(Optional = true)] public float AccrualSeconds = 5f;
    /// <summary>Game seconds between two scans for new mission offers and ships that spawned into stasis.</summary>
    [Serialize(Optional = true)] public float ScanSeconds = 5f;
    /// <summary>A ship logs at most one AI module switch per this many game seconds.</summary>
    [Serialize(Optional = true)] public float AiSwitchMinSeconds = 3f;
    /// <summary>At most this many AI switch events per real minute, over all ships. The sums in snapshots count every switch.</summary>
    [Serialize(Optional = true)] public int AiEventsPerMinute = 600;
    /// <summary>At most this many stack walks per real second to find the caller of a money or heat change. Later ones log caller "rate_limited".</summary>
    [Serialize(Optional = true)] public int CallerLookupsPerSecond = 50;
    /// <summary>A file that reaches this size continues in a new part file.</summary>
    [Serialize(Optional = true)] public int MaxFileMegabytes = 32;
    /// <summary>At session start the oldest files go until the telemetry folder is below this size.</summary>
    [Serialize(Optional = true)] public int MaxFolderMegabytes = 1024;

    /// <summary>
    /// Methods to trace, as "Namespace.Type:Method". Every overload of the name gets traced. A missing method is a warning
    /// (telemetry.warning event and the autotest field tele_trace_missing), not an error. A change needs a game restart.
    /// </summary>
    [Serialize(Optional = true)] public string[] TraceMethods =
    [
        "HotCargo.Hunters.HunterSpawner:StartStage",
        "HotCargo.Hunters.HunterSpawner:SpawnWave",
        "HotCargo.Hunters.HunterSpawner:PullNextWave",
        "HotCargo.Contracts.ContractBook:OnAccepted",
        "HotCargo.Contracts.ContractBook:SellOut",
        "HotCargo.Contracts.ContractBook:PairRival",
        "HotCargo.Events.EventRunner:Open",
        "HotCargo.Events.EventRunner:Choose",
        "HotCargo.Events.EventRunner:RollOutcome",
        "HotCargo.Events.EventRunner:Spawn",
        "HotCargo.Chains.ChainRunner:Start",
        "HotCargo.Chains.ChainRunner:SpawnMission",
        "HotCargo.Chains.ChainRunner:OpenPage",
        "HotCargo.Chains.ChainRunner:Resolve",
        "HotCargo.Journey.Buyer:Spawn",
        "HotCargo.Journey.Buyer:OnDelivered",
        "HotCargo.Missions.ShadyTrapObjective:Spring",
        "HotCargo.Missions.Mechanics.HoldOutObjective:SpawnWave",
    ];

    public void Validate(Action<bool, string> require)
    {
        require(SnapshotSeconds >= 1, "Telemetry.SnapshotSeconds must be 1 or more");
        require(PerfSeconds >= 1, "Telemetry.PerfSeconds must be 1 or more");
        require(AccrualSeconds >= 0.5f, "Telemetry.AccrualSeconds must be 0.5 or more");
        require(ScanSeconds >= 1, "Telemetry.ScanSeconds must be 1 or more");
        require(AiSwitchMinSeconds >= 0 && AiEventsPerMinute >= 0 && CallerLookupsPerSecond >= 0, "Telemetry rate limits must not be negative");
        require(MaxFileMegabytes >= 1 && MaxFolderMegabytes >= MaxFileMegabytes, "Telemetry.MaxFileMegabytes must be 1 or more and at most MaxFolderMegabytes");
        require(TraceMethods.All(m => m.Contains(':')), "Telemetry.TraceMethods entries need the form Namespace.Type:Method");
    }
}

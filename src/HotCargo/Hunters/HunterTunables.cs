using Halfling.Serialization;

namespace HotCargo.Hunters;

[ReflectiveSerialization(Explicit = true)]
public sealed class HunterTunables
{
    /// <summary>Response for each heat stage, matched to Heat.Stages by Name. The alert text is the string HotCargoUx/Stage{Name}.</summary>
    [Serialize(Optional = true)] public StageResponse[] Stages =
    [
        new() { Name = "Warning", ShipCount = 1, TierOffset = 0 },
        new() { Name = "Patrol", ShipCount = 2, TierOffset = 0 },
        new() { Name = "Hunt", ShipCount = 3, TierOffset = 1 },
        // Spec 190-191: 2 ships of tier + 1 in sector 1, 3 ships of tier + 2 in the last sector, one ship at a time.
        new() { Name = "Strike", ShipCount = 3, TierOffset = 2, EarlyScale = 0.67f, EarlyTierReduction = 1, Waves = 3, WaveGapSeconds = 45f },
    ];
    /// <summary>While heat stays at 100, the last stage sends a new wave this often (seconds).</summary>
    [Serialize(Optional = true)] public float StrikeWaveIntervalSeconds = 180f;
    [Serialize(Optional = true)] public float SpawnDistanceMin = 2000f;
    [Serialize(Optional = true)] public float SpawnDistanceMax = 3000f;
    /// <summary>All ships of one wave arrive from one direction, spread over this arc.</summary>
    [Serialize(Optional = true)] public float WaveArcDegrees = 40f;
    /// <summary>Faction IDs of the built-in ship pool for hunters. Empty means all factions.</summary>
    [Serialize(Optional = true)] public string[] ShipFactions = [];
    /// <summary>Built-in ship tags for hunters. A ship needs at least one of them.</summary>
    [Serialize(Optional = true)] public string[] ShipTags = ["combat"];
    /// <summary>Shown before the ship name, so players can tell hunters from other ships.</summary>
    [Serialize(Optional = true)] public string ShipNamePrefix = "Corporate";
    /// <summary>A wave spawns only enough ships to reach this many live hunters in the system. Keeps repeat strike waves from piling up without end.</summary>
    [Serialize(Optional = true)] public int MaxLiveHunters = 12;
    /// <summary>Spec 34: a player ship with a Tech Vault part counts this many times more as a target.</summary>
    [Serialize(Optional = true)] public float VaultTargetFactor = 3f;
    /// <summary>Spec 192: every hunter carries at least this much FTL fuel (hyperium). It drops or can be salvaged when the hunter dies.</summary>
    [Serialize(Optional = true)] public int MinFuel = 40;

    /// <summary>Called from TunablesFile.Validate (spec 53).</summary>
    public void Validate(Action<bool, string> require)
    {
        foreach (var r in Stages)
        {
            require(r.ShipCount >= 1, $"Hunters.Stages {r.Name}: ShipCount must be 1 or more");
            require(r.EarlyScale is > 0 and <= 1, $"Hunters.Stages {r.Name}: EarlyScale must be above 0 and at most 1");
            require(r.EarlyTierReduction >= 0, $"Hunters.Stages {r.Name}: EarlyTierReduction must not be negative");
            require(r.Waves >= 1, $"Hunters.Stages {r.Name}: Waves must be 1 or more");
            require(r.WaveGapSeconds >= 0, $"Hunters.Stages {r.Name}: WaveGapSeconds must not be negative");
        }
        require(MinFuel >= 0, "Hunters.MinFuel must not be negative");
    }
}

[ReflectiveSerialization(Explicit = true)]
public sealed class StageResponse
{
    [Serialize] public string Name = "";
    /// <summary>Ships in the last sector of the run.</summary>
    [Serialize(Optional = true)] public int ShipCount = 1;
    /// <summary>Tier above the sector tier in the last sector of the run.</summary>
    [Serialize(Optional = true)] public int TierOffset;
    /// <summary>Spec 190: ship count in sector 1 = ShipCount × EarlyScale. It rises linearly to ShipCount in the last sector.</summary>
    [Serialize(Optional = true)] public float EarlyScale = 1f;
    /// <summary>Spec 190: tier offset in sector 1 = TierOffset − EarlyTierReduction. It rises linearly to TierOffset in the last sector.</summary>
    [Serialize(Optional = true)] public int EarlyTierReduction;
    /// <summary>Spec 191: the ships arrive in this many groups, WaveGapSeconds apart. 1 = all at once.</summary>
    [Serialize(Optional = true)] public int Waves = 1;
    [Serialize(Optional = true)] public float WaveGapSeconds = 30f;
}

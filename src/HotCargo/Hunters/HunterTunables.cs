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
        new() { Name = "Strike", ShipCount = 3, TierOffset = 2 },
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
}

[ReflectiveSerialization(Explicit = true)]
public sealed class StageResponse
{
    [Serialize] public string Name = "";
    [Serialize(Optional = true)] public int ShipCount = 1;
    [Serialize(Optional = true)] public int TierOffset;
}

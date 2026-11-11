using Halfling.Serialization;

namespace HotCargo.Hunters;

[ReflectiveSerialization(Explicit = true)]
public sealed class HunterTunables
{
    /// <summary>Response for each heat stage, matched to Heat.Stages by Name.</summary>
    [Serialize(Optional = true)] public StageResponse[] Stages =
    [
        new() { Name = "Warning", ShipCount = 1, TierOffset = 0, Message = "Corporate scouts picked up your trail." },
        new() { Name = "Patrol", ShipCount = 2, TierOffset = 0, Message = "A corporate patrol is closing in." },
        new() { Name = "Hunt", ShipCount = 3, TierOffset = 1, Message = "Corporate hunters are here for the tech." },
        new() { Name = "Strike", ShipCount = 3, TierOffset = 2, Message = "Corporate strike force inbound!" },
    ];
    /// <summary>While heat stays at 100, the last stage sends a new wave this often (seconds).</summary>
    [Serialize(Optional = true)] public float StrikeWaveIntervalSeconds = 180f;
    [Serialize(Optional = true)] public float SpawnDistanceMin = 2000f;
    [Serialize(Optional = true)] public float SpawnDistanceMax = 3000f;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class StageResponse
{
    [Serialize] public string Name = "";
    [Serialize(Optional = true)] public int ShipCount = 1;
    [Serialize(Optional = true)] public int TierOffset;
    [Serialize(Optional = true)] public string Message = "";
}

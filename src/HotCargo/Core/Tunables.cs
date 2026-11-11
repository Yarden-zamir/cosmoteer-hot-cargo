using Halfling.Serialization;
using HotCargo.Events;
using HotCargo.Hunters;
using HotCargo.Journey;
using HotCargo.Missions;
using HotCargo.Tactics;
using HotCargo.Tech;
using HotCargo.Ux;

namespace HotCargo.Core;

/// <summary>
/// All numbers that the C# code reads. Loaded from tunables.rules in the mod folder.
/// Each area owns its section class in its own folder.
/// Missing fields keep the defaults written here.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class Tunables
{
    [Serialize(Optional = true)] public HeatTunables Heat = new();
    [Serialize(Optional = true)] public TechTunables Tech = new();
    [Serialize(Optional = true)] public JourneyTunables Journey = new();
    [Serialize(Optional = true)] public HunterTunables Hunters = new();
    [Serialize(Optional = true)] public MissionTunables Missions = new();
    [Serialize(Optional = true)] public TacticsTunables Tactics = new();
    [Serialize(Optional = true)] public EventsTunables Events = new();
    [Serialize(Optional = true)] public UxTunables Ux = new();
    [Serialize(Optional = true)] public DebugTunables Debug = new();
}

[ReflectiveSerialization(Explicit = true)]
public sealed class HeatTunables
{
    [Serialize(Optional = true)] public float HeatPerGameMinute = 2f;
    [Serialize(Optional = true)] public float HeatPerStandardMission = 15f;
    /// <summary>Index = fleet tech count. A count above the table uses the last entry.</summary>
    [Serialize(Optional = true)] public float[] HeatMultiplierByUnits = [0.25f, 0.45f, 0.6f, 0.75f, 0.9f, 1.05f, 1.2f];
    [Serialize(Optional = true)] public float HeatCarryOverFraction = 0.25f;
    /// <summary>Ordered by Heat. Hunters/HunterTunables.Stages has the response for each stage, matched by Name.</summary>
    [Serialize(Optional = true)] public StageThreshold[] Stages =
    [
        new() { Name = "Warning", Heat = 25f },
        new() { Name = "Patrol", Heat = 50f },
        new() { Name = "Hunt", Heat = 75f },
        new() { Name = "Strike", Heat = 100f },
    ];
}

[ReflectiveSerialization(Explicit = true)]
public sealed class StageThreshold
{
    [Serialize] public string Name = "";
    [Serialize] public float Heat;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class DebugTunables
{
    /// <summary>
    /// Single player only. F9 / Shift+F9 add or remove 10 heat, F10 starts the next stage, F11 / Shift+F11 add or remove 1 tech.
    /// With debug keys on, the heat panel also shows the heat value, the multiplier and the tunables hash.
    /// </summary>
    [Serialize(Optional = true)] public bool DebugKeys;
    /// <summary>Writes each hunter tactic decision to hot_cargo_log.txt. For tests and tuning.</summary>
    [Serialize(Optional = true)] public bool TacticsLog;
}

public static class HeatTunablesExtensions
{
    public static float[] Thresholds(this HeatTunables t) => t.Stages.Select(s => s.Heat).ToArray();
}

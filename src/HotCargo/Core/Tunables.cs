using Halfling.Serialization;
using HotCargo.Chains;
using HotCargo.Contracts;
using HotCargo.Events;
using HotCargo.Factions;
using HotCargo.Hunters;
using HotCargo.Journey;
using HotCargo.Missions;
using HotCargo.Missions.Mechanics;
using HotCargo.RunModes;
using HotCargo.SharedChoice;
using HotCargo.ShipAi;
using HotCargo.Tactics;
using HotCargo.Tech;
using HotCargo.Ux;
using HotCargo.Worlds;

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
    [Serialize(Optional = true)] public MechanicsTunables Mechanics = new();
    [Serialize(Optional = true)] public TacticsTunables Tactics = new();
    [Serialize(Optional = true)] public EventsTunables Events = new();
    [Serialize(Optional = true)] public ChainsTunables Chains = new();
    [Serialize(Optional = true)] public SharedChoiceTunables SharedChoice = new();
    [Serialize(Optional = true)] public ContractsTunables Contracts = new();
    [Serialize(Optional = true)] public UxTunables Ux = new();
    [Serialize(Optional = true)] public WorldsTunables Worlds = new();
    [Serialize(Optional = true)] public Diplomacy.DiplomacyTunables Diplomacy = new();
    [Serialize(Optional = true)] public ShipAiTunables ShipAi = new();
    [Serialize(Optional = true)] public Onboarding.OnboardingTunables Onboarding = new();
    [Serialize(Optional = true)] public RunModesTunables RunModes = new();
    [Serialize(Optional = true)] public FactionsTunables Factions = new();
    [Serialize(Optional = true)] public DebugTunables Debug = new();
}

/// <summary>How fast heat rises, the tech multiplier, the carry-over on a jump and the stage thresholds.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class HeatTunables
{
    /// <summary>Spec 24: heat per game minute at multiplier 1. About a quarter of a sector's heat comes from time (BALANCE.md).</summary>
    [Serialize(Optional = true)] public float HeatPerGameMinute = 1.2f;
    /// <summary>
    /// Spec 25: heat per standard mission fame unit at multiplier 1. A typical mission pays about 3.4 units (kill fame plus reward fame),
    /// so it adds about 5 heat, and six missions with the time and contract heat fill a sector.
    /// </summary>
    [Serialize(Optional = true)] public float HeatPerStandardMission = 1.5f;
    /// <summary>Heat multiplier for each fleet tech count, from 0 units up. A count above the table uses the last entry.</summary>
    [Serialize(Optional = true)] public float[] HeatMultiplierByUnits = [0.25f, 0.4f, 0.52f, 0.64f, 0.76f, 0.88f, 1.0f, 1.1f, 1.2f];
    /// <summary>Share of the heat that stays after a switch to a system where own ships wait, without a jump (0 to 1). Jumps use the Factions heat cuts.</summary>
    [Serialize(Optional = true)] public float HeatCarryOverFraction = 0.25f;
    /// <summary>Heat stages, ordered by Heat. Hunters.Stages has the response for each stage, matched by Name.</summary>
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
    /// <summary>Stage name. Hunters.Stages and the tactic minimum stages refer to it.</summary>
    [Serialize] public string Name = "";
    /// <summary>Heat at which the stage starts (0 to 100).</summary>
    [Serialize] public float Heat;
}

/// <summary>Debug keys and test logs.</summary>
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

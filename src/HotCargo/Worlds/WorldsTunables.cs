using Halfling.Serialization;

namespace HotCargo.Worlds;

/// <summary>
/// Numbers of the system archetypes (WORLDS.md, spec 220 to 229). The archetype weights are rules values in
/// mod/worlds/archetypes.rules, because the galaxy map is made once per career.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class WorldsTunables
{
    /// <summary>Spec 221: the speed of the current along its flow, in world units per second. Ships slower than this downstream get pushed.</summary>
    [Serialize(Optional = true)] public float CurrentFlowSpeed = 45f;
    /// <summary>Spec 221: the push of the current on a ship at rest, in world units per second squared, at full cloud strength.</summary>
    [Serialize(Optional = true)] public float CurrentAccel = 9f;
    /// <summary>
    /// Spec 221: the sideways part of the drift, as a share of the flow. The current also pushes across the flow, toward the
    /// nearer side of the band, at this share of the flow speed and push, so a drifting ship or item leaves the band. 0 turns it off.
    /// Above 0.4, drift from the band center leaves at the side (half width 1200 over half length 3000).
    /// </summary>
    [Serialize(Optional = true)] public float CurrentSideShare = 0.5f;
    /// <summary>Spec 221: draw flow arrows over the current.</summary>
    [Serialize(Optional = true)] public bool CurrentArrows = true;

    /// <summary>Spec 222: seconds that a player ship must hold the pass, after the campers are gone.</summary>
    [Serialize(Optional = true)] public float PassHoldSeconds = 120f;
    /// <summary>Spec 222: seconds the pass may stay empty after the hold started, before the mission fails.</summary>
    [Serialize(Optional = true)] public float PassLostSeconds = 15f;
    /// <summary>Spec 222: seconds of holding between raider waves. The first wave comes after one interval.</summary>
    [Serialize(Optional = true)] public float PassWaveSeconds = 35f;
    /// <summary>Spec 222: distance from the pass center at which a wave appears.</summary>
    [Serialize(Optional = true)] public float PassWaveDistance = 2500f;

    /// <summary>Spec 223: seconds a player ship must stay at the sundive wreck.</summary>
    [Serialize(Optional = true)] public float SundiveSalvageSeconds = 5f;
    /// <summary>Spec 223: heat zone depth (0 = edge, 1 = kill radius) above which the HUD line turns red.</summary>
    [Serialize(Optional = true)] public float HeatWarnDepth = 0.5f;

    /// <summary>Spec 224: a player ship this close to the lair finds it.</summary>
    [Serialize(Optional = true)] public float LairDiscoverDistance = 2000f;

    /// <summary>Spec 225: the chat line that names the system type and its hazard on the first arrival.</summary>
    [Serialize(Optional = true)] public bool ArrivalLine = true;
    /// <summary>Spec 225: seconds after the jump before the arrival line shows. The jump shows the system name first.</summary>
    [Serialize(Optional = true)] public float ArrivalLineDelaySeconds = 4f;
}

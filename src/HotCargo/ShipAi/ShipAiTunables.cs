using Halfling.Serialization;

namespace HotCargo.ShipAi;

/// <summary>Player ship tactics (spec 400 to 412). The rows themselves live on each ship, not here.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ShipAiTunables
{
    /// <summary>Off: no ship runs its tactics. The rows stay on the ships and in saved designs.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;
    /// <summary>Game seconds between two evaluations of a ship's rows.</summary>
    [Serialize(Optional = true)] public float TickSeconds = 1f;
    /// <summary>Kite: hold the target at this fraction of the longest weapon range, edge to edge.</summary>
    [Serialize(Optional = true)] public float KiteRangeFraction = 0.9f;
    /// <summary>Close in: edge-to-edge distance to the target.</summary>
    [Serialize(Optional = true)] public float CloseInDistance = 100f;
    /// <summary>Retreat and repair, flee: the edge distance from the nearest enemy that the ship runs to. Retreat stops there.</summary>
    [Serialize(Optional = true)] public float RetreatDistance = 2000f;
    /// <summary>Hold position: the ship goes back to its spot when it drifts further than this.</summary>
    [Serialize(Optional = true)] public float HoldTolerance = 150f;
    /// <summary>
    /// Anti-flicker: a threshold condition that holds keeps holding until its value passes the threshold by this much.
    /// Percent conditions: this × 100 points. Distance and seconds conditions: this fraction of the threshold. 0 turns it off.
    /// </summary>
    [Serialize(Optional = true)] public float HysteresisMargin = 0.03f;
    /// <summary>Anti-flicker: a threshold condition keeps a new value at least this many game seconds.</summary>
    [Serialize(Optional = true)] public float MinHoldSeconds = 2f;
    /// <summary>Collect tech: the ship flies to the nearest tech crate until its hull is this close to the crate.</summary>
    [Serialize(Optional = true)] public float CollectDistance = 150f;
    /// <summary>
    /// "Fire: disruptors" uses the weapons of these parts. Limit: no rule marks a weapon as a disruptor, so this list names them.
    /// Revisit if the game rules gain such a flag.
    /// </summary>
    [Serialize(Optional = true)] public string[] DisruptorParts = ["cosmoteer.disruptor", "cosmoteer.ion_beam_emitter", "cosmoteer.ion_beam_prism"];
    /// <summary>"Fire: disruptors" also uses weapons whose part stores one of these resources (missile launchers set to EMP missiles).</summary>
    [Serialize(Optional = true)] public string[] DisruptorAmmo = ["missile_part_emp"];
    /// <summary>Writes each tactics decision change to hot_cargo_log.txt.</summary>
    [Serialize(Optional = true)] public bool Log;
}

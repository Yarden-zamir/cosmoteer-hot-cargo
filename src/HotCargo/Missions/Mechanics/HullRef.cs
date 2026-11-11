using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling.Geometry;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HotCargo.Core;

namespace HotCargo.Missions.Mechanics;

/// <summary>
/// A reference to one ship of one system by ID, live or in stasis. Unlike ObjectiveTargetShip it keeps a ship that becomes
/// a wreck (junk) or changes owner, which the tow, capture and prototype missions need: a wreck is their normal state.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class HullRef
{
    [Serialize] public ObjectID NodeId { get; private set; }
    [Serialize] public ObjectID ShipId { get; set; }
    /// <summary>Name at creation, for text when the ship is not in the current system.</summary>
    [Serialize] public string Name { get; private set; }

    public HullRef(SimRoot sim, ObjectID shipId)
    {
        NodeId = sim.NodeId();
        ShipId = shipId;
        Name = sim.Ships[shipId]?.Metadata.PlainShipName ?? sim.Stasis[shipId]?.ObjectName
            ?? throw new InvalidOperationException($"no ship {shipId} in this system");
    }

    [GenericConstructor]
    HullRef(GenericSerialReader reader)
    {
        Name = "";
        reader.ReflectiveRead(this);
    }

    public bool InSystem(SimRoot? sim) => sim?.MapNode?.UniqueID == NodeId;

    /// <summary>The live ship, or null when it is in stasis, gone or in another system.</summary>
    public Ship? Live(SimRoot? sim) => InSystem(sim) ? sim!.Ships[ShipId] : null;

    /// <summary>False only when the current sim is the ship's system and the ship is neither live nor in stasis.</summary>
    public bool Exists(SimRoot? sim) => !InSystem(sim) || sim!.Ships[ShipId] is not null || sim.Stasis[ShipId] is not null;

    public Vector2? Location(SimRoot? sim) =>
        !InSystem(sim) ? null : sim!.Ships[ShipId]?.DetWorldCenter ?? (sim.Stasis[ShipId] is StasisSpawner s ? s.Location : null);

    public string XmlName(GameRoot game) =>
        Halfling.StringTools.EscapeForXml(Live(game.Sim)?.Metadata.PlainShipName ?? Name);
}

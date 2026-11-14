using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HotCargo.Core;

namespace HotCargo.Scavenge;

/// <summary>
/// Spec 1342: the scavenger ship. No vanilla built-in ship has a mining laser (checked against all 1,268 in 0.30.5a), so the design is
/// built here from vanilla parts around a corridor spine (x = 3) that every room opens onto, with an airlock at its end so the crew can
/// fetch salvage: two small mining lasers and one small laser
/// blaster at the nose, a small control room, four small crew quarters, a small reactor, two storage bays and small thrusters.
/// Rotation 0 faces the nose (up, -y). The doors come from the game's automatic door rule.
/// </summary>
internal static class ScavengerShip
{
    internal static readonly ID<ShipAIRules> AiId = new("HotCargoScavenger");

    /// <summary>The design: part ID, cell, rotation (0 to 3, quarter turns clockwise). Parts go in this order, each next to one before it.</summary>
    static readonly (string Part, int X, int Y, int Rot)[] Design =
    [
        ("cosmoteer.corridor", 3, -3, 0),
        ("cosmoteer.corridor", 3, -2, 0),
        ("cosmoteer.corridor", 3, -1, 0),
        ("cosmoteer.corridor", 3, 0, 0),
        ("cosmoteer.corridor", 3, 1, 0),
        ("cosmoteer.corridor", 3, 2, 0),
        ("cosmoteer.corridor", 3, 3, 0),
        ("cosmoteer.corridor", 3, 4, 0),
        ("cosmoteer.corridor", 3, 5, 0),
        ("cosmoteer.mining_laser_small", 1, -3, 0),
        ("cosmoteer.mining_laser_small", 4, -3, 0),
        ("cosmoteer.laser_blaster_small", 3, -6, 0),
        ("cosmoteer.control_room_small", 4, 0, 0),
        ("cosmoteer.crew_quarters_small", 2, 0, 0),
        ("cosmoteer.crew_quarters_small", 1, 0, 0),
        ("cosmoteer.crew_quarters_small", 2, 2, 0),
        ("cosmoteer.crew_quarters_small", 1, 2, 0),
        ("cosmoteer.reactor_small", 4, 2, 0),
        ("cosmoteer.storage_2x2", 4, 4, 0),
        ("cosmoteer.storage_2x2", 1, 4, 0),
        ("cosmoteer.thruster_small", 1, 6, 0),
        ("cosmoteer.thruster_small", 2, 6, 0),
        ("cosmoteer.airlock", 3, 6, 2),
        ("cosmoteer.thruster_small", 4, 6, 0),
        ("cosmoteer.thruster_small", 5, 6, 0),
        ("cosmoteer.thruster_small", -1, 0, 1),
        ("cosmoteer.thruster_small", -1, 2, 1),
        ("cosmoteer.thruster_small", 6, 0, 3),
        ("cosmoteer.thruster_small", 6, 2, 3),
    ];

    static byte[]? _designBytes;

    /// <summary>The ship rules that hold every part of the design (the terran rules in 0.30.5a), found from the rules, not named.</summary>
    static ShipRules Rules() => GameApp.Rules.Ships.FirstOrDefault(r => !r.IsAsteroid && Design.All(d => r.PartsByID.ContainsKey(new ID<PartRules>(d.Part))))
        ?? throw new InvalidOperationException("scavenger design: no ship rules hold all its parts");

    /// <summary>The saved design, built once per process from the rules. Throws when a part does not fit, so a rules change shows at once.</summary>
    static byte[] DesignBytes()
    {
        if (_designBytes is { } cached) return cached;
        var rules = Rules();
        var ship = new Ship(rules, "Scavenger", 0, randSeed: 1UL);
        // One update per part: the automatic door rule adds doors only toward parts that the ship already has.
        foreach (var (part, x, y, rot) in Design)
        {
            using var sui = ShipUpdateInfo.Alloc(ship);
            var partRules = rules.PartsByID[new ID<PartRules>(part)];
            if (!sui.TryAddBlueprintPart(partRules, new IntVector2(x, y), rot, flipX: false, mirrorPhysical: false, allowOverwrite: false, addDoors: true,
                    hireCrew: false, out var reasons, force: false))
                throw new InvalidOperationException($"scavenger design: cannot add {part} at {x},{y} rot {rot}: {reasons}");
            sui.Apply();
        }
        ship.MatchPhysicalToBlueprints();
        using var stream = new MemoryStream();
        ship.SaveDesign(stream);
        return _designBytes = stream.ToArray();
    }

    /// <summary>
    /// Spec 1342, 1343: a full scavenger with crew, power and empty storage bays, the scavenger AI and the faction's player index, at the
    /// spot. The object ID and the random seed come from the sim random source, so every co-op machine builds the same ship.
    /// </summary>
    internal static Ship Spawn(SimRoot sim, Vector2 at, Direction facing)
    {
        ObjectID id;
        do id = ObjectID.NewRandom(sim.Rand); while (!sim.IsObjectIDAvailable(id));
        ulong seed = sim.Rand.UInt64();
        var ship = Ship.Load(DesignBytes(), Name(sim), Scavengers.PlayerIndex, spawnWithCrew: true, initializeToMax: true, randSeed: seed, id: id);
        ship.WorldCenter = at;
        ship.WorldRotation = facing - (float)ship.FlightDirection;
        ship.AI = new ShipAI(GameApp.Rules.ShipAIs[AiId], null);
        sim.Ships.Add(ship);
        Log.Info($"scavengers: {ship.Metadata.PlainShipName} id={ship.UniqueID} arrives at {at.X.ToString("0", CultureInfo.InvariantCulture)},{at.Y.ToString("0", CultureInfo.InvariantCulture)} " +
                 $"crew={ship.Crew.Assigned.Count} cargo={Autonomy.AutonomyWorld.CargoFill(ship)?.ToString("0.00", CultureInfo.InvariantCulture) ?? "none"}");
        return ship;
    }

    /// <summary>"Scavenger" and a number that no scavenger in the system has (live or in stasis), so players can tell them apart.</summary>
    static string Name(SimRoot sim)
    {
        var taken = Scavengers.Names(sim);
        for (int n = 1; ; n++)
            if (!taken.Contains($"Scavenger {n}")) return $"Scavenger {n}";
    }
}

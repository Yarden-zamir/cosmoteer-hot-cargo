using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using HotCargo.Core;

namespace HotCargo.Factions;

/// <summary>
/// Rules type "HotCargoControllerFaction" (spec 663): a group like a None spawner whose sub-spawners take the faction that
/// controls the system (MapNode.ControllingFaction), so every system has a station of its controller.
/// It needs SynchronizeFactions = true: the game then gives the synchronized faction to the whole subtree and clears it when
/// the group ends (SimObjectSpawner.Spawn). Without a controller, or when the controller has no built-in ship with ShipTags,
/// the sub-spawners roll the faction as before.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoControllerFaction")]
[TrackMultipleReferences]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class ControllerFactionSpawner : SimObjectSpawner
{
    /// <summary>The ship tags that the sub-spawners pick (for example trade_station). The controller needs a ship with them.</summary>
    [Serialize, PortableHash] public HashSet<ID<BuiltinShipTag>> ShipTags = null!;

    public override void DoSpawn(SimRoot sim, Rand rand, int wholeSpawnSeed, int index, int totalCount, object? spawnData, Vector2 loc,
        List<SpawnedObject> parentTree, SpawnedObjectCollection spawnedObjects, float areaRadius, bool loadInstantly,
        List<(ObjectID? ObjectID, Ship? Ship, StasisSpawner? StasisSpawner, int? PlayerIndex)> spawned)
    {
        if (!SynchronizeFactions) throw new InvalidDataException("HotCargoControllerFaction needs SynchronizeFactions = true");
        if (ShipTags is not { Count: > 0 }) throw new InvalidDataException("HotCargoControllerFaction needs ShipTags");
        if (sim.MapNode.ControllingFaction is { } controller && GameApp.Rules.BuiltinShips.HasMatchingShips([controller], tags: ShipTags))
            s_synchronizedFaction = controller;
        else
            Log.Info($"controller station: {sim.MapNode.Name} has no controller with a ship tagged [{string.Join(", ", ShipTags)}]; the faction rolls");
        // Like a None spawner: one spawned point, so tags and sub-spawners work.
        spawned.Add(default);
    }
}

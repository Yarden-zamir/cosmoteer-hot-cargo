using Cosmoteer.Data;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Ships.AI;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling.PortableHashing;
using Halfling.Serialization;
using Halfling.Timing;

using HotCargo.Core;

namespace HotCargo.Hunters;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcHuntStasis")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class HuntStasisRules : AIStasisModuleRules
{
    [Serialize, PortableHash] public float DefaultStasisSpeed;
    [Serialize(Optional = true), PortableHash] public HashSet<ID<SimObjectSpawner>>? AvoidableDoodadTags;
    [Serialize(Optional = true), PortableHash] public float AvoidableDoodadBuffer;

    public override AIStasisModule CreateModule(AIParameters? parameters) => new HuntStasis(this);
}

/// <summary>
/// A hunter far from every player ship goes into stasis. This module keeps it moving toward the nearest player ship
/// there, so it comes back into the live area. Same movement call as AIStasisWantedSearchModule.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class HuntStasis(HuntStasisRules rules) : AIStasisModule
{
    StasisSpawner.DestAvoidanceState _avoidState;

    public override bool StasisUpdate(SimRoot sim, StasisSpawner spawner, AIParameters? parameters, Time interval)
    {
        var target = SimQueries.NearestHumanShip(sim, spawner.Location);
        if (target is null) return false;
        float speed = parameters?.StasisSpeed ?? rules.DefaultStasisSpeed;
        spawner.MoveTowards(target.DetWorldCenter, speed, sim, interval, rules.AvoidableDoodadTags, nebulaPathfinding: true, ref _avoidState, rules.AvoidableDoodadBuffer);
        return true;
    }
}

using Cosmoteer;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using Halfling.Serialization.Generic;

namespace HotCargo.Missions;

/// <summary>
/// Rules type "HotCargoEscort" (spec requirement 36): completes when the escorted AI ship is within ArrivalRadius of the destination.
/// While the mission is active and the escort is live (near a player), the objective points the escort AI at the destination.
/// The escort does not move in stasis, so it waits when the players leave it behind.
/// Put it in an AnyOrder next to a ProtectShips on the same ship: ProtectShips fails the mission if the escort dies.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoEscort")]
internal sealed class EscortObjective : Objective
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoEscort")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        [Serialize, PortableHash] public SpawnedObjectSearch EscortTag = null!;
        /// <summary>A POI doodad that marks the destination. The objective reveals it on acceptance.</summary>
        [Serialize, PortableHash] public SpawnedObjectSearch DestinationTag = null!;
        [Serialize(Optional = true), PortableHash] public float ArrivalRadius = 400f;
        [Serialize(Optional = true), PortableHash] public int TierOffset;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            // A missing tag means the escort group could not place its destination. Drop the offer instead of crashing generation.
            invalid = !objects.TryFindObject(EscortTag, loc, rand, out var escort) | !objects.TryFindObject(DestinationTag, loc, rand, out var dest);
            if (invalid) return null;
            ObjectID escortId = escort!.UniqueID ?? throw new InvalidOperationException($"escort tag {EscortTag} found an object without an ID");
            var mode = (CareerSimModeManager)sim.Mode;
            Vector2 destination = dest!.Location.Center;
            return new EscortObjective(new ObjectiveTargetShip(sim.RequireGame(), sim.NodeId(), escortId), destination, ArrivalRadius)
            {
                DestinationPoiID = dest.UniqueID,
                Tier = Math.Max(1, mode.TierFields.GetTier(destination) + TierOffset),
            };
        }
    }

    [Serialize] public ObjectiveTargetShip Escort { get; private set; }
    [Serialize] public Vector2 Destination { get; private set; }
    [Serialize] public float ArrivalRadius { get; private set; }
    [Serialize(Optional = true)] public ObjectID? DestinationPoiID { get; private set; }
    [Serialize(Optional = true)] public int Tier { get; private set; }

    public override string ScriptTypeName => "HotCargoEscort";

    public EscortObjective(ObjectiveTargetShip escort, Vector2 destination, float arrivalRadius)
    {
        Escort = escort ?? throw new ArgumentNullException(nameof(escort));
        if (arrivalRadius <= 0f) throw new ArgumentOutOfRangeException(nameof(arrivalRadius), arrivalRadius, "must be above 0");
        Destination = destination;
        ArrivalRadius = arrivalRadius;
    }

    [GenericConstructor]
    EscortObjective(GenericSerialReader reader)
    {
        Escort = null!;
        reader.ReflectiveRead(this);
    }

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        Escort.GetIntegrityHash(iHash);
        Tier.GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => Escort.MapNode;

    public override Circle? GetAreaLocation(GameRoot game) =>
        Completion == CompletionState.InProgress && game.Sim?.MapNode?.UniqueID == Escort.MapNodeID ? new Circle(Destination, ArrivalRadius) : null;

    public override bool IsShipAssociated(MetaShip ship) => Completion == CompletionState.InProgress && Escort.TargetMetaShip == ship;

    public override bool IsPOIAssociated(POIDoodad poi) => Completion == CompletionState.InProgress && poi.UniqueID == DestinationPoiID;

    public override bool CheckExpired(GameRoot game) => Completion == CompletionState.Failed || !Escort.IsAlive;

    public override void GetStringArgs(Dictionary<string, object?> args, GameRoot game)
    {
        base.GetStringArgs(args, game);
        args["escort_ship"] = Escort.GetXmlTargetName(game.Sim);
    }

    public override bool CanFocusDisplayLine(GameRoot game) => Escort.CanFocus();

    public override void OnDisplayLineFocused(GameRoot game) => Escort.OnFocused();

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState) =>
        lines.Add(new DisplayLine
        {
            Text = Strings.KeyStringFmt("HotCargoObjectives/EscortFmt", Escort.GetXmlTargetName(game.Sim)),
            State = Completion,
            CanFocus = CanFocusDisplayLine,
            OnFocused = OnDisplayLineFocused,
        });

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        if (DestinationPoiID is { } poiId && game.Sim?.Doodads[poiId] is POIDoodad poi) poi.IsUndiscovered = false;
        Escort.Killed += OnEscortKilled;
        game.EventHub.Tick += OnTick;
        if (!isLoading && !Escort.IsAlive) OnEscortKilled(null, EventArgs.Empty);
    }

    public override void UnregisterEvents(GameRoot game)
    {
        Escort.Killed -= OnEscortKilled;
        game.EventHub.Tick -= OnTick;
    }

    void OnEscortKilled(object? sender, EventArgs e)
    {
        if (Completion == CompletionState.InProgress) Fail();
    }

    void OnTick(object? sender, SimRoot sim)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim != Mission.Game.Sim) return;
        if (sim.NodeId() != Escort.MapNodeID || Escort.Location is not { } at) return;
        if (Escort.TargetShip?.AI is { } ai) SteerToDestination(ai);
        if (at.DistanceTo(Destination) <= ArrivalRadius)
        {
            Log.Info($"escort {Escort.GetPlainTargetName(sim)} arrived");
            Complete();
        }
    }

    /// <summary>
    /// Moves the AI origin to the destination. The escort rules give a small TargetUnacquireRangeFromOrigin,
    /// so AggroEnemies keeps flying it to the origin and fights only while it moves.
    /// </summary>
    void SteerToDestination(ShipAI ai)
    {
        if (ai.Parameters?.PatrolOrigin is { } origin && origin == Destination) return;
        AIParameters p = ai.Parameters?.Clone() ?? new AIParameters();
        p.PatrolOrigin = Destination;
        ai.Parameters = p;
    }

    public override int GetTypeIntegrityHash() => 0x4843_4553; // "HCES"
}

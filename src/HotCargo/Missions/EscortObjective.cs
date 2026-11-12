using Cosmoteer;
using Cosmoteer.Data;
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
using HotCargo.Core;

namespace HotCargo.Missions;

/// <summary>
/// Rules type "HotCargoEscort" (spec requirement 36): completes when the escorted AI ship is within ArrivalRadius of the destination.
/// While the mission is active and the escort is live (near a player), the objective points the escort AI at the destination.
/// The escort does not move in stasis, so it waits when the players leave it behind.
/// Put it in an AnyOrder next to a ProtectShips on the same ship: ProtectShips fails the mission if the escort dies.
/// At creation it moves the destination POI to MissionTunables.EscortRouteShare of the gate ring. When the escort covers
/// EscortAmbushAt of the route, it spawns the raiders from missions/convoy_ambush.rules ahead of the escort.
/// It removes the POI when it completes or fails.
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
        /// <summary>The straight route keeps this distance from every sun.</summary>
        [Serialize(Optional = true), PortableHash] public float SunClearance = 6000f;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            // A missing tag means the escort group could not place its destination. Drop the offer instead of crashing generation.
            invalid = !objects.TryFindObject(EscortTag, loc, rand, out var escort) | !objects.TryFindObject(DestinationTag, loc, rand, out var dest);
            if (invalid) return null;
            ObjectID escortId = escort!.UniqueID ?? throw new InvalidOperationException($"escort tag {EscortTag} found an object without an ID");
            ObjectID poiId = dest!.UniqueID ?? throw new InvalidOperationException($"destination tag {DestinationTag} found an object without an ID");
            // POI doodads never go into stasis (POIDoodadRules.CanSpawnIntoStasis), so the spawned POI is live.
            var poi = sim.Doodads[poiId] as POIDoodad ?? throw new InvalidOperationException($"destination {poiId} is not a live POI doodad");
            Vector2 start = escort.Location.Center;
            if (Route(sim, objects, start, poi.Location) is not { } destination)
            {
                invalid = true;
                return null;
            }
            poi.Location = destination;
            var mode = (CareerSimModeManager)sim.Mode;
            return new EscortObjective(new ObjectiveTargetShip(sim.RequireGame(), sim.NodeId(), escortId), destination, ArrivalRadius)
            {
                DestinationPoiID = poiId,
                Start = start,
                AmbushSeed = rand.UInt64(),
                Tier = Math.Max(1, mode.TierFields.GetTier(destination) + TierOffset),
            };
        }

        /// <summary>
        /// The destination at EscortRouteShare of the nearest gate's distance from the center (spec 36, spec 105: inside the gate ring).
        /// The first direction tried is toward the rules POI. Null drops the offer: no gate, or no direction fits.
        /// </summary>
        Vector2? Route(SimRoot sim, SpawnedObjectCollection objects, Vector2 start, Vector2 rulesDestination)
        {
            List<System.Numerics.Vector2> Tagged(string tag) =>
                objects.TryGetValue(new ID<SimObjectSpawner>(tag), out var list) ? [.. list.Select(o => N(o.Location.Center))] : [];
            var gates = Tagged("ftl_gate");
            if (gates.Count == 0)
            {
                Log.Info("escort offer dropped: no FTL gate in this system");
                return null;
            }
            float reach = gates.Min(g => g.Length());
            float length = MissionArea.TunablesFor(sim.RequireGame()).EscortRouteShare * reach;
            Vector2 toward = rulesDestination - start;
            if (toward == Vector2.Zero) toward = new Vector2(1f, 0f);
            if (MissionModel.ConvoyDestination(N(start), N(toward), length, reach, SunClearance, Tagged("sun")) is { } d) return new Vector2(d.X, d.Y);
            Log.Info($"escort offer dropped: no route of {length:0} from {start} inside {reach:0}");
            return null;
        }

        static System.Numerics.Vector2 N(Vector2 v) => new(v.X, v.Y);
    }

    [Serialize] public ObjectiveTargetShip Escort { get; private set; }
    [Serialize] public Vector2 Destination { get; private set; }
    [Serialize] public float ArrivalRadius { get; private set; }
    [Serialize(Optional = true)] public ObjectID? DestinationPoiID { get; private set; }
    [Serialize(Optional = true)] public int Tier { get; private set; }
    /// <summary>Where the escort started. The ambush springs at a share of the route from here.</summary>
    [Serialize(Optional = true)] public Vector2 Start { get; private set; }
    [Serialize(Optional = true)] public ulong AmbushSeed { get; private set; }
    [Serialize(Optional = true)] public bool AmbushSprung { get; private set; }

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
        AmbushSprung.GetIntegrityHash(iHash);
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
        if (Completion != CompletionState.InProgress) return;
        Fail();
        if (Mission?.Game.Sim is { } sim) RemoveMarker(sim);
    }

    /// <summary>
    /// Removes the destination POI, like vanilla AutoRemoveWhenTagDestroyed does. Only in the escort's system: object IDs belong to one system.
    /// Limit: a failure while the players are in another system leaves the POI there. Revisit if a freighter can die in stasis.
    /// </summary>
    void RemoveMarker(SimRoot sim)
    {
        if (sim.NodeId() != Escort.MapNodeID || DestinationPoiID is not { } id || sim.Doodads[id] is not POIDoodad poi) return;
        sim.Doodads.Remove(poi, dispose: true);
        Log.Info("escort marker removed");
    }

    void OnTick(object? sender, SimRoot sim)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim != Mission.Game.Sim) return;
        if (sim.NodeId() != Escort.MapNodeID || Escort.Location is not { } at) return;
        if (Escort.TargetShip?.AI is { } ai) SteerToDestination(ai);
        float left = at.DistanceTo(Destination);
        if (!AmbushSprung && Escort.TargetShip is not null
            && MissionModel.RouteProgress(Start.DistanceTo(Destination), left) >= MissionArea.TunablesFor(sim.RequireGame()).EscortAmbushAt)
            SpringAmbush(sim, at);
        if (left <= ArrivalRadius)
        {
            Log.Info($"escort {Escort.GetPlainTargetName(sim)} arrived");
            Complete();
            RemoveMarker(sim);
        }
    }

    /// <summary>Spawns the raiders EscortAmbushLead ahead of the escort, on its route. Internal for the autotest.</summary>
    internal void SpringAmbush(SimRoot sim, Vector2 at)
    {
        // Set first: a broken ambush file must not retry on every tick.
        AmbushSprung = true;
        float lead = Math.Min(MissionArea.TunablesFor(sim.RequireGame()).EscortAmbushLead, at.DistanceTo(Destination));
        Vector2 root = at + (Destination - at).Normalize() * lead;
        try
        {
            AmbushFile.Convoy.Generator.Generate(sim, new FastRand(AmbushSeed, 0x4843_4553_414DUL), loadInstantly: true, rootLoc: root);
        }
        catch (Exception ex)
        {
            // The escort goes on without a fight.
            Errors.Report("convoy ambush", ex.Message, ex);
            return;
        }
        Log.Info($"convoy ambush sprung at {root}");
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

using Cosmoteer.Data;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HotCargo.Core;

namespace HotCargo.Events;

/// <summary>
/// Rules type "HotCargoTow" (spec 88): completes when the towed ship is within ArrivalRadius of the destination station: the nearest
/// station at least MinStationDistance away when the mission started. Fails when the towed ship is destroyed. The towed ship has no AI, so it moves only when the players
/// pull it (tractor beam) or push it.
/// Limit: self-contained distance check. The gameplay area plans its own tow logic; revisit to share it once that is merged.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoTow")]
internal sealed class TowObjective : Objective
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoTow")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        [Serialize, PortableHash] public SpawnedObjectSearch TowedTag = null!;
        /// <summary>Tag of the destination stations. Only the location is used, so a station in stasis works too.</summary>
        [Serialize, PortableHash] public ID<SimObjectSpawner> StationTag;
        /// <summary>The destination is the nearest tagged station at least this far from the towed ship, so a tow is never trivial.</summary>
        [Serialize(Optional = true), PortableHash] public float MinStationDistance = 3000f;
        [Serialize(Optional = true), PortableHash] public float ArrivalRadius = 1200f;
        /// <summary>Spec 1127: only stations whose owner is not hostile to the towed ship. None: the objective is invalid.</summary>
        [Serialize(Optional = true), PortableHash] public bool FriendlyOnly;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            bool found = objects.TryFindObject(TowedTag, loc, rand, out var towed);
            List<SimSpawner.SpawnedObject>? stations = objects.GetValueOrDefault(StationTag);
            invalid = !found || stations is not { Count: > 0 };
            if (invalid) return null;
            ObjectID towedId = towed!.UniqueID ?? throw new InvalidOperationException($"towed tag {TowedTag} found an object without an ID");
            if (FriendlyOnly)
            {
                var ship = sim.Ships[towedId] ?? throw new InvalidOperationException($"towed ship {towedId} is not live");
                stations = stations!.Where(st => st.PlayerIndex is not { } owner || !ship.IsEnemiesWith(owner)).ToList();
                invalid = stations.Count == 0;
                if (invalid) return null;
            }
            Vector2 from = towed.Location.Center;
            // Nearest station beyond MinStationDistance; when every station is closer, the farthest one.
            var byDistance = stations!.Select(st => st.Location.Center).OrderBy(c => c.DistanceTo(from)).ToList();
            Vector2 destination = byDistance.FirstOrDefault(c => c.DistanceTo(from) >= MinStationDistance, byDistance[^1]);
            return LastCreated = new TowObjective(new ObjectiveTargetShip(sim.RequireGame(), sim.NodeId(), towedId), destination, ArrivalRadius)
            {
                Tier = Math.Max(1, ((Cosmoteer.Modes.Career.CareerSimModeManager)sim.Mode).TierFields.GetTier(destination)),
            };
        }
    }

    /// <summary>
    /// The objective that the spawner made last, in sim logic (spec 1127). The mission spawner adds the mission to the log a step
    /// later (SimRoot.EnqueueDeterministic), so a caller of the spawn reads the result here, at once.
    /// </summary>
    internal static TowObjective? LastCreated;

    [Serialize] public ObjectiveTargetShip Towed { get; private set; }
    [Serialize] public Vector2 Destination { get; private set; }
    [Serialize] public float ArrivalRadius { get; private set; }
    [Serialize(Optional = true)] public int Tier { get; private set; } = 1;

    public override string ScriptTypeName => "HotCargoTow";

    public TowObjective(ObjectiveTargetShip towed, Vector2 destination, float arrivalRadius)
    {
        Towed = towed ?? throw new ArgumentNullException(nameof(towed));
        if (arrivalRadius <= 0f) throw new ArgumentOutOfRangeException(nameof(arrivalRadius), arrivalRadius, "must be above 0");
        Destination = destination;
        ArrivalRadius = arrivalRadius;
    }

    [GenericConstructor]
    TowObjective(GenericSerialReader reader)
    {
        Towed = null!;
        reader.ReflectiveRead(this);
    }

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        Towed.GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => Towed.MapNode;

    public override Circle? GetAreaLocation(GameRoot game) =>
        Completion == CompletionState.InProgress && game.Sim?.MapNode?.UniqueID == Towed.MapNodeID ? new Circle(Destination, ArrivalRadius) : null;

    public override bool IsShipAssociated(MetaShip ship) => Completion == CompletionState.InProgress && Towed.TargetMetaShip == ship;

    public override bool CheckExpired(GameRoot game) => Completion == CompletionState.Failed || !Towed.IsAlive;

    public override bool CanFocusDisplayLine(GameRoot game) => Towed.CanFocus();

    public override void OnDisplayLineFocused(GameRoot game) => Towed.OnFocused();

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState) =>
        lines.Add(new DisplayLine
        {
            Text = Strings.KeyStringFmt("HotCargoEventMissions/TowFmt", Towed.GetXmlTargetName(game.Sim)),
            State = Completion,
            CanFocus = CanFocusDisplayLine,
            OnFocused = OnDisplayLineFocused,
        });

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        Towed.Killed += OnTowedKilled;
        game.EventHub.Tick += OnTick;
        if (!isLoading && !Towed.IsAlive) OnTowedKilled(null, EventArgs.Empty);
    }

    public override void UnregisterEvents(GameRoot game)
    {
        Towed.Killed -= OnTowedKilled;
        game.EventHub.Tick -= OnTick;
    }

    void OnTowedKilled(object? sender, EventArgs e)
    {
        if (Completion == CompletionState.InProgress) Fail();
    }

    void OnTick(object? sender, SimRoot sim)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim != Mission.Game.Sim) return;
        if (sim.NodeId() != Towed.MapNodeID || Towed.Location is not { } at) return;
        // A towed ship with an AI (an event carrier answers comms) would fly back to its spot. Its spot follows it instead.
        if (Towed.TargetShip?.AI is { Parameters: { PatrolOrigin: { } origin } } ai && origin.DistanceTo(at) > 100f)
        {
            var p = ai.Parameters.Clone();
            p.PatrolOrigin = at;
            ai.Parameters = p;
        }
        if (at.DistanceTo(Destination) > ArrivalRadius) return;
        Log.Info($"towed ship {Towed.GetPlainTargetName(sim)} reached the station");
        Complete();
    }

    public override int GetTypeIntegrityHash() => 0x4843_5457; // "HCTW"
}

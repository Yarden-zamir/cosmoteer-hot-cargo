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
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HotCargo.Core;

namespace HotCargo.Missions.Mechanics;

/// <summary>
/// Rules type "HotCargoCapture" (spec 135): take a ship without destroying it.
/// Completes when the ship surrenders to the players (they accept its surrender in comms) or when it becomes a player ship
/// (the players knock out its crew, then move their own crew aboard). Fails when the ship is gone.
/// A wreck that nobody claims yet is not a failure: the players can still put crew aboard.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoCapture")]
internal sealed class CaptureObjective : Objective, IMissionMarkers
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoCapture")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        [Serialize, PortableHash] public SpawnedObjectSearch TargetTag = null!;
        [Serialize(Optional = true), PortableHash] public SpawnedObjectSearch? AreaTag;
        [Serialize(Optional = true), PortableHash] public int TierOffset;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            invalid = !objects.TryFindObject(TargetTag, loc, rand, out var target);
            if (invalid) return null;
            SimSpawner.SpawnedObject? area = null;
            if (AreaTag is not null && !objects.TryFindObject(AreaTag, loc, rand, out area)) { invalid = true; return null; }
            ObjectID id = target!.UniqueID ?? throw new InvalidOperationException($"target tag {TargetTag} found an object without an ID");
            int tier = Math.Max(1, ((CareerSimModeManager)sim.Mode).TierFields.GetTier(target.Location.Center) + TierOffset);
            return new CaptureObjective(new HullRef(sim, id), tier) { AreaPoiId = area?.UniqueID };
        }
    }

    [Serialize] public HullRef Target { get; private set; }
    [Serialize] public int Tier { get; private set; }
    [Serialize(Optional = true)] public ObjectID? AreaPoiId { get; private set; }

    IEnumerable<ObjectID> IMissionMarkers.MarkerIds => AreaPoiId is { } id ? [id] : [];

    public override string ScriptTypeName => "HotCargoCapture";

    public CaptureObjective(HullRef target, int tier)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Tier = tier;
    }

    [GenericConstructor]
    CaptureObjective(GenericSerialReader reader)
    {
        Target = null!;
        reader.ReflectiveRead(this);
    }

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        Target.ShipId.GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => game.Map.Nodes[Target.NodeId];

    public override Circle? GetAreaLocation(GameRoot game) =>
        Completion == CompletionState.InProgress && Target.Location(game.Sim) is { } at ? new Circle(at, 1500f) : null;

    public override bool IsShipAssociated(MetaShip ship) => Completion == CompletionState.InProgress && ship.UniqueID == Target.ShipId;

    public override bool IsPOIAssociated(POIDoodad poi) => Completion == CompletionState.InProgress && poi.UniqueID == AreaPoiId;

    public override bool CheckExpired(GameRoot game) => Completion == CompletionState.Failed || !Target.Exists(game.Sim);

    public override void GetStringArgs(Dictionary<string, object?> args, GameRoot game)
    {
        base.GetStringArgs(args, game);
        args["target_ship"] = Target.XmlName(game);
    }

    public override bool CanFocusDisplayLine(GameRoot game) => Target.Live(game.Sim) is not null;

    public override void OnDisplayLineFocused(GameRoot game) => Target.Live(game.Sim)?.FocusCamera();

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState) =>
        lines.Add(new DisplayLine
        {
            Text = Strings.KeyStringFmt("HotCargoMechanics/CaptureFmt", Target.XmlName(game)),
            State = Completion,
            CanFocus = CanFocusDisplayLine,
            OnFocused = OnDisplayLineFocused,
        });

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        if (AreaPoiId is { } poiId && game.Sim?.Doodads[poiId] is POIDoodad poi) poi.IsUndiscovered = false;
        game.EventHub.Tick += OnTick;
    }

    public override void UnregisterEvents(GameRoot game) => game.EventHub.Tick -= OnTick;

    void OnTick(object? sender, SimRoot sim)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim != Mission.Game.Sim || !Target.InSystem(sim)) return;
        if (((CareerSimModeManager)sim.Mode).SurrenderedAIs.Contains(Target.ShipId))
        {
            Log.Info($"capture: {Target.Name} surrendered");
            Complete();
            return;
        }
        if (!Target.Exists(sim))
        {
            Log.Info($"capture failed: {Target.Name} is gone");
            Fail();
            return;
        }
        if (Target.Live(sim) is { IsHumanPlayerShip: true } ship)
        {
            Log.Info($"capture: {ship.Metadata.PlainShipName} now belongs to player {ship.Metadata.PlayerIndex}");
            Complete();
        }
    }

    public override int GetTypeIntegrityHash() => 0x4843_4350; // "HCCP"
}

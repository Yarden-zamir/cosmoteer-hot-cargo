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
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HotCargo.Core;

namespace HotCargo.Missions.Mechanics;

/// <summary>
/// Rules type "HotCargoTow" (spec 134): bring a derelict hulk to a station. Completes when the hulk's hull is within
/// Mechanics.DeliveryRadius of the station's hull. The players tow it with tractor beams, push it, or claim it with crew and fly it.
/// Fails when the hulk is gone (destroyed or salvaged to nothing) or the station dies.
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
        [Serialize, PortableHash] public SpawnedObjectSearch HulkTag = null!;
        [Serialize, PortableHash] public SpawnedObjectSearch StationTag = null!;
        [Serialize(Optional = true), PortableHash] public int TierOffset;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            invalid = !objects.TryFindObject(HulkTag, loc, rand, out var hulk) | !objects.TryFindObject(StationTag, loc, rand, out var station);
            if (invalid) return null;
            ObjectID hulkId = hulk!.UniqueID ?? throw new InvalidOperationException($"hulk tag {HulkTag} found an object without an ID");
            ObjectID stationId = station!.UniqueID ?? throw new InvalidOperationException($"station tag {StationTag} found an object without an ID");
            int tier = Math.Max(1, ((CareerSimModeManager)sim.Mode).TierFields.GetTier(hulk.Location.Center) + TierOffset);
            return new TowObjective(new HullRef(sim, hulkId), new ObjectiveTargetShip(sim.RequireGame(), sim.NodeId(), stationId), tier);
        }
    }

    [Serialize] public HullRef Hulk { get; private set; }
    [Serialize] public ObjectiveTargetShip Station { get; private set; }
    [Serialize] public int Tier { get; private set; }

    public override string ScriptTypeName => "HotCargoTow";

    public TowObjective(HullRef hulk, ObjectiveTargetShip station, int tier)
    {
        Hulk = hulk ?? throw new ArgumentNullException(nameof(hulk));
        Station = station ?? throw new ArgumentNullException(nameof(station));
        Tier = tier;
    }

    [GenericConstructor]
    TowObjective(GenericSerialReader reader)
    {
        Hulk = null!;
        Station = null!;
        reader.ReflectiveRead(this);
    }

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        Hulk.ShipId.GetIntegrityHash(iHash);
        Station.GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => game.Map.Nodes[Hulk.NodeId];

    public override Circle? GetAreaLocation(GameRoot game) =>
        Completion == CompletionState.InProgress && Hulk.Location(game.Sim) is { } at ? new Circle(at, 600f) : null;

    public override bool IsShipAssociated(MetaShip ship) =>
        Completion == CompletionState.InProgress && (ship.UniqueID == Hulk.ShipId || Station.TargetMetaShip == ship);

    public override bool CheckExpired(GameRoot game) => Completion == CompletionState.Failed || !Hulk.Exists(game.Sim) || !Station.IsAlive;

    public override void GetStringArgs(Dictionary<string, object?> args, GameRoot game)
    {
        base.GetStringArgs(args, game);
        args["target_ship"] = Hulk.XmlName(game);
        args["station"] = Station.GetXmlTargetName(game.Sim);
    }

    public override bool CanFocusDisplayLine(GameRoot game) => Hulk.Live(game.Sim) is not null;

    public override void OnDisplayLineFocused(GameRoot game) => Hulk.Live(game.Sim)?.FocusCamera();

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState) =>
        lines.Add(new DisplayLine
        {
            Text = Strings.KeyStringFmt("HotCargoMechanics/TowFmt", Hulk.XmlName(game), Station.GetXmlTargetName(game.Sim)),
            State = Completion,
            CanFocus = CanFocusDisplayLine,
            OnFocused = OnDisplayLineFocused,
        });

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        Station.Killed += OnStationKilled;
        game.EventHub.Tick += OnTick;
    }

    public override void UnregisterEvents(GameRoot game)
    {
        Station.Killed -= OnStationKilled;
        game.EventHub.Tick -= OnTick;
    }

    void OnStationKilled(object? sender, EventArgs e)
    {
        if (Completion == CompletionState.InProgress) Fail();
    }

    void OnTick(object? sender, SimRoot sim)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim != Mission.Game.Sim || !Hulk.InSystem(sim)) return;
        if (!Hulk.Exists(sim))
        {
            Log.Info("tow mission failed: the hulk is gone");
            Fail();
            return;
        }
        if (Hulk.Live(sim) is not { } hulk || Station.TargetShip is not { } station) return;
        Circle a = hulk.DetWorldBoundingCircle, b = station.DetWorldBoundingCircle;
        if (MechanicsModel.Gap(a.Center.DistanceTo(b.Center), a.Radius, b.Radius) > MechanicsArea.TunablesFor(sim.RequireGame()).DeliveryRadius) return;
        Log.Info($"tow mission: {hulk.Metadata.PlainShipName} delivered to {station.Metadata.PlainShipName}");
        Complete();
    }

    public override int GetTypeIntegrityHash() => 0x4843_5457; // "HCTW"
}

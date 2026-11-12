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
using Cosmoteer.Ships.Parts.Sensors;
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
/// Rules type "HotCargoSurvey" (spec 136): scan markers inside clouds or storms. A marker counts scan time while a human ship
/// with a working sensor part is within ScanRadius of it. Each marker needs Mechanics.SurveyScanSeconds.
/// Completes when every marker is scanned. Lightning in the storm is the danger; the objective itself never fails.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoSurvey")]
internal sealed class SurveyObjective : Objective
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoSurvey")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        [Serialize, PortableHash] public MultiSpawnedObjectSearch MarkersTag = null!;
        [Serialize(Optional = true), PortableHash] public float ScanRadius = 400f;
        [Serialize(Optional = true), PortableHash] public int MinMarkers = 2;
        [Serialize(Optional = true), PortableHash] public int TierOffset;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            var markers = objects.FindObjects(MarkersTag, loc, rand).Where(m => m.UniqueID is not null).ToList();
            // Systems without nebulas place no markers. The offer is then dropped.
            invalid = markers.Count < MinMarkers;
            if (invalid) return null;
            var mode = (CareerSimModeManager)sim.Mode;
            int tier = Math.Max(1, markers.Max(m => mode.TierFields.GetTier(m.Location.Center)) + TierOffset);
            return new SurveyObjective(sim.NodeId(), markers.Select(m => new SurveyMarker { PoiId = m.UniqueID!.Value, Location = m.Location.Center }).ToList(), ScanRadius, tier);
        }
    }

    [Serialize] public ObjectID NodeId { get; private set; }
    [Serialize] public List<SurveyMarker> Markers { get; private set; }
    [Serialize] public float ScanRadius { get; private set; }
    [Serialize] public int Tier { get; private set; }

    int _displayedDone = -1, _displayedPercent = -1;

    public override string ScriptTypeName => "HotCargoSurvey";

    public SurveyObjective(ObjectID nodeId, List<SurveyMarker> markers, float scanRadius, int tier)
    {
        if (markers.Count == 0) throw new ArgumentException("needs at least one marker", nameof(markers));
        if (scanRadius <= 0f) throw new ArgumentOutOfRangeException(nameof(scanRadius), scanRadius, "must be above 0");
        NodeId = nodeId;
        Markers = markers;
        ScanRadius = scanRadius;
        Tier = tier;
    }

    [GenericConstructor]
    SurveyObjective(GenericSerialReader reader)
    {
        Markers = [];
        reader.ReflectiveRead(this);
    }

    float ScanSeconds(GameRoot game) => MechanicsArea.TunablesFor(game).SurveyScanSeconds;

    public int Done(GameRoot game) => Markers.Count(m => m.Seconds >= ScanSeconds(game));

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        foreach (var m in Markers) ((int)(m.Seconds * 10)).GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => game.Map.Nodes[NodeId];

    /// <summary>The next marker that still needs scanning.</summary>
    SurveyMarker? Next(GameRoot game) => Markers.FirstOrDefault(m => m.Seconds < ScanSeconds(game));

    public override Circle? GetAreaLocation(GameRoot game) =>
        Completion == CompletionState.InProgress && game.Sim?.MapNode?.UniqueID == NodeId && Next(game) is { } m ? new Circle(m.Location, ScanRadius) : null;

    public override bool IsPOIAssociated(POIDoodad poi) => Completion == CompletionState.InProgress && Markers.Any(m => m.PoiId == poi.UniqueID);

    public override void GetStringArgs(Dictionary<string, object?> args, GameRoot game)
    {
        base.GetStringArgs(args, game);
        args["marker_count"] = Markers.Count;
        args["scan_seconds"] = (int)ScanSeconds(game);
    }

    public override void RefreshDisplayLines(GameRoot game)
    {
        int done = Done(game);
        int percent = Next(game) is { } m ? (int)(100 * m.Seconds / ScanSeconds(game)) : 100;
        if (done == _displayedDone && percent == _displayedPercent) return;
        _displayedDone = done;
        _displayedPercent = percent;
        OnDisplayLinesChanged();
    }

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState)
    {
        lines.Add(new DisplayLine
        {
            Text = Strings.KeyStringFmt("Objectives/CountableFmt", Strings.KeyString("HotCargoMechanics/SurveyLine"), Done(game), Markers.Count),
            State = Completion,
        });
        if (Completion == CompletionState.InProgress && Next(game) is { Seconds: > 0f } m)
            lines.Add(new DisplayLine
            {
                Text = Strings.KeyStringFmt("HotCargoMechanics/ScanFmt", (int)(100 * m.Seconds / ScanSeconds(game))),
                State = CompletionState.InProgress,
            });
    }

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        if (game.Sim?.MapNode?.UniqueID == NodeId)
            foreach (var m in Markers)
                if (game.Sim.Doodads[m.PoiId] is POIDoodad poi) poi.IsUndiscovered = false;
        game.EventHub.Tick += OnTick;
    }

    public override void UnregisterEvents(GameRoot game) => game.EventHub.Tick -= OnTick;

    void OnTick(object? sender, SimRoot sim)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim != Mission.Game.Sim || sim.NodeId() != NodeId) return;
        float need = ScanSeconds(sim.RequireGame());
        float dt = (float)sim.FixedUpdater.Interval.Seconds;
        foreach (var m in Markers)
        {
            if (m.Seconds >= need || !sim.Ships.HumanPlayerSignificantShips.Any(s => InScanRange(s, m.Location) && HasWorkingSensor(s))) continue;
            m.Seconds = Math.Min(need, m.Seconds + dt);
            if (m.Seconds >= need) Log.Info($"survey: marker at {m.Location} scanned ({Done(sim.RequireGame())}/{Markers.Count})");
        }
        if (Markers.All(m => m.Seconds >= need)) Complete();
    }

    bool InScanRange(Ship s, Vector2 at) => MechanicsModel.Gap(s.DetWorldCenter.DistanceTo(at), s.DetWorldBoundingCircle.Radius, 0f) <= ScanRadius;

    /// <summary>A ship with a sensor part (any part with a Sensor component) that works now: crewed, powered, not damaged off.</summary>
    static bool HasWorkingSensor(Ship s) => s.Parts.Any(p => p.Components.Any(c => c is Sensor { IsOperational: true }));

    public override int GetTypeIntegrityHash() => 0x4843_5356; // "HCSV"
}

[ReflectiveSerialization(Explicit = true)]
internal sealed class SurveyMarker
{
    [Serialize] public ObjectID PoiId;
    [Serialize] public Vector2 Location;
    /// <summary>Scan seconds so far.</summary>
    [Serialize(Optional = true)] public float Seconds;
}

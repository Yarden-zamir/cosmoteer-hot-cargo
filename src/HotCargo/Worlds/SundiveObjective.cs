using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Resources;
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
using HotCargo.Missions;

namespace HotCargo.Worlds;

/// <summary>
/// Rules type "HotCargoSundive" (spec 223): a wreck site deep in a sun's heat zone.
/// Dive: a human ship within Radius of the site salvages; salvage time adds up over visits. At Worlds.SundiveSalvageSeconds
/// the ship that finished gets one Recorder item in its storage (spec 1125); with no free storage the salvage waits.
/// Escape: the objective completes when a human ship that holds the recorder is out of every heat zone. It fails when no recorder is
/// left in the players' ships or loose in the system. When the mission leaves the log, the client takes the recorder (IMissionCargo).
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoSundive")]
internal sealed class SundiveObjective : Objective, IMissionMarkers, Events.IMissionCargo
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoSundive")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        [Serialize, PortableHash] public SpawnedObjectSearch SiteTag = null!;
        [Serialize, PortableHash] public float Radius;
        /// <summary>The item that the salvage gives (yarden_zamir.flight_recorder).</summary>
        [Serialize, PortableHash] public ID<ResourceRules> Recorder;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            invalid = !objects.TryFindObject(SiteTag, loc, rand, out var site);
            if (invalid) return null;
            Vector2 center = site!.Location.Center;
            return new SundiveObjective(sim.NodeId(), center, Radius)
            {
                Recorder = Recorder,
                SitePoiID = site.UniqueID,
                Tier = Math.Max(1, ((CareerSimModeManager)sim.Mode).TierFields.GetTier(center)),
            };
        }
    }

    [Serialize] public ObjectID NodeID { get; private set; }
    [Serialize] public Vector2 Site { get; private set; }
    [Serialize] public float Radius { get; private set; }
    [Serialize(Optional = true)] public ObjectID? SitePoiID { get; private set; }

    IEnumerable<ObjectID> IMissionMarkers.MarkerIds => SitePoiID is { } id ? [id] : [];
    [Serialize(Optional = true)] public int Tier { get; private set; }
    [Serialize(Optional = true)] public float SalvageSeconds { get; private set; }
    [Serialize] public ID<ResourceRules> Recorder { get; private set; }
    /// <summary>True once a player ship got the recorder. The escape follows the item from then on.</summary>
    [Serialize(Optional = true)] public bool Held { get; private set; }

    int _shownSecond = -1;
    /// <summary>The salvage is done, but the salvaging ship has no free storage. Display only.</summary>
    bool _noRoom;

    public IReadOnlyList<ID<ResourceRules>> CargoToTakeBack => Held ? [Recorder] : [];

    public override string ScriptTypeName => "HotCargoSundive";

    public SundiveObjective(ObjectID nodeId, Vector2 site, float radius)
    {
        if (radius <= 0f) throw new ArgumentOutOfRangeException(nameof(radius), radius, "must be above 0");
        NodeID = nodeId;
        Site = site;
        Radius = radius;
    }

    [GenericConstructor]
    SundiveObjective(GenericSerialReader reader) => reader.ReflectiveRead(this);

    static float NeededSeconds(SimRoot? sim) => Current.Tunables(sim).SundiveSalvageSeconds;

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        SalvageSeconds.GetIntegrityHash(iHash);
        Held.GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => game.Map.Nodes[NodeID];

    public override Circle? GetAreaLocation(GameRoot game) =>
        Completion == CompletionState.InProgress && !Held && game.Sim?.MapNode?.UniqueID == NodeID ? new Circle(Site, Radius) : null;

    public override bool IsPOIAssociated(POIDoodad poi) => Completion == CompletionState.InProgress && poi.UniqueID == SitePoiID;

    public override bool CheckExpired(GameRoot game) => Completion == CompletionState.Failed;

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState)
    {
        string item = Recorder.GetResourceRules().NameKey.Text;
        if (Held)
        {
            lines.Add(new DisplayLine { Text = Strings.KeyStringFmt("HotCargoWorlds/SundiveEscapeFmt", item), State = Completion });
            return;
        }
        float depth = game.Sim?.MapNode?.UniqueID == NodeID ? HeatZone.Depth(game.Sim, Site) * 100f : 0f;
        lines.Add(new DisplayLine { Text = Strings.KeyStringFmt("HotCargoWorlds/SundiveDiveFmt", depth), State = Completion });
        if (SalvageSeconds > 0f)
            lines.Add(new DisplayLine { Text = Strings.KeyStringFmt("HotCargoWorlds/SundiveSalvageFmt", SalvageSeconds, NeededSeconds(game.Sim)), State = CompletionState.InProgress });
        if (_noRoom) lines.Add(new DisplayLine { Text = Strings.KeyStringFmt("HotCargoWorlds/SundiveNoRoomFmt", item), State = CompletionState.InProgress });
    }

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        if (SitePoiID is { } poiId && game.Sim?.Doodads[poiId] is POIDoodad poi) poi.IsUndiscovered = false;
        game.EventHub.Tick += OnTick;
    }

    public override void UnregisterEvents(GameRoot game)
    {
        game.EventHub.Tick -= OnTick;
    }

    void OnTick(object? sender, SimRoot sim) => Errors.Guard("sundive tick", () => Tick(sim));

    void Tick(SimRoot sim)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim != Mission.Game.Sim) return;
        if (sim.MapNode?.UniqueID != NodeID) return;
        if (Held)
        {
            if (Events.HandOverObjective.UnitsLeft(sim, Recorder) == 0)
            {
                Log.Info("sundive: no recorder left in the fleet or the system; the job fails");
                var game = Mission.Game;
                Fail();
                // UI only: tell the players why.
                Ux.UxKit.CenterText(game, Strings.GetText("HotCargoWorlds/SundiveLost"));
                return;
            }
            var clear = sim.Ships.HumanPlayerShips.OrderBy(s => s.UniqueID).FirstOrDefault(s =>
                s.Resources.GetResourceTotal(Recorder, includeStored: true, includeCarried: true) > 0 && HeatZone.Depth(sim, s.DetWorldCenter) <= 0f);
            if (clear is not null)
            {
                Log.Info($"sundive: {clear.Name} left the heat with the recorder");
                Complete();
            }
            return;
        }
        Ship? salvager = sim.Ships.HumanPlayerSignificantShips
            .Where(s => s.DetWorldCenter.DistanceTo(Site) <= Radius)
            .OrderBy(s => s.UniqueID).FirstOrDefault();
        if (salvager is null) return;
        SalvageSeconds = Math.Min(NeededSeconds(sim), SalvageSeconds + (float)sim.FixedUpdater.Interval.Seconds);
        if (SalvageSeconds >= NeededSeconds(sim))
        {
            bool room = salvager.Resources.AddResources(Recorder, 1, excludeConstructionSinks: true) == 1;
            if (room != !_noRoom) { _noRoom = !room; OnDisplayLinesChanged(); }
            if (!room) return;
            Held = true;
            Log.Info($"sundive: {salvager.Name} has the recorder after {SalvageSeconds:0.0} s");
            OnDisplayLinesChanged();
            return;
        }
        int second = (int)SalvageSeconds;
        if (second != _shownSecond) { _shownSecond = second; OnDisplayLinesChanged(); }
    }

    public override int GetTypeIntegrityHash() => 0x4843_5344; // "HCSD"
}

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
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HotCargo.Core;

namespace HotCargo.Worlds;

/// <summary>
/// Rules type "HotCargoSundive" (spec 223): a wreck site deep in a sun's heat zone.
/// Dive: a human ship within Radius of the site salvages; salvage time adds up over visits. At Worlds.SundiveSalvageSeconds
/// the ship that finished holds the recorder. Escape: the objective completes when that ship is out of every heat zone,
/// and fails when that ship is destroyed.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoSundive")]
internal sealed class SundiveObjective : Objective
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoSundive")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        [Serialize, PortableHash] public SpawnedObjectSearch SiteTag = null!;
        [Serialize, PortableHash] public float Radius;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            invalid = !objects.TryFindObject(SiteTag, loc, rand, out var site);
            if (invalid) return null;
            Vector2 center = site!.Location.Center;
            return new SundiveObjective(sim.NodeId(), center, Radius)
            {
                SitePoiID = site.UniqueID,
                Tier = Math.Max(1, ((CareerSimModeManager)sim.Mode).TierFields.GetTier(center)),
            };
        }
    }

    [Serialize] public ObjectID NodeID { get; private set; }
    [Serialize] public Vector2 Site { get; private set; }
    [Serialize] public float Radius { get; private set; }
    [Serialize(Optional = true)] public ObjectID? SitePoiID { get; private set; }
    [Serialize(Optional = true)] public int Tier { get; private set; }
    [Serialize(Optional = true)] public float SalvageSeconds { get; private set; }
    /// <summary>The ship that holds the recorder. Null while the dive goes on.</summary>
    [Serialize(Optional = true)] public ObjectiveTargetShip? Carrier { get; private set; }

    int _shownSecond = -1;

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
        (Carrier is not null).GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => game.Map.Nodes[NodeID];

    public override Circle? GetAreaLocation(GameRoot game) =>
        Completion == CompletionState.InProgress && Carrier is null && game.Sim?.MapNode?.UniqueID == NodeID ? new Circle(Site, Radius) : null;

    public override bool IsPOIAssociated(POIDoodad poi) => Completion == CompletionState.InProgress && poi.UniqueID == SitePoiID;

    public override bool IsShipAssociated(MetaShip ship) => Completion == CompletionState.InProgress && Carrier?.TargetMetaShip == ship;

    public override bool CheckExpired(GameRoot game) => Completion == CompletionState.Failed;

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState)
    {
        if (Carrier is { } carrier)
        {
            lines.Add(new DisplayLine { Text = Strings.KeyStringFmt("HotCargoWorlds/SundiveEscapeFmt", carrier.GetXmlTargetName(game.Sim)), State = Completion });
            return;
        }
        float depth = game.Sim?.MapNode?.UniqueID == NodeID ? HeatZone.Depth(game.Sim, Site) * 100f : 0f;
        lines.Add(new DisplayLine { Text = Strings.KeyStringFmt("HotCargoWorlds/SundiveDiveFmt", depth), State = Completion });
        if (SalvageSeconds > 0f)
            lines.Add(new DisplayLine { Text = Strings.KeyStringFmt("HotCargoWorlds/SundiveSalvageFmt", SalvageSeconds, NeededSeconds(game.Sim)), State = CompletionState.InProgress });
    }

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        if (SitePoiID is { } poiId && game.Sim?.Doodads[poiId] is POIDoodad poi) poi.IsUndiscovered = false;
        if (Carrier is not null) Carrier.Killed += OnCarrierKilled;
        game.EventHub.Tick += OnTick;
    }

    public override void UnregisterEvents(GameRoot game)
    {
        if (Carrier is not null) Carrier.Killed -= OnCarrierKilled;
        game.EventHub.Tick -= OnTick;
    }

    void OnCarrierKilled(object? sender, EventArgs e)
    {
        if (Completion != CompletionState.InProgress) return;
        Log.Info("sundive carrier destroyed");
        Mission?.Game.Gui.ShowAreaName(Strings.GetText("HotCargoWorlds/SundiveLost"));
        Fail();
    }

    void OnTick(object? sender, SimRoot sim) => Errors.Guard("sundive tick", () => Tick(sim));

    void Tick(SimRoot sim)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim != Mission.Game.Sim) return;
        if (sim.MapNode?.UniqueID != NodeID) return;
        if (Carrier is { } carrier)
        {
            if (carrier.TargetShip is { } ship && HeatZone.Depth(sim, ship.DetWorldCenter) <= 0f)
            {
                Log.Info($"sundive: {ship.Name} left the heat with the recorder");
                Complete();
            }
            return;
        }
        Ship? salvager = sim.Ships.HumanPlayerSignificantShips
            .Where(s => s.DetWorldCenter.DistanceTo(Site) <= Radius)
            .OrderBy(s => s.UniqueID).FirstOrDefault();
        if (salvager is null) return;
        SalvageSeconds += (float)sim.FixedUpdater.Interval.Seconds;
        if (SalvageSeconds >= NeededSeconds(sim))
        {
            Carrier = new ObjectiveTargetShip(sim.RequireGame(), salvager);
            Carrier.Killed += OnCarrierKilled;
            Log.Info($"sundive: {salvager.Name} has the recorder after {SalvageSeconds:0.0} s");
            OnDisplayLinesChanged();
            return;
        }
        int second = (int)SalvageSeconds;
        if (second != _shownSecond) { _shownSecond = second; OnDisplayLinesChanged(); }
    }

    public override int GetTypeIntegrityHash() => 0x4843_5344; // "HCSD"
}

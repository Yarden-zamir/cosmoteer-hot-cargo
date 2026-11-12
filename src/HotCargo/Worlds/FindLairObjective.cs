using Cosmoteer;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HotCargo.Core;

namespace HotCargo.Worlds;

/// <summary>
/// Rules type "HotCargoFindLair" (spec 224): find a hidden lair. The HUD shows a search circle of SearchRadius that holds the lair,
/// with its center away from the lair. The objective completes when a human ship comes within Worlds.LairDiscoverDistance
/// of the lair (live or in stasis), or when the lair is gone. Put a DefeatShips on the lair after it.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoFindLair")]
internal sealed class FindLairObjective : Objective
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoFindLair")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        [Serialize, PortableHash] public SpawnedObjectSearch LairTag = null!;
        [Serialize, PortableHash] public float SearchRadius;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            invalid = !objects.TryFindObject(LairTag, loc, rand, out var lair);
            if (invalid) return null;
            ObjectID lairId = lair!.UniqueID ?? throw new InvalidOperationException($"lair tag {LairTag} found an object without an ID");
            Vector2 at = lair.Location.Center;
            // The lair sits between 20 % and 60 % of the radius from the circle center, so the circle never centers on it.
            Vector2 center = at + Vector2.FromAngle(rand.Direction()) * (SearchRadius * rand.Single(0.2f, 0.6f));
            return new FindLairObjective(new ObjectiveTargetShip(sim.RequireGame(), sim.NodeId(), lairId), center, SearchRadius)
            {
                Tier = Math.Max(1, ((CareerSimModeManager)sim.Mode).TierFields.GetTier(at)),
            };
        }
    }

    [Serialize] public ObjectiveTargetShip Lair { get; private set; }
    [Serialize] public Vector2 SearchCenter { get; private set; }
    [Serialize] public float SearchRadius { get; private set; }
    [Serialize(Optional = true)] public int Tier { get; private set; }

    public override string ScriptTypeName => "HotCargoFindLair";

    public FindLairObjective(ObjectiveTargetShip lair, Vector2 searchCenter, float searchRadius)
    {
        Lair = lair ?? throw new ArgumentNullException(nameof(lair));
        if (searchRadius <= 0f) throw new ArgumentOutOfRangeException(nameof(searchRadius), searchRadius, "must be above 0");
        SearchCenter = searchCenter;
        SearchRadius = searchRadius;
    }

    [GenericConstructor]
    FindLairObjective(GenericSerialReader reader)
    {
        Lair = null!;
        reader.ReflectiveRead(this);
    }

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        Lair.GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => Lair.MapNode;

    public override Circle? GetAreaLocation(GameRoot game) =>
        Completion == CompletionState.InProgress && game.Sim?.MapNode?.UniqueID == Lair.MapNodeID ? new Circle(SearchCenter, SearchRadius) : null;

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState) =>
        lines.Add(new DisplayLine { Text = Strings.KeyString("HotCargoWorlds/FindLairFmt"), State = Completion });

    public override void RegisterEvents(GameRoot game, bool isLoading) => game.EventHub.Tick += OnTick;

    public override void UnregisterEvents(GameRoot game) => game.EventHub.Tick -= OnTick;

    void OnTick(object? sender, SimRoot sim) => Errors.Guard("find lair tick", () => Tick(sim));

    void Tick(SimRoot sim)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim != Mission.Game.Sim) return;
        if (sim.MapNode?.UniqueID != Lair.MapNodeID) return;
        if (!Lair.IsAlive) { Complete(); return; }
        if (Lair.Location is not { } at) return;
        float reach = Current.Tunables(sim).LairDiscoverDistance;
        if (!sim.Ships.HumanPlayerSignificantShips.Any(s => s.DetWorldCenter.DistanceTo(at) <= reach)) return;
        Log.Info($"lair found at {at}");
        Ux.UxKit.CenterText(sim.RequireGame(), Strings.GetText("HotCargoWorlds/LairFound"));
        Complete();
    }

    public override int GetTypeIntegrityHash() => 0x4843_464C; // "HCFL"
}

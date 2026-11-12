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
/// Rules type "HotCargoHoldPass" (spec 222): hold a pass through the belt. The clock runs while a human ship is within Radius
/// of the pass center. After the first entry, a pass that stays empty for Worlds.PassLostSeconds fails the mission.
/// Every Worlds.PassWaveSeconds of holding, a wave from mod/worlds/spawns.rules comes from outside the pass.
/// Away from the pass system, the clock waits.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoHoldPass")]
internal sealed class HoldPassObjective : Objective
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoHoldPass")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        /// <summary>The ring POI that marks the pass.</summary>
        [Serialize, PortableHash] public SpawnedObjectSearch PassTag = null!;
        [Serialize, PortableHash] public float Radius;
        /// <summary>Name of the wave spawner in mod/worlds/spawns.rules.</summary>
        [Serialize, PortableHash] public string Wave = "";
        [Serialize(Optional = true), PortableHash] public int TierOffset;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            invalid = !objects.TryFindObject(PassTag, loc, rand, out var pass);
            if (invalid) return null;
            Vector2 center = pass!.Location.Center;
            var mode = (CareerSimModeManager)sim.Mode;
            return new HoldPassObjective(sim.NodeId(), center, Radius, Wave)
            {
                PassPoiID = pass.UniqueID,
                WaveSeed = rand.UInt64(),
                Tier = Math.Max(1, mode.TierFields.GetTier(center) + TierOffset),
            };
        }
    }

    [Serialize] public ObjectID NodeID { get; private set; }
    [Serialize] public Vector2 Center { get; private set; }
    [Serialize] public float Radius { get; private set; }
    [Serialize] public string Wave { get; private set; }
    [Serialize(Optional = true)] public ObjectID? PassPoiID { get; private set; }
    [Serialize(Optional = true)] public ulong WaveSeed { get; private set; }
    [Serialize(Optional = true)] public int Tier { get; private set; }
    [Serialize(Optional = true)] public float HeldSeconds { get; private set; }
    [Serialize(Optional = true)] public float EmptySeconds { get; private set; }
    [Serialize(Optional = true)] public bool Started { get; private set; }
    [Serialize(Optional = true)] public int WavesSpawned { get; private set; }

    int _shownSecond = -1;
    bool _shownEmpty;

    public override string ScriptTypeName => "HotCargoHoldPass";

    public HoldPassObjective(ObjectID nodeId, Vector2 center, float radius, string wave)
    {
        if (radius <= 0f) throw new ArgumentOutOfRangeException(nameof(radius), radius, "must be above 0");
        if (string.IsNullOrEmpty(wave)) throw new ArgumentException("a pass hold needs a wave spawner name", nameof(wave));
        NodeID = nodeId;
        Center = center;
        Radius = radius;
        Wave = wave;
    }

    [GenericConstructor]
    HoldPassObjective(GenericSerialReader reader)
    {
        Wave = "";
        reader.ReflectiveRead(this);
    }

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        HeldSeconds.GetIntegrityHash(iHash);
        EmptySeconds.GetIntegrityHash(iHash);
        WavesSpawned.GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => game.Map.Nodes[NodeID];

    public override Circle? GetAreaLocation(GameRoot game) =>
        Completion == CompletionState.InProgress && game.Sim?.MapNode?.UniqueID == NodeID ? new Circle(Center, Radius) : null;

    public override bool IsPOIAssociated(POIDoodad poi) => Completion == CompletionState.InProgress && poi.UniqueID == PassPoiID;

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState)
    {
        var t = Current.Tunables(game.Sim);
        string left = Clock(t.PassHoldSeconds - HeldSeconds);
        lines.Add(new DisplayLine
        {
            Text = Started
                ? Strings.KeyStringFmt("HotCargoWorlds/HoldPassFmt", left)
                : Strings.KeyStringFmt("HotCargoWorlds/HoldPassWaitFmt", left),
            State = Completion,
        });
        if (Completion == CompletionState.InProgress && Started && EmptySeconds > 0f)
            lines.Add(new DisplayLine
            {
                Text = Strings.KeyStringFmt("HotCargoWorlds/HoldPassEmptyFmt", Clock(t.PassLostSeconds - EmptySeconds)),
                State = CompletionState.InProgress,
            });
    }

    static string Clock(float seconds)
    {
        int s = Math.Max(0, (int)MathF.Ceiling(seconds));
        return $"{s / 60}:{s % 60:00}";
    }

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        if (PassPoiID is { } poiId && game.Sim?.Doodads[poiId] is POIDoodad poi) poi.IsUndiscovered = false;
        game.EventHub.Tick += OnTick;
    }

    public override void UnregisterEvents(GameRoot game) => game.EventHub.Tick -= OnTick;

    void OnTick(object? sender, SimRoot sim) => Errors.Guard("hold pass tick", () => Tick(sim));

    void Tick(SimRoot sim)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim != Mission.Game.Sim) return;
        if (sim.MapNode?.UniqueID != NodeID) return;
        var t = Current.Tunables(sim);
        bool inside = sim.Ships.HumanPlayerSignificantShips.Any(s => s.DetWorldCenter.DistanceTo(Center) <= Radius);
        float before = HeldSeconds;
        var (state, outcome) = WorldsModel.PassStep(new PassState(HeldSeconds, EmptySeconds, Started), inside,
            (float)sim.FixedUpdater.Interval.Seconds, t.PassHoldSeconds, t.PassLostSeconds);
        HeldSeconds = state.HeldSeconds;
        EmptySeconds = state.EmptySeconds;
        Started = state.Started;
        for (int i = WorldsModel.WavesDue(before, HeldSeconds, t.PassWaveSeconds); i > 0 && outcome == PassOutcome.Holding; i--) SpawnWave(sim, t);
        switch (outcome)
        {
            case PassOutcome.Won:
                Log.Info($"pass held for {HeldSeconds:0.0} s, {WavesSpawned} waves");
                Complete();
                return;
            case PassOutcome.Lost:
                Log.Info($"pass lost after {HeldSeconds:0.0} s held");
                Ux.UxKit.CenterText(sim.RequireGame(), Strings.GetText("HotCargoWorlds/PassLost"));
                Fail();
                return;
        }
        int second = (int)MathF.Ceiling(Started && EmptySeconds > 0f ? EmptySeconds : HeldSeconds);
        if (second != _shownSecond || (EmptySeconds > 0f) != _shownEmpty)
        {
            _shownSecond = second;
            _shownEmpty = EmptySeconds > 0f;
            OnDisplayLinesChanged();
        }
    }

    /// <summary>Spawns one wave outside the pass, from a direction that the wave seed and the wave number fix.</summary>
    void SpawnWave(SimRoot sim, WorldsTunables t)
    {
        var rand = new FastRand(WaveSeed, (ulong)WavesSpawned);
        Vector2 at = Center + Vector2.FromAngle(rand.Direction()) * t.PassWaveDistance;
        WavesSpawned++;
        Errors.Guard("pass wave", () =>
            WorldsSpawns.Get(Wave).Generate(sim, rand, loadInstantly: true, rootLoc: at, addExistingSpawnTags: true));
        Log.Info($"pass wave {WavesSpawned} at {at}");
    }

    public override int GetTypeIntegrityHash() => 0x4843_4850; // "HCHP"
}

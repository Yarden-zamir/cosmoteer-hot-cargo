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
using Cosmoteer.Simulation.Stasis;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HotCargo.Core;

namespace HotCargo.Missions.Mechanics;

public enum HoldOutProfile
{
    /// <summary>Drone Siege (spec 132): many tiny drones for five minutes.</summary>
    Siege,
    /// <summary>Claim Jumpers (spec 137): a few raiders of the sector tier − 1 for four minutes.</summary>
    Guard,
}

/// <summary>
/// Rules type "HotCargoHoldOut" (spec 132, 137): keep a friendly target alive while waves attack it.
/// The clock runs only while a human ship (any ship, a player-built platform too) is within GuardRadius of the target.
/// So one ship must stay on station: the players either wait there or build a separate defence platform and fly on.
/// Waves come only while the target is guarded. The mission fails when the target dies.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoHoldOut")]
internal sealed class HoldOutObjective : Objective
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoHoldOut")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        [Serialize, PortableHash] public SpawnedObjectSearch TargetTag = null!;
        [Serialize, PortableHash] public HoldOutProfile Profile;
        [Serialize(Optional = true), PortableHash] public int TierOffset;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            invalid = !objects.TryFindObject(TargetTag, loc, rand, out var target);
            if (invalid) return null;
            ObjectID id = target!.UniqueID ?? throw new InvalidOperationException($"target tag {TargetTag} found an object without an ID");
            int tier = Math.Max(1, ((CareerSimModeManager)sim.Mode).TierFields.GetTier(target.Location.Center) + TierOffset);
            return new HoldOutObjective(new ObjectiveTargetShip(sim.RequireGame(), sim.NodeId(), id), Profile, tier);
        }
    }

    [Serialize] public ObjectiveTargetShip Target { get; private set; }
    [Serialize] public HoldOutProfile Profile { get; private set; }
    [Serialize] public int Tier { get; private set; }
    /// <summary>Game seconds with a human ship on station.</summary>
    [Serialize(Optional = true)] public double GuardedSeconds { get; set; }
    /// <summary>Player index of all wave ships of this objective. 0 until the first wave.</summary>
    [Serialize(Optional = true)] public int WaveAllegiance { get; private set; }

    /// <summary>UI only: true when the last sim tick found a human ship on station.</summary>
    public bool Guarded { get; private set; }
    int _displayedSeconds = -1;
    bool _displayedGuarded;

    public override string ScriptTypeName => "HotCargoHoldOut";

    public ID<SimObjectSpawner> WaveTag => new(Profile == HoldOutProfile.Siege ? "hc_siege_drone" : "hc_guard_raider");

    public HoldOutObjective(ObjectiveTargetShip target, HoldOutProfile profile, int tier)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Profile = profile;
        Tier = tier;
    }

    [GenericConstructor]
    HoldOutObjective(GenericSerialReader reader)
    {
        Target = null!;
        reader.ReflectiveRead(this);
    }

    public HoldOutTunables Settings(GameRoot game) => Profile switch
    {
        HoldOutProfile.Siege => MechanicsArea.TunablesFor(game).Siege,
        HoldOutProfile.Guard => MechanicsArea.TunablesFor(game).Guard,
        _ => throw new InvalidOperationException($"unknown hold-out profile {Profile}"),
    };

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        Target.GetIntegrityHash(iHash);
        ((int)(GuardedSeconds * 10)).GetIntegrityHash(iHash);
        WaveAllegiance.GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => Target.MapNode;

    public override Circle? GetAreaLocation(GameRoot game) =>
        Completion == CompletionState.InProgress && Target.Location is { } at ? new Circle(at, Settings(game).GuardRadius) : null;

    public override bool IsShipAssociated(MetaShip ship) => Completion == CompletionState.InProgress && Target.TargetMetaShip == ship;

    public override bool CheckExpired(GameRoot game) => Completion == CompletionState.Failed || !Target.IsAlive;

    public override void GetStringArgs(Dictionary<string, object?> args, GameRoot game)
    {
        base.GetStringArgs(args, game);
        args["target_ship"] = Target.GetXmlTargetName(game.Sim);
        args["hold_minutes"] = (int)Math.Round(Settings(game).Seconds / 60f);
    }

    public override bool CanFocusDisplayLine(GameRoot game) => Target.CanFocus();

    public override void OnDisplayLineFocused(GameRoot game) => Target.OnFocused();

    public override void RefreshDisplayLines(GameRoot game)
    {
        int left = MechanicsModel.SecondsLeft(Settings(game).Seconds, GuardedSeconds);
        if (left == _displayedSeconds && Guarded == _displayedGuarded) return;
        _displayedSeconds = left;
        _displayedGuarded = Guarded;
        OnDisplayLinesChanged();
    }

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState)
    {
        int left = MechanicsModel.SecondsLeft(Settings(game).Seconds, GuardedSeconds);
        lines.Add(new DisplayLine
        {
            Text = Strings.KeyStringFmt("HotCargoMechanics/HoldFmt", Target.GetXmlTargetName(game.Sim), left / 60, (left % 60).ToString("00")),
            State = Completion,
            CanFocus = CanFocusDisplayLine,
            OnFocused = OnDisplayLineFocused,
        });
        if (Completion == CompletionState.InProgress && !Guarded)
            lines.Add(new DisplayLine { Text = Strings.KeyString("HotCargoMechanics/HoldPaused"), State = CompletionState.InProgress });
    }

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        Target.Killed += OnTargetKilled;
        game.EventHub.Tick += OnTick;
        if (!isLoading && !Target.IsAlive) OnTargetKilled(null, EventArgs.Empty);
    }

    public override void UnregisterEvents(GameRoot game)
    {
        Target.Killed -= OnTargetKilled;
        game.EventHub.Tick -= OnTick;
    }

    void OnTargetKilled(object? sender, EventArgs e)
    {
        if (Completion != CompletionState.InProgress) return;
        Log.Info($"hold-out {Profile}: target lost at {GuardedSeconds:0} s");
        Fail();
    }

    void OnTick(object? sender, SimRoot sim)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim != Mission.Game.Sim) return;
        Guarded = false;
        if (sim.NodeId() != Target.MapNodeID || Target.TargetShip is not { } target) return;
        var t = Settings(sim.RequireGame());
        Circle area = target.DetWorldBoundingCircle;
        Guarded = sim.Ships.HumanPlayerSignificantShips.Any(s =>
            MechanicsModel.Gap(s.DetWorldCenter.DistanceTo(area.Center), 0f, area.Radius) <= t.GuardRadius);
        if (!Guarded) return;
        double before = GuardedSeconds;
        GuardedSeconds += sim.FixedUpdater.Interval.Seconds;
        int waves = MechanicsModel.WavesDue(before, GuardedSeconds, t.FirstWaveSeconds, t.WaveSeconds);
        if (waves > 0) Errors.Guard($"hold-out {Profile} wave", () => SpawnWave(sim, target, t));
        if (GuardedSeconds >= t.Seconds)
        {
            Log.Info($"hold-out {Profile}: held for {GuardedSeconds:0} s");
            Complete();
        }
    }

    /// <summary>Spawns one wave around the target. Sim tick only (sim.Rand). Public for the autotest.</summary>
    internal void SpawnWave(SimRoot sim, Ship target, HoldOutTunables t)
    {
        var career = (CareerSimModeManager)sim.Mode;
        if (WaveAllegiance == 0) WaveAllegiance = career.AllocUniquePlayerIndex();
        int live = SimQueries.TaggedShips(sim, WaveTag).Count(s => s.PlayerIndex == WaveAllegiance);
        int count = MechanicsModel.WaveShipCount(t.ShipsBase, t.ShipsPerTier, Tier, live, t.MaxLiveShips);
        int shipTier = MechanicsModel.WaveShipTier(t.ShipTierBase, t.ShipTierPerTier, Tier, t.ShipTierMax);
        Vector2 center = target.DetWorldCenter;
        float direction = sim.Rand.Direction();
        var tags = t.ShipTags.Select(s => new ID<BuiltinShipTag>(s)).ToArray();
        for (int i = 0; i < count; i++)
        {
            BuiltinShipRules ship = WaveSpawns.PickShip(sim, tags, shipTier);
            Vector2 loc = WaveSpawns.Location(sim, center, direction, 1800f, 2400f);
            var spawner = new StasisShipSpawner(sim, ship.ID, loc, SpawnedShipFeatures.None, sim.Rand.UInt64(), [WaveTag])
            {
                AI = new ID<ShipAIRules>("Normal"),
                AIParameters = new AIParameters
                {
                    PatrolOrigin = center,
                    PatrolRadius = 300f,
                    TargetAcquireRangeFromOrigin = 3000f,
                    TargetUnacquireRangeFromOrigin = 4500f,
                    ChanceToConsiderSurrenderingToPlayer = 0f,
                },
                Direction = loc.DirectionTo(in center),
                ObjectAllegiance = WaveAllegiance,
            };
            spawner.Spawn(sim);
            var spawned = sim.Ships[spawner.ReservedObjectID] ?? throw new InvalidOperationException($"wave ship {ship.ID} did not spawn");
            _ = spawned.Renderer.AnimateFtlJumpIn(spawned.WorldFlightDirection);
        }
        Log.Info($"hold-out {Profile}: wave of {count} tier-{shipTier} ships at {GuardedSeconds:0} s ({live} still live)");
    }

    public override int GetTypeIntegrityHash() => 0x4843_484F; // "HCHO"
}

/// <summary>Ship picks and spawn points for waves that C# spawns in the sim tick.</summary>
internal static class WaveSpawns
{
    /// <summary>A random built-in ship with the tags at the tier, or the nearest lower tier that has one.</summary>
    public static BuiltinShipRules PickShip(SimRoot sim, ID<BuiltinShipTag>[] tags, int tier)
    {
        for (int wanted = Math.Max(1, tier); wanted >= 1; wanted--)
            if (GameApp.Rules.BuiltinShips.GetRandomMatchingShip(sim.Rand, null, new Range<int>(wanted, wanted), null, tags) is { } ship)
                return ship;
        throw new InvalidOperationException($"no built-in ship with tags [{string.Join(", ", tags)}] at tier {tier} or below");
    }

    /// <summary>A point at a random distance in [min, max] from center, near the direction. Same sun check as police spawns.</summary>
    public static Vector2 Location(SimRoot sim, Vector2 center, float direction, float min, float max)
    {
        var wanted = GameApp.Rules.CareerMode.Wanted;
        Vector2 loc = center;
        for (int tries = 0; tries < 100; tries++)
        {
            float angle = direction + sim.Rand.Single(new Range<float>(-0.6f, 0.6f));
            loc = center + Vector2.FromAngle(angle) * sim.Rand.Single(new Range<float>(min, max));
            if (!sim.ShouldAIAvoidLocation(loc, wanted.PoliceSpawnAvoidableDoodadTags, wanted.PoliceSpawnAvoidableDoodadBuffer)) break;
        }
        return loc;
    }
}

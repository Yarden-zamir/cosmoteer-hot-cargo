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
using Cosmoteer.Modes.Career.Missions.Rewards;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling;
using Halfling.Geometry;
using Halfling.Gui.Components.Text;
using Halfling.Network;
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
    /// <summary>Listening Post (spec 140): escort a scout to a scan spot near a hostile base, then fend off its waves.</summary>
    Scan,
}

public enum HoldOutPhase
{
    /// <summary>Scan profile only: the scout follows the players until they start the scan.</summary>
    Positioning,
    Holding,
}

/// <summary>Player orders on a hold-out (spec 141). They change sim state, so they go through HoldOutOrderInput.</summary>
public enum HoldOutOrder
{
    StartScan,
    Intensive,
    Leave,
}

/// <summary>
/// Rules type "HotCargoHoldOut" (spec 132, 137, 140, 141): keep a friendly target alive while waves attack it.
/// The clock runs only while a human ship (any ship, a player-built platform too) is within GuardRadius of the target.
/// So one ship must stay on station: the players either wait there or build a separate defence platform and fly on.
/// Waves come only while the target is guarded. The mission fails when the target dies.
/// The players can leave early: the objective completes and the mission pays the share of the waves they survived (spec 141).
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
        /// <summary>Scan profile: the hostile base that the scout scans.</summary>
        [Serialize(Optional = true), PortableHash] public SpawnedObjectSearch? BaseTag;
        [Serialize(Optional = true), PortableHash] public int TierOffset;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            invalid = !objects.TryFindObject(TargetTag, loc, rand, out var target);
            if (invalid) return null;
            SimSpawner.SpawnedObject? baseObj = null;
            if (Profile == HoldOutProfile.Scan && (BaseTag is null || !objects.TryFindObject(BaseTag, loc, rand, out baseObj))) { invalid = true; return null; }
            ObjectID id = target!.UniqueID ?? throw new InvalidOperationException($"target tag {TargetTag} found an object without an ID");
            int tier = Math.Max(1, ((CareerSimModeManager)sim.Mode).TierFields.GetTier((baseObj ?? target).Location.Center) + TierOffset);
            return new HoldOutObjective(new ObjectiveTargetShip(sim.RequireGame(), sim.NodeId(), id), Profile, tier)
            {
                BaseLocation = baseObj?.Location.Center,
                Phase = Profile == HoldOutProfile.Scan ? HoldOutPhase.Positioning : HoldOutPhase.Holding,
            };
        }
    }

    [Serialize] public ObjectiveTargetShip Target { get; private set; }
    [Serialize] public HoldOutProfile Profile { get; private set; }
    [Serialize] public int Tier { get; private set; }
    /// <summary>Game seconds with a human ship on station.</summary>
    [Serialize(Optional = true)] public double GuardedSeconds { get; set; }
    /// <summary>Player index of all wave ships of this objective. 0 until the first wave.</summary>
    [Serialize(Optional = true)] public int WaveAllegiance { get; private set; }
    [Serialize(Optional = true)] public HoldOutPhase Phase { get; private set; }
    [Serialize(Optional = true)] public Vector2? BaseLocation { get; private set; }
    [Serialize(Optional = true)] public Vector2? ScanSpot { get; private set; }
    /// <summary>Intensive scans ordered for the next wave.</summary>
    [Serialize(Optional = true)] public int PendingIntensive { get; private set; }
    /// <summary>Money factor from intensive scans, applied when the objective settles.</summary>
    [Serialize(Optional = true)] public float PayFactor { get; private set; } = 1f;
    /// <summary>Fraction of the pay that the mission rewards were set to. Null until the objective completes.</summary>
    [Serialize(Optional = true)] public float? Settled { get; private set; }

    /// <summary>UI only: true when the last sim tick found a human ship on station.</summary>
    public bool Guarded { get; private set; }
    int _displayedSeconds = -1;
    bool _displayedGuarded, _displayedInRange;

    public override string ScriptTypeName => "HotCargoHoldOut";

    public ID<SimObjectSpawner> WaveTag => new(Profile switch
    {
        HoldOutProfile.Siege => "hc_siege_drone",
        HoldOutProfile.Guard => "hc_guard_raider",
        _ => "hc_scan_raider",
    });

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

    public HoldOutTunables Settings(GameRoot game)
    {
        var t = MechanicsArea.TunablesFor(game);
        return Profile switch
        {
            HoldOutProfile.Siege => t.Siege,
            HoldOutProfile.Guard => t.Guard,
            HoldOutProfile.Scan => t.Scan,
            _ => throw new InvalidOperationException($"unknown hold-out profile {Profile}"),
        };
    }

    /// <summary>Spec 141: the share of the pay that leaving now keeps.</summary>
    public float EarnedFraction(GameRoot game)
    {
        var t = Settings(game);
        return MechanicsModel.EarnedFraction(GuardedSeconds, t.Seconds, t.WaveSeconds);
    }

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        Target.GetIntegrityHash(iHash);
        ((int)(GuardedSeconds * 10)).GetIntegrityHash(iHash);
        WaveAllegiance.GetIntegrityHash(iHash);
        ((int)Phase).GetIntegrityHash(iHash);
        PendingIntensive.GetIntegrityHash(iHash);
        ((int)(PayFactor * 1000)).GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => Target.MapNode;

    public override Circle? GetAreaLocation(GameRoot game)
    {
        if (Completion != CompletionState.InProgress) return null;
        if (Phase == HoldOutPhase.Positioning && BaseLocation is { } b && game.Sim?.MapNode?.UniqueID == Target.MapNodeID)
            return new Circle(b, MechanicsArea.TunablesFor(game).ScanRange);
        return Target.Location is { } at ? new Circle(at, Settings(game).GuardRadius) : null;
    }

    public override bool IsShipAssociated(MetaShip ship) => Completion == CompletionState.InProgress && Target.TargetMetaShip == ship;

    public override bool CheckExpired(GameRoot game) => Completion == CompletionState.Failed || !Target.IsAlive;

    public override void GetStringArgs(Dictionary<string, object?> args, GameRoot game)
    {
        base.GetStringArgs(args, game);
        var t = MechanicsArea.TunablesFor(game);
        args["target_ship"] = Target.GetXmlTargetName(game.Sim);
        args["hold_minutes"] = (int)Math.Round(Settings(game).Seconds / 60f);
        args["wave_count"] = MechanicsModel.Intervals(Settings(game).Seconds, Settings(game).WaveSeconds);
        args["scan_km"] = (t.ScanRange / 1000f).ToString("0.#");
        args["intensive_percent"] = (int)Math.Round(t.IntensivePayBonus * 100f);
    }

    public override bool CanFocusDisplayLine(GameRoot game) => Target.CanFocus();

    public override void OnDisplayLineFocused(GameRoot game) => Target.OnFocused();

    bool InScanRange(SimRoot? sim) =>
        Phase == HoldOutPhase.Positioning && BaseLocation is { } b && sim is not null && Target.TargetShip is { } s
        && s.DetWorldCenter.DistanceTo(b) <= MechanicsArea.TunablesFor(sim.Game).ScanRange;

    public override void RefreshDisplayLines(GameRoot game)
    {
        int left = MechanicsModel.SecondsLeft(Settings(game).Seconds, GuardedSeconds);
        bool inRange = InScanRange(game.Sim);
        if (left == _displayedSeconds && Guarded == _displayedGuarded && inRange == _displayedInRange) return;
        _displayedSeconds = left;
        _displayedGuarded = Guarded;
        _displayedInRange = inRange;
        OnDisplayLinesChanged();
    }

    /// <summary>Money that leaving now pays: the mission's money rewards × earned fraction × PayFactor, rounded like the rewards.</summary>
    int LeaveMoney(GameRoot game) =>
        Mission is null ? 0 : Mission.Rewards.OfType<MoneyReward>().Sum(m => MissionModel.ScaleReward(m.Money, EarnedFraction(game) * PayFactor, 100));

    /// <summary>A clickable HUD line that sends an order. Clicks are UI; the order runs as an MP input on every client.</summary>
    DisplayLine OrderLine(ITextProvider text, HoldOutOrder order) => new()
    {
        Text = text,
        State = CompletionState.InProgress,
        CanFocus = _ => true,
        OnFocused = g => { if (Mission is not null) g.NetManager.EnqueueInput(new HoldOutOrderInput(Mission.MissionID, order)); },
    };

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState)
    {
        var t = MechanicsArea.TunablesFor(game);
        string name = Target.GetXmlTargetName(game.Sim);
        if (Phase == HoldOutPhase.Positioning)
        {
            lines.Add(new DisplayLine { Text = Strings.KeyStringFmt("HotCargoMechanics/ScanEscortFmt", name, (t.ScanRange / 1000f).ToString("0.#")), State = Completion, CanFocus = CanFocusDisplayLine, OnFocused = OnDisplayLineFocused });
            if (Completion == CompletionState.InProgress && InScanRange(game.Sim)) lines.Add(OrderLine(Strings.KeyString("HotCargoMechanics/OrderStartScan"), HoldOutOrder.StartScan));
            return;
        }
        int left = MechanicsModel.SecondsLeft(Settings(game).Seconds, GuardedSeconds);
        lines.Add(new DisplayLine
        {
            Text = Strings.KeyStringFmt("HotCargoMechanics/HoldFmt", name, left / 60, (left % 60).ToString("00")),
            State = Completion,
            CanFocus = CanFocusDisplayLine,
            OnFocused = OnDisplayLineFocused,
        });
        if (Completion != CompletionState.InProgress) return;
        if (!Guarded) lines.Add(new DisplayLine { Text = Strings.KeyString("HotCargoMechanics/HoldPaused"), State = CompletionState.InProgress });
        if (Profile == HoldOutProfile.Scan && PendingIntensive < t.MaxIntensivePerWave)
            lines.Add(OrderLine(Strings.KeyStringFmt("HotCargoMechanics/OrderIntensiveFmt", (int)Math.Round(t.IntensivePayBonus * 100f), PayFactor.ToString("0.00")), HoldOutOrder.Intensive));
        if (EarnedFraction(game) > 0f)
            lines.Add(OrderLine(Strings.KeyStringFmt("HotCargoMechanics/OrderLeaveFmt", LeaveMoney(game)), HoldOutOrder.Leave));
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
        if (Phase == HoldOutPhase.Positioning)
        {
            // The scout follows the nearest player ship until the players pick the scan spot.
            if (SimQueries.NearestHumanShip(sim, target.DetWorldCenter) is { } lead) Steer(target, lead.DetWorldCenter, 250f);
            return;
        }
        if (ScanSpot is { } spot) Steer(target, spot, 1f);
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
            Settle(1f);
            Complete();
        }
    }

    /// <summary>Moves the AI origin to a point, like the escort does. A small change below minMove keeps the AI from re-planning every tick.</summary>
    static void Steer(Ship ship, Vector2 to, float minMove)
    {
        if (ship.AI is not { } ai) return;
        if (ai.Parameters?.PatrolOrigin is { } origin && origin.DistanceTo(to) < minMove) return;
        AIParameters p = ai.Parameters?.Clone() ?? new AIParameters();
        p.PatrolOrigin = to;
        ai.Parameters = p;
    }

    /// <summary>Runs an order from HoldOutOrderInput. Every client runs it in the same input order. Invalid orders do nothing.</summary>
    internal void Order(SimRoot sim, HoldOutOrder order, int playerIndex)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim.NodeId() != Target.MapNodeID) return;
        var t = MechanicsArea.TunablesFor(sim.RequireGame());
        switch (order)
        {
            case HoldOutOrder.StartScan when InScanRange(sim):
                Phase = HoldOutPhase.Holding;
                ScanSpot = Target.TargetShip!.DetWorldCenter;
                Log.Info($"listening post: player {playerIndex} started the scan at {ScanSpot}");
                break;
            case HoldOutOrder.Intensive when Profile == HoldOutProfile.Scan && Phase == HoldOutPhase.Holding && PendingIntensive < t.MaxIntensivePerWave:
                PendingIntensive++;
                PayFactor += t.IntensivePayBonus;
                Log.Info($"listening post: player {playerIndex} ordered an intensive scan; pay x{PayFactor:0.00}");
                break;
            case HoldOutOrder.Leave when Phase == HoldOutPhase.Holding && EarnedFraction(sim.RequireGame()) > 0f:
                float earned = EarnedFraction(sim.RequireGame());
                Log.Info($"hold-out {Profile}: player {playerIndex} left early at {GuardedSeconds:0} s, keeping {earned:0.00} of the pay");
                Settle(earned);
                Complete();
                break;
            default:
                Log.Info($"hold-out {Profile}: order {order} from player {playerIndex} does not apply now");
                return;
        }
        OnDisplayLinesChanged();
    }

    /// <summary>
    /// Sets the mission's money and fame rewards to their share (spec 141). Money also takes PayFactor (intensive scans).
    /// The rewards pay later through ReceiveRewards, as usual. Runs once.
    /// </summary>
    void Settle(float fraction)
    {
        if (Mission is null || Settled is not null) return;
        Settled = fraction;
        foreach (Reward r in Mission.Rewards)
            switch (r)
            {
                case MoneyReward m: m.Money = MissionModel.ScaleReward(m.Money, fraction * PayFactor, 100); break;
                case FameReward f: f.Fame = MissionModel.ScaleReward(f.Fame, fraction, 1); break;
            }
    }

    /// <summary>Spawns one wave around the target. Sim tick only (sim.Rand). Public for the autotest.</summary>
    internal void SpawnWave(SimRoot sim, Ship target, HoldOutTunables t)
    {
        var career = (CareerSimModeManager)sim.Mode;
        var mt = MechanicsArea.TunablesFor(sim.RequireGame());
        if (WaveAllegiance == 0) WaveAllegiance = career.AllocUniquePlayerIndex();
        int live = SimQueries.TaggedShips(sim, WaveTag).Count(s => s.PlayerIndex == WaveAllegiance);
        int boost = PendingIntensive;
        PendingIntensive = 0;
        int count = MechanicsModel.WaveShipCount(t.ShipsBase + boost * mt.IntensiveExtraShips, t.ShipsPerTier, Tier, live, t.MaxLiveShips + (int)MathF.Ceiling(boost * mt.IntensiveExtraShips));
        int shipTier = MechanicsModel.WaveShipTier(t.ShipTierBase + boost * mt.IntensiveExtraTier, t.ShipTierPerTier, Tier, t.ShipTierMax);
        Vector2 center = target.DetWorldCenter;
        // Scan waves come from the base side; other waves from a random side.
        float direction = BaseLocation is { } b ? MathF.Atan2(b.Y - center.Y, b.X - center.X) : sim.Rand.Direction();
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
        Log.Info($"hold-out {Profile}: wave of {count} tier-{shipTier} ships at {GuardedSeconds:0} s ({live} still live, {boost} intensive)");
    }

    public override int GetTypeIntegrityHash() => 0x4843_484F; // "HCHO"
}

/// <summary>A player order on a hold-out objective (spec 141), found by mission ID. Runs in lockstep on every client.</summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoHoldOutOrder")]
internal sealed class HoldOutOrderInput : MPInput
{
    [Serialize] public ObjectID MissionID { get; private set; }
    [Serialize] public HoldOutOrder Order { get; private set; }

    public HoldOutOrderInput(ObjectID missionId, HoldOutOrder order)
    {
        MissionID = missionId;
        Order = order;
    }

    HoldOutOrderInput() { }

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false) => Errors.Guard("hold-out order", () =>
    {
        if (game.Sim is not { } sim || game.Mode is not CareerGameModeManager mode) return;
        var mission = mode.Missions.FirstOrDefault(m => m.MissionID.Equals(MissionID));
        if (mission is null) return;
        var found = new List<HoldOutObjective>();
        foreach (var o in mission.Objectives) o.GetObjectives(found);
        foreach (var o in found) o.Order(sim, Order, playerIndex);
    });
}

[HarmonyLib.HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.AllowMPInput))]
static class AllowHoldOutOrderPatch
{
    static void Postfix(MPInput mpInput, ref bool __result)
    {
        if (mpInput is HoldOutOrderInput) __result = true;
    }
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

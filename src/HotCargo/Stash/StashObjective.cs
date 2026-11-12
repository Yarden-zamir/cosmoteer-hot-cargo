using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Cosmoteer.Simulation;
using Cosmoteer.Ships;
using Cosmoteer.Simulation.Stasis;
using Halfling.ObjectText;
using Cosmoteer.Game.Multiplayer;
using Halfling;
using Halfling.Geometry;
using Halfling.Gui.Components.Text;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HotCargo.Core;
using HotCargo.Tech;
using HotCargo.Ux;

namespace HotCargo.Stash;

/// <summary>
/// Rules type "HotCargoStash" (spec 970 to 975). A rumoured stash at a place in the system. The longer the players wait after the offer,
/// the higher the chance that the corporation found it first. The objective shows that chance in the offer and the mission log.
/// When a human ship comes within TriggerDistance of the site, it rolls with the chance at that moment: loot (the rewards and maybe tech),
/// empty, or an ambush (the shady trap ambush, spec 40).
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoStash")]
internal sealed class StashObjective : Objective
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoStash")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        [Serialize, PortableHash] public SpawnedObjectSearch SiteTag = null!;
        [Serialize(Optional = true), PortableHash] public float TriggerDistance = 1200f;
        /// <summary>Created with the tier at the site. Money rewards × Stash.LootMoneyFactor.</summary>
        [Serialize(Optional = true), PortableHash] public RewardSpawner[]? LootRewards;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            invalid = !objects.TryFindObject(SiteTag, loc, rand, out var site);
            if (invalid) return null;
            var game = sim.RequireGame();
            double now = Run.Current is { } run && run.Game == game ? run.State.GameSeconds : 0;
            var objective = new StashObjective(sim.NodeId(), site!.Location.Center, TriggerDistance, now, rand.UInt64());
            int tier = Math.Max(1, ((CareerSimModeManager)sim.Mode).TierFields.GetTier(site.Location.Center));
            float factor = StashArea.TunablesFor(game).LootMoneyFactor;
            foreach (RewardSpawner r in LootRewards ?? [])
                if (r.CreateReward(mission, rand, objects, loc, tier, null) is { } reward)
                {
                    if (reward is MoneyReward m) m.Money = (int)(Math.Round(m.Money * factor / 500.0) * 500);
                    objective.AddReward(reward);
                }
            return objective;
        }
    }

    [Serialize] public ObjectID NodeId { get; private set; }
    [Serialize] public Vector2 Site { get; private set; }
    [Serialize] public float TriggerDistance { get; private set; }
    /// <summary>Run game seconds at the offer. The found-first chance counts from here.</summary>
    [Serialize] public double OfferedAt { get; private set; }
    [Serialize] public ulong RollSeed { get; private set; }
    [Serialize(Optional = true)] public StashState State { get; private set; }
    [Serialize(Optional = true)] public List<ObjectiveTargetShip> Ambushers { get; private set; } = [];

    public override string ScriptTypeName => "HotCargoStash";

    public StashObjective(ObjectID nodeId, Vector2 site, float triggerDistance, double offeredAt, ulong rollSeed)
    {
        if (triggerDistance <= 0f) throw new ArgumentOutOfRangeException(nameof(triggerDistance), triggerDistance, "must be above 0");
        NodeId = nodeId;
        Site = site;
        TriggerDistance = triggerDistance;
        OfferedAt = offeredAt;
        RollSeed = rollSeed;
    }

    [GenericConstructor]
    StashObjective(GenericSerialReader reader) => reader.ReflectiveRead(this);

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        ((int)State).GetIntegrityHash(iHash);
        Ambushers.Count.GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => null;

    public override MapNode? GetFirstMapNode(GameRoot game) => game.Map.Nodes[NodeId];

    public override Circle? GetAreaLocation(GameRoot game) =>
        State == StashState.Waiting && game.CurrentMapNode.UniqueID.Equals(NodeId) ? new Circle(Site, TriggerDistance) : null;

    public override bool IsShipAssociated(MetaShip ship) =>
        State == StashState.Ambush && Completion == CompletionState.InProgress && Ambushers.Any(a => a.TargetMetaShip == ship);

    /// <summary>Spec 971: the found-first chance now, from the run time. Before a run is active, the chance at the offer.</summary>
    internal float FoundFirstNow(GameRoot game)
    {
        var t = StashArea.TunablesFor(game);
        double now = Run.Current is { } run && run.Game == game ? run.State.GameSeconds : OfferedAt;
        return StashModel.FoundFirstChance(now - OfferedAt, t.FoundFirstBase, t.FoundFirstPerMinute, t.FoundFirstMax);
    }

    /// <summary>Tests: the offer becomes older by this many game seconds.</summary>
    internal void Age(double seconds) => OfferedAt -= seconds;

    /// <summary>The loot shows only while the stash may still hold it.</summary>
    public override void GetRewardDisplayLines(List<(ITextProvider Text, ITextProvider? Tooltip)> lines, GameRoot game, Dictionary<int, int>? implicitFame = null)
    {
        if (State is StashState.Waiting or StashState.Looted) base.GetRewardDisplayLines(lines, game, implicitFame);
    }

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState)
    {
        // The waiting line is live text: the found-first chance rises while the offer or the log shows it.
        Func<string> text = State switch
        {
            StashState.Waiting => () => FindLine(game),
            StashState.Looted => () => StashArea.Text("LineLooted"),
            StashState.Empty => () => StashArea.Text("LineEmpty"),
            _ when Completion == CompletionState.InProgress => () => StashArea.Format("LineAmbushFmt", Ambushers.Count(a => !a.IsAlive), Ambushers.Count),
            _ => () => StashArea.Text("LineAmbushDone"),
        };
        lines.Add(new DisplayLine { Text = new LiveText(text), State = Completion });
    }

    /// <summary>Spec 974: "Find the stash. Found first: 35%". Tests log it.</summary>
    internal string FindLine(GameRoot game) =>
        StashArea.Format("FindFmt", StashModel.Percent(FoundFirstNow(game)), StashModel.Percent(StashArea.TunablesFor(game).LootTechChance));

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        game.EventHub.Tick += OnTick;
        game.SwitchedSimulation += OnSwitchedSimulation;
        foreach (var a in Ambushers) a.Killed += OnAmbusherKilled;
        if (!isLoading && State == StashState.Ambush) OnAmbusherKilled(null, EventArgs.Empty);
    }

    public override void UnregisterEvents(GameRoot game)
    {
        game.EventHub.Tick -= OnTick;
        game.SwitchedSimulation -= OnSwitchedSimulation;
        foreach (var a in Ambushers) a.Killed -= OnAmbusherKilled;
    }

    void OnTick(object? sender, SimRoot sim)
    {
        if (Mission is null || State != StashState.Waiting || sim != Mission.Game.Sim) return;
        if (!sim.NodeId().Equals(NodeId) || SimQueries.NearestHumanShip(sim, Site) is not { } nearest) return;
        if (nearest.DetWorldCenter.DistanceTo(Site) <= TriggerDistance) Resolve(sim, nearest.DetWorldCenter);
    }

    /// <summary>Spec 972: rolls with the found-first chance now. Tests force the outcome with <see cref="StashArea.ForcedOutcome"/>.</summary>
    void Resolve(SimRoot sim, Vector2 near)
    {
        var game = sim.RequireGame();
        var t = StashArea.TunablesFor(game);
        float chance = FoundFirstNow(game);
        var rand = new FastRand(RollSeed);
        float foundRoll = rand.Single(), emptyRoll = rand.Single(), techRoll = rand.Single();
        var outcome = StashArea.ForcedOutcome ?? StashModel.Outcome(foundRoll, chance, emptyRoll, t.EmptyShare);
        StashArea.ForcedOutcome = null;
        Log.Info($"stash resolved: chance={chance:0.000} roll={foundRoll:0.000} outcome={outcome}");
        switch (outcome)
        {
            case StashState.Looted:
                State = StashState.Looted;
                string line = StashArea.Text("Looted");
                if (techRoll < t.LootTechChance && t.LootTechUnits > 0)
                {
                    int added = Vaults.Add(sim, t.LootTechUnits);
                    line += " " + (added == t.LootTechUnits ? StashArea.Format("TechFmt", added) : StashArea.Format("TechNoRoomFmt", added, t.LootTechUnits - added));
                }
                Say(game, line);
                Complete();
                break;
            case StashState.Empty:
                State = StashState.Empty;
                Say(game, StashArea.Text("Empty"));
                FinishWithoutReward();
                break;
            default:
                Spring(sim, near);
                break;
        }
    }

    void Spring(SimRoot sim, Vector2 near)
    {
        try
        {
            var tag = new ID<SimObjectSpawner>(Missions.ShadyTrapObjective.AmbushTag);
            var before = SimQueries.TaggedShips(sim, tag).Select(s => s.Id).ToHashSet();
            Missions.ShadyAmbush.Generator.Generate(sim, new FastRand(RollSeed, 0x5354_4153_48UL), loadInstantly: true, rootLoc: near);
            var spawned = SimQueries.TaggedShips(sim, tag).Select(s => s.Id).Where(id => !before.Contains(id)).Order().ToList();
            if (spawned.Count == 0) throw new InvalidOperationException("the ambush spawner made no ships");
            Ambushers = spawned.Select(id => new ObjectiveTargetShip(sim.RequireGame(), sim.NodeId(), id)).ToList();
        }
        catch (Exception ex)
        {
            // A broken ambush file must not crash the sim tick. The stash is then only empty.
            Errors.Report("stash ambush", ex.Message, ex);
            State = StashState.Empty;
            FinishWithoutReward();
            return;
        }
        State = StashState.Ambush;
        foreach (var a in Ambushers) a.Killed += OnAmbusherKilled;
        Log.Info($"stash ambush: {Ambushers.Count} ships");
        Say(sim.RequireGame(), StashArea.Text("Ambush"));
        App.Audio.Play(GameApp.Rules.CareerMode.Wanted.WantedAlertSound, AudioGroups.Interface);
        OnDisplayLinesChanged();
    }

    static void Say(GameRoot game, string line) =>
        Run.OnNextFrame(() => UxKit.Guard("stash line", () => { game.Gui.ShowAreaName(line); UxKit.ChatLine(game, line); }));

    void OnAmbusherKilled(object? sender, EventArgs e)
    {
        if (Completion != CompletionState.InProgress || State != StashState.Ambush) return;
        if (Ambushers.All(a => !a.IsAlive)) FinishWithoutReward();
        else OnDisplayLinesChanged();
    }

    /// <summary>Leaving the system during an ambush ends the mission without loot.</summary>
    void OnSwitchedSimulation(object? sender, EventArgs e)
    {
        if (Mission is null || State != StashState.Ambush || Completion != CompletionState.InProgress) return;
        if (!Mission.Game.CurrentMapNode.UniqueID.Equals(NodeId)) FinishWithoutReward();
    }

    /// <summary>Completes without giving the rewards. Objective.Complete() always gives them.</summary>
    void FinishWithoutReward()
    {
        IsActive = false;
        Completion = CompletionState.Completed;
        OnDisplayLinesChanged();
    }

    public override int GetTypeIntegrityHash() => 0x4843_5348; // "HCSH"
}

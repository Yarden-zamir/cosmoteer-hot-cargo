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
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling;
using Halfling.Geometry;
using Halfling.Gui.Components.Text;
using Halfling.ObjectText;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HotCargo.Core;

namespace HotCargo.Missions;

public enum ShadyTrapState
{
    /// <summary>The players have not reached the site yet.</summary>
    Waiting,
    /// <summary>The roll said honest. The objective completed without a line or a reward.</summary>
    Honest,
    /// <summary>The ambush spawned. The objective completes with its rewards when all ambushers are gone.</summary>
    Sprung,
}

/// <summary>
/// Rules type "HotCargoShadyTrap" (spec requirements 39 and 40). Put it in an AnyOrder next to the real objective of a shady offer.
/// It shows nothing until it springs, so the offer looks like a normal mission.
/// When a human ship comes within TriggerDistance of the site ship (or the site ship dies), it rolls the trap chance with the heat at that moment.
/// A trap spawns the ambush from missions/shady_ambush.rules next to the nearest human ship and pays SurvivalRewards when all ambushers are gone.
/// If the players leave the system during the ambush, the objective completes without the survival reward.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoShadyTrap")]
internal sealed class ShadyTrapObjective : Objective
{
    public const string AmbushTag = "hc_shady_ambush";

    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoShadyTrap")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        [Serialize, PortableHash] public SpawnedObjectSearch SiteShipTag = null!;
        [Serialize(Optional = true), PortableHash] public float TriggerDistance = 2500f;
        /// <summary>Created with the tier at the site, so they match the normal reward of the offer. Use HotCargoScaled to apply ShadyTrapSurvivalReward.</summary>
        [Serialize(Optional = true), PortableHash] public RewardSpawner[]? SurvivalRewards;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            invalid = !objects.TryFindObject(SiteShipTag, loc, rand, out var site);
            if (invalid) return null;
            ObjectID siteId = site!.UniqueID ?? throw new InvalidOperationException($"site tag {SiteShipTag} found an object without an ID");
            var objective = new ShadyTrapObjective(new ObjectiveTargetShip(sim.RequireGame(), sim.NodeId(), siteId), TriggerDistance, rand.UInt64());
            int tier = Math.Max(1, ((CareerSimModeManager)sim.Mode).TierFields.GetTier(site.Location.Center));
            foreach (RewardSpawner r in SurvivalRewards ?? [])
                if (r.CreateReward(mission, rand, objects, loc, tier, null) is { } reward) objective.AddReward(reward);
            return objective;
        }
    }

    [Serialize] public ObjectiveTargetShip Site { get; private set; }
    [Serialize] public float TriggerDistance { get; private set; }
    [Serialize] public ulong RollSeed { get; private set; }
    [Serialize(Optional = true)] public ShadyTrapState State { get; private set; }
    [Serialize(Optional = true)] public Vector2? LastSiteLocation { get; private set; }
    [Serialize(Optional = true)] public List<ObjectiveTargetShip> Ambushers { get; private set; } = [];

    public override string ScriptTypeName => "HotCargoShadyTrap";

    public ShadyTrapObjective(ObjectiveTargetShip site, float triggerDistance, ulong rollSeed)
    {
        Site = site ?? throw new ArgumentNullException(nameof(site));
        if (triggerDistance <= 0f) throw new ArgumentOutOfRangeException(nameof(triggerDistance), triggerDistance, "must be above 0");
        TriggerDistance = triggerDistance;
        RollSeed = rollSeed;
    }

    [GenericConstructor]
    ShadyTrapObjective(GenericSerialReader reader)
    {
        Site = null!;
        reader.ReflectiveRead(this);
    }

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        Site.GetIntegrityHash(iHash);
        ((int)State).GetIntegrityHash(iHash);
        Ambushers.Count.GetIntegrityHash(iHash);
    }

    /// <summary>No tier, so the trap does not change the danger shown on the offer.</summary>
    public override Range<int>? GetTierRange(SimRoot curSim) => null;

    public override MapNode? GetFirstMapNode(GameRoot game) => Site.MapNode;

    public override bool IsShipAssociated(MetaShip ship) =>
        State == ShadyTrapState.Sprung && Completion == CompletionState.InProgress && Ambushers.Any(a => a.TargetMetaShip == ship);

    /// <summary>The survival reward stays hidden until the trap springs.</summary>
    public override void GetRewardDisplayLines(List<(ITextProvider Text, ITextProvider? Tooltip)> lines, GameRoot game, Dictionary<int, int>? implicitFame = null)
    {
        if (State == ShadyTrapState.Sprung) base.GetRewardDisplayLines(lines, game, implicitFame);
    }

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState)
    {
        if (State != ShadyTrapState.Sprung) return;
        int defeated = Ambushers.Count(a => !a.IsAlive);
        lines.Add(new DisplayLine
        {
            Text = Completion == CompletionState.InProgress
                ? Strings.KeyStringFmt("Objectives/CountableFmt", Strings.KeyString("HotCargoObjectives/SurviveAmbush"), defeated, Ambushers.Count)
                : Strings.KeyString("HotCargoObjectives/SurviveAmbush"),
            State = Completion,
        });
    }

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        game.EventHub.Tick += OnTick;
        game.SwitchedSimulation += OnSwitchedSimulation;
        foreach (var a in Ambushers) a.Killed += OnAmbusherKilled;
        if (!isLoading && State == ShadyTrapState.Sprung) OnAmbusherKilled(null, EventArgs.Empty);
    }

    public override void UnregisterEvents(GameRoot game)
    {
        game.EventHub.Tick -= OnTick;
        game.SwitchedSimulation -= OnSwitchedSimulation;
        foreach (var a in Ambushers) a.Killed -= OnAmbusherKilled;
    }

    void OnTick(object? sender, SimRoot sim)
    {
        if (Mission is null || State != ShadyTrapState.Waiting || sim != Mission.Game.Sim || sim.NodeId() != Site.MapNodeID) return;
        if (Site.Location is { } siteLoc) LastSiteLocation = siteLoc;
        if (LastSiteLocation is not { } at) return;
        if (SimQueries.NearestHumanShip(sim, at) is not { } nearest) return;
        bool reached = Site.Location is null || nearest.DetWorldCenter.DistanceTo(at) <= TriggerDistance;
        if (reached) Resolve(sim, nearest.DetWorldCenter);
    }

    /// <summary>Rolls the trap with the heat now (spec requirement 39) and springs it or completes quietly.</summary>
    void Resolve(SimRoot sim, Vector2 near)
    {
        var game = sim.RequireGame();
        float heat = Run.Current is { } run && run.Game == game ? run.State.Heat : 0f;
        var t = MissionArea.TunablesFor(game);
        float chance = MissionModel.TrapChance(t.ShadyTrapChanceBase, t.ShadyTrapChancePerHeat, heat);
        float roll = new FastRand(RollSeed).Single();
        bool trap = MissionModel.IsTrap(roll, chance);
        Log.Info($"shady offer resolved: heat={heat:0.0} chance={chance:0.000} roll={roll:0.000} trap={trap}");
        if (trap) Spring(sim, near);
        else
        {
            State = ShadyTrapState.Honest;
            FinishWithoutReward();
        }
    }

    /// <summary>Spawns the ambush next to the given point. Public for the autotest.</summary>
    internal void Spring(SimRoot sim, Vector2 near)
    {
        try
        {
            var tag = new ID<SimObjectSpawner>(AmbushTag);
            var before = SimQueries.TaggedShips(sim, tag).Select(t => t.Id).ToHashSet();
            ShadyAmbush.Generator.Generate(sim, new FastRand(RollSeed, 0x5348_4144_59UL), loadInstantly: true, rootLoc: near);
            var spawned = SimQueries.TaggedShips(sim, tag).Select(t => t.Id).Where(id => !before.Contains(id)).ToList();
            if (spawned.Count == 0) throw new InvalidOperationException("the ambush spawner made no ships");
            Ambushers = spawned.Select(id => new ObjectiveTargetShip(sim.RequireGame(), sim.NodeId(), id)).ToList();
        }
        catch (Exception ex)
        {
            // A broken ambush file must not crash the sim tick. The offer then pays like an honest one.
            Errors.Report("shady ambush", ex.Message, ex);
            State = ShadyTrapState.Honest;
            FinishWithoutReward();
            return;
        }
        State = ShadyTrapState.Sprung;
        foreach (var a in Ambushers) a.Killed += OnAmbusherKilled;
        Log.Info($"shady trap sprung: {Ambushers.Count} ambushers");
        // UI and audio only, like a hunter wave alert: the players must read the ambush as a setup, not as random pirates.
        Ux.UxKit.CenterText(sim.RequireGame(), Strings.GetText("HotCargoObjectives/TrapSprung"));
        App.Audio.Play(GameApp.Rules.CareerMode.Wanted.WantedAlertSound, AudioGroups.Interface);
        OnDisplayLinesChanged();
    }

    void OnAmbusherKilled(object? sender, EventArgs e)
    {
        if (Completion != CompletionState.InProgress) return;
        if (Ambushers.All(a => !a.IsAlive)) Complete();
        else OnDisplayLinesChanged();
    }

    void OnSwitchedSimulation(object? sender, EventArgs e)
    {
        if (Mission is null || State != ShadyTrapState.Sprung || Completion != CompletionState.InProgress) return;
        if (Mission.Game.CurrentMapNode.UniqueID != Site.MapNodeID) FinishWithoutReward();
    }

    /// <summary>Completes without giving the survival rewards. Objective.Complete() always gives them.</summary>
    void FinishWithoutReward()
    {
        IsActive = false;
        Completion = CompletionState.Completed;
        OnDisplayLinesChanged();
    }

    public override int GetTypeIntegrityHash() => 0x4843_5354; // "HCST"
}

/// <summary>Loads the ambush spawner from missions/shady_ambush.rules in the mod folder. It is not part of the game rules tree.</summary>
internal static class ShadyAmbush
{
    static SimulationGenerator? _generator;

    public static string FilePath => Path.Combine(Mod.ModRoot, "missions", "shady_ambush.rules");

    public static SimulationGenerator Generator => _generator ??= Load();

    static SimulationGenerator Load()
    {
        var generator = App.ObjectTextSerializer.ReadFromPath<SimulationGenerator>(new OTFile(FilePath), "Ambush")
            ?? throw new InvalidDataException($"Ambush missing in {FilePath}");
        if (generator.Spawners is not { Length: > 0 }) throw new InvalidDataException($"Ambush/Spawners is empty in {FilePath}");
        Log.Info($"shady ambush loaded: {generator.Spawners.Length} spawners");
        return generator;
    }
}

using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Missions;

/// <summary>
/// Entry point of the Missions area (spec section 11). Mission content is rules (mod/missions); the C# part is
/// the custom objective and reward types (EscortObjective, ShadyTrapObjective, ScaledRewardSpawner) and autotest commands.
/// </summary>
public static class MissionArea
{
    /// <summary>Hot Cargo mission metatypes, by the short name that autotest commands use.</summary>
    static readonly Dictionary<string, string> Metatypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["escort"] = "HotCargoEscort",
        ["assault"] = "HotCargoFleetAssault",
        ["shady"] = "HotCargoShadyBounty",
    };

    public static void Register()
    {
        foreach (var (name, metatype) in Metatypes)
        {
            var id = new ID<MissionMetatype>(metatype);
            Autotest.Fields[$"hc_offers_{name}"] = () => Offers(RequireSim()).Count(m => m.MetatypeID.Equals(id));
            Autotest.Fields[$"hc_active_{name}"] = () => RequireRun().Mode.Missions.Count(m => m.MetatypeID.Equals(id) && m.Completion == CompletionState.InProgress);
            Autotest.Fields[$"hc_completed_{name}"] = () => RequireRun().Mode.Missions.Count(m => m.MetatypeID.Equals(id) && m.Completion == CompletionState.Completed);
        }
        Autotest.Commands["hc_generate"] = TestGenerate;
        Autotest.Commands["hc_accept"] = TestAccept;
        Autotest.Commands["hc_escort_goto"] = _ => TeleportPlayers(RequireEscort().Escort.Location ?? throw new InvalidOperationException("escort is not in this system"));
        Autotest.Commands["hc_escort_arrive"] = _ => TestEscortArrive();
        Autotest.Commands["hc_escort_diag"] = arg => LogEscort(arg);
        // Test slots start with empty settings, so tutorial codex pages are unseen. The combat tutorial pauses the game
        // the first time an enemy is in sight without a station in sight, which freezes the escort. Tests turn that off.
        Autotest.Commands["hc_assault_goto"] = _ => TeleportPlayers(TaggedShips(RequireSim(), "hc_assault_ally").First().Location);
        Autotest.Commands["hc_shady_spring"] = _ => TestShadySpring();
        Autotest.Commands["hc_kill_ambush"] = _ => TestKillAmbush();
        Autotest.Commands["hc_shady_goto"] = _ => TeleportPlayers(RequireTrap().Site.Location ?? throw new InvalidOperationException("shady site is not in this system"));
        // Remembers money and the escort distance, for hc_money_gain and hc_escort_progress.
        Autotest.Commands["hc_mark"] = _ =>
        {
            _markedMoney = RequireRun().Game.LocalPlayerState.Money;
            _markedEscortDist = FindObjective<EscortObjective>() is null ? 0f : EscortDistance();
        };
        Autotest.Commands["hc_ambush_file"] = _ => Autotest.Check(ShadyAmbush.Generator.Spawners.Length > 0, $"shady ambush file loads ({ShadyAmbush.FilePath})");

        Autotest.Fields["hc_money_gain"] = () => RequireRun().Game.LocalPlayerState.Money - _markedMoney;
        Autotest.Fields["hc_escort_steering"] = () => RequireEscort() is var o && o.Escort.TargetShip?.AI?.Parameters?.PatrolOrigin is { } p && p == o.Destination ? 1 : 0;
        Autotest.Fields["hc_escort_dist"] = () => EscortDistance();
        Autotest.Fields["hc_escort_progress"] = () => _markedEscortDist - EscortDistance();
        Autotest.Fields["hc_escort_done"] = () => RequireEscort().Completion == CompletionState.Completed ? 1 : 0;
        Autotest.Fields["hc_trap_done"] = () => RequireTrap().Completion == CompletionState.Completed ? 1 : 0;
        Autotest.Fields["hc_ambushers"] = () => RequireTrap().Ambushers.Count(a => a.IsAlive);
        Autotest.Fields["hc_trap_state"] = () => (int)RequireTrap().State;
        Autotest.Fields["hc_trap_chance_now"] = () =>
        {
            var r = RequireRun();
            return MissionModel.TrapChance(r.T.Missions.ShadyTrapChanceBase, r.T.Missions.ShadyTrapChancePerHeat, r.State.Heat);
        };
        // Relations (spec 36, 37): counts of tagged ships that are enemies of a human player, or of the given target tag.
        Autotest.Fields["hc_allies"] = () => TaggedShips(RequireSim(), "hc_assault_ally").Count;
        Autotest.Fields["hc_allies_vs_player"] = () => EnemiesOfPlayer("hc_assault_ally");
        Autotest.Fields["hc_allies_vs_target"] = () => EnemiesOf("hc_assault_ally", "hc_assault_station");
        Autotest.Fields["hc_escort_vs_player"] = () => EnemiesOfPlayer("hc_escort_ship");
        Autotest.Fields["hc_raiders_vs_escort"] = () => EnemiesOf("hc_escort_raider", "hc_escort_ship");
        Autotest.Fields["hc_raiders_vs_player"] = () => EnemiesOfPlayer("hc_escort_raider");
        Autotest.Fields["hc_ambush_vs_player"] = () => EnemiesOfPlayer(ShadyTrapObjective.AmbushTag);
    }

    /// <summary>
    /// Mission tunables for code that runs in a game. The first system of a new career generates before the run starts,
    /// so it reads the tunables file like Run.Start does.
    /// </summary>
    internal static MissionTunables TunablesFor(GameRoot game) =>
        Run.Current is { } run && run.Game == game ? run.T.Missions : (TunablesFile.Read()?.Values ?? new Tunables()).Missions;

    static int _markedMoney;
    static float _markedEscortDist;

    static float EscortDistance() => RequireEscort() is var o && o.Escort.Location is { } at ? at.DistanceTo(o.Destination) : -1f;

    static Run RequireRun() => Autotest.RequireRun();
    static SimRoot RequireSim() => RequireRun().Game.Sim ?? throw new InvalidOperationException("no sim");

    /// <summary>All offered missions in the current system: every station list, the system list and the galaxy list.</summary>
    static IEnumerable<Mission> Offers(SimRoot sim)
    {
        foreach (MetaSpaceStation station in SpaceStation.GetStations(sim, onlyOperating: false))
            foreach (Mission m in station.GetProvidedMissions() ?? new MissionList())
                yield return m;
        foreach (Mission m in ((CareerSimModeManager)sim.Mode).SimGlobalAvailableMissions) yield return m;
        foreach (Mission m in ((CareerGameModeManager)sim.RequireGame().Mode).GameGlobalAvailableMissions) yield return m;
    }

    /// <summary>
    /// Runs the Hot Cargo spawner groups now, centered on the first player ship, with SpawnChance forced to 1.
    /// Arg: escort, assault, shady or all. Single player test helper; it uses sim.Rand from a GUI frame.
    /// </summary>
    static void TestGenerate(string arg)
    {
        var sim = RequireSim();
        var mode = (CareerSimModeManager)sim.Mode;
        string[] names = arg is "" or "all" ? [.. Metatypes.Keys] : [arg];
        var groups = new List<SimObjectSpawner>();
        foreach (string name in names)
        {
            if (!Metatypes.ContainsKey(name)) throw new InvalidOperationException($"unknown group '{name}'");
            var tag = new ID<SimObjectSpawner>($"hc_group_{name}");
            groups.Add(AllSpawners(mode.SectorType.SystemGenerator.Spawners)
                .OfType<SimObjectSpawner>().FirstOrDefault(s => s.Tags?.Contains(tag) == true)
                ?? throw new InvalidOperationException($"spawner group {tag} not found in the sector generator; are the mission rules loaded?"));
        }
        var objects = ExistingSpawnTags(sim);
        var parentTree = new List<SimSpawner.SpawnedObject> { new(sim.Ships.HumanPlayerSignificantShips.First().DetWorldCenter) };
        uint batch = 0, fleet = 0;
        var saved = groups.Select(g => g.SpawnChance).ToList();
        try
        {
            foreach (var g in groups)
            {
                g.SpawnChance = 1f;
                g.Spawn(sim, sim.Rand, parentTree, 0, ref batch, ref fleet, loadInstantly: true, [], objects, mode.GetSpawnFlags(), 0f);
            }
        }
        finally
        {
            for (int i = 0; i < groups.Count; i++) groups[i].SpawnChance = saved[i];
        }
        Log.Info($"AUTOTEST generated Hot Cargo groups: {string.Join(", ", names)}");
        Autotest.Wait(2);
    }

    /// <summary>
    /// Spawn tags of the objects that already exist, with their IDs.
    /// SimulationGenerator's addExistingSpawnTags leaves out the IDs of objects in stasis, and missions that spawn at a station need them.
    /// </summary>
    static SpawnedObjectCollection ExistingSpawnTags(SimRoot sim)
    {
        var objects = new SpawnedObjectCollection();
        void Add(HashSet<ID<SimObjectSpawner>>? tags, SimSpawner.SpawnedObject so)
        {
            foreach (var tag in tags ?? [])
            {
                if (!objects.TryGetValue(tag, out var list)) objects.Add(tag, list = []);
                list.Add(so);
            }
        }
        for (int i = 0; i < sim.Ships.Count; i++) Add(sim.Ships[i].Tags, new(sim.Ships[i], null, null));
        foreach (StasisSpawner s in sim.Stasis.OrderBy(s => s.ReservedObjectID))
        {
            var tags = s is StasisDoodadSpawner d ? d.Tags : StasisTags(s);
            Add(tags, new(new Circle(s.Location, 0f), s.ReservedObjectID, s, playerIndex: s.ObjectAllegiance));
        }
        foreach (var d in sim.Doodads) Add(d.Tags, new(new Circle(d.Location, d.Rules.SpawnRadius), d.UniqueID));
        return objects;
    }

    static IEnumerable<SimSpawner> AllSpawners(IEnumerable<SimSpawner> roots)
    {
        foreach (SimSpawner s in roots)
        {
            yield return s;
            if (s is SimObjectSpawner { SubSpawners: { } subs })
                foreach (SimSpawner sub in AllSpawners(subs)) yield return sub;
        }
    }

    /// <summary>Accepts the first offered mission of the given kind, like CommMissionsTab.MPAcceptMissionInput does.</summary>
    static void TestAccept(string arg)
    {
        var sim = RequireSim();
        string metatype = Metatypes.GetValueOrDefault(arg) ?? throw new InvalidOperationException($"unknown mission kind '{arg}'");
        var id = new ID<MissionMetatype>(metatype);
        foreach (MetaSpaceStation station in SpaceStation.GetStations(sim, onlyOperating: false))
        {
            if (station.GetProvidedMissions() is not { } list) continue;
            if (list.FirstOrDefault(m => m.MetatypeID.Equals(id)) is not { } mission) continue;
            list.RemoveMission(mission);
            RequireRun().Mode.Missions.AddMission(mission, track: true);
            Log.Info($"AUTOTEST accepted {metatype}");
            return;
        }
        throw new InvalidOperationException($"no {metatype} offer in this system");
    }

    /// <summary>The objective of the given type in the latest accepted mission that has one.</summary>
    static T? FindObjective<T>() where T : Cosmoteer.Modes.Career.Missions.Objectives.Objective
    {
        var found = new List<T>();
        foreach (Mission m in RequireRun().Mode.Missions)
            foreach (var o in m.Objectives) o.GetObjectives(found);
        return found.LastOrDefault();
    }

    static T RequireObjective<T>() where T : Cosmoteer.Modes.Career.Missions.Objectives.Objective =>
        FindObjective<T>() ?? throw new InvalidOperationException($"no accepted mission has a {typeof(T).Name}");

    static EscortObjective RequireEscort() => RequireObjective<EscortObjective>();
    static ShadyTrapObjective RequireTrap() => RequireObjective<ShadyTrapObjective>();

    /// <summary>Logs the escort state: stasis, position, speed, command, AI origin and target, distance to the destination.</summary>
    static void LogEscort(string label)
    {
        var o = RequireEscort();
        var sim = RequireSim();
        Ship? ship = o.Escort.TargetShip;
        string player = string.Join(" ", sim.Ships.HumanPlayerSignificantShips.Select(s => $"{s.DetWorldCenter}"));
        if (ship is null)
        {
            Log.Info($"AUTOTEST ESCORT {label}: not live; location={o.Escort.Location} spawner={o.Escort.TargetSpawner?.GetType().Name} dest={o.Destination} players={player} paused={sim.IsPaused.Confirmed} clockPaused={sim.Clock.Paused} scale={sim.Clock.ScaleFactor} t={sim.LogicalTime} runSeconds={RequireRun().State.GameSeconds:0.0}");
            return;
        }
        var ai = ship.AI;
        Log.Info($"AUTOTEST ESCORT {label}: live name={ship.Metadata.PlainShipName} pos={ship.DetWorldCenter} vel={ship.Physics.Body?.LinearVelocity} " +
                 $"cmd={ship.Commands.CurConfirmedCommand?.GetType().Name} canExec={ship.Commands.CanExecuteCommands} " +
                 $"ai={ai?.Rules.ID} origin={ai?.PreferredOrigin} unacq={ai?.Parameters?.TargetUnacquireRangeFromOrigin} " +
                 $"modules={string.Join(",", ai?.StrategyModules.Select(m => m.GetType().Name) ?? [])} crew={ship.Crew.TotalMoneyValue} " +
                 $"dest={o.Destination} dist={ship.DetWorldCenter.DistanceTo(o.Destination):0} player={ship.Metadata.PlayerIndex} players={player} paused={sim.IsPaused.Confirmed} clockPaused={sim.Clock.Paused} scale={sim.Clock.ScaleFactor} t={sim.LogicalTime} runSeconds={RequireRun().State.GameSeconds:0.0}");
    }

    static void TeleportPlayers(Vector2 near)
    {
        int i = 0;
        foreach (Ship s in RequireSim().Ships.HumanPlayerSignificantShips.ToList())
            s.WorldCenter = near + new Vector2(400 + 250 * i++, 0);
        Autotest.Wait(3);
    }

    /// <summary>Moves the live escort and the players to the destination. Run hc_escort_goto first, so the escort is out of stasis.</summary>
    static void TestEscortArrive()
    {
        var o = RequireEscort();
        Ship escort = o.Escort.TargetShip ?? throw new InvalidOperationException("escort is not live; run hc_escort_goto and wait first");
        escort.WorldCenter = o.Destination;
        TeleportPlayers(o.Destination);
    }

    static void TestShadySpring()
    {
        var o = RequireTrap();
        if (o.State != ShadyTrapState.Waiting) throw new InvalidOperationException($"trap is {o.State}, not Waiting");
        var sim = RequireSim();
        o.Spring(sim, sim.Ships.HumanPlayerSignificantShips.First().DetWorldCenter);
        Autotest.Wait(2);
    }

    static void TestKillAmbush()
    {
        var sim = RequireSim();
        foreach (var a in RequireTrap().Ambushers)
            if (a.TargetShip is { } ship) sim.Ships.Remove(ship, dispose: true, handleLostShip: false);
        Autotest.Wait(2);
    }

    /// <summary>Player indexes and locations of live and stasis ships with the tag.</summary>
    static List<(int PlayerIndex, Vector2 Location)> TaggedShips(SimRoot sim, string tagName)
    {
        var tag = new ID<SimObjectSpawner>(tagName);
        var result = new List<(int, Vector2)>();
        for (int i = 0; i < sim.Ships.Count; i++)
            if (sim.Ships[i].Tags?.Contains(tag) == true) result.Add((sim.Ships[i].Metadata.PlayerIndex, sim.Ships[i].DetWorldCenter));
        foreach (StasisSpawner s in sim.Stasis)
            if (StasisTags(s)?.Contains(tag) == true) result.Add((s.ObjectAllegiance, s.Location));
        return result;
    }

    /// <summary>Spawn tags of a ship in stasis. Ships spawned into stasis and ships that entered stasis use different spawner types.</summary>
    internal static HashSet<ID<SimObjectSpawner>>? StasisTags(StasisSpawner s) => s switch
    {
        StasisShipSpawner ss => ss.Tags,
        SimStasisManager.SerializedStasisShip ser => ser.Tags,
        _ => null,
    };

    static int EnemiesOfPlayer(string tag)
    {
        var sim = RequireSim();
        var humans = sim.RequireGame().HumanPlayersIndexes;
        return TaggedShips(sim, tag).Count(t => humans.Any(h => sim.Mode.AreEnemies(t.PlayerIndex, h)));
    }

    static int EnemiesOf(string tag, string targetTag)
    {
        var sim = RequireSim();
        var targets = TaggedShips(sim, targetTag);
        if (targets.Count == 0) throw new InvalidOperationException($"no ships tagged {targetTag}");
        return TaggedShips(sim, tag).Count(t => targets.All(x => sim.Mode.AreEnemies(t.PlayerIndex, x.PlayerIndex)));
    }
}

internal static class SimExtensions
{
    /// <summary>Career sims always have a game and a map node. A missing one is a bug, so it throws.</summary>
    public static GameRoot RequireGame(this SimRoot sim) => sim.Game ?? throw new InvalidOperationException("sim has no game");
    public static ObjectID NodeId(this SimRoot sim) => (sim.MapNode ?? throw new InvalidOperationException("sim has no map node")).UniqueID;
}

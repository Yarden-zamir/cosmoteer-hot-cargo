using System.Globalization;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Resources;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Missions.Mechanics;

/// <summary>
/// Entry point of the mechanics missions (spec 130 to 139). Mission content is rules in mod/missions/mechanics;
/// the C# part is the objective types, the spawn chances from the tunables and the autotest commands.
/// </summary>
public static class MechanicsArea
{
    /// <summary>One mission kind: the autotest name, the metatype, the spawner group tag and its spawn chance tunable.</summary>
    public sealed record Kind(string Name, string Metatype, Func<MechanicsTunables, float> Chance)
    {
        public string GroupTag => $"hc_mech_{Name}";
    }

    public static readonly Kind[] Kinds =
    [
        new("siege", "HotCargoDroneSiege", t => t.DroneSiegeChance),
        new("prototype", "HotCargoPrototype", t => t.PrototypeChance),
        new("sunmining", "HotCargoSunMining", t => t.SunMiningChance),
        new("tow", "HotCargoSalvageRights", t => t.TowChance),
        new("capture", "HotCargoCapture", t => t.CaptureChance),
        new("survey", "HotCargoSurvey", t => t.SurveyChance),
        new("guard", "HotCargoClaimJumpers", t => t.MiningGuardChance),
        new("listening", "HotCargoListeningPost", t => t.ListeningPostChance),
    ];

    static readonly ID<ResourceRules> OreId = new("yarden_zamir.helion_ore");
    static readonly ID<SimObjectSpawner> SunOreTag = new("hc_sun_ore");
    static readonly ID<SimObjectSpawner> SunTag = new("sun");

    public static void Register()
    {
        foreach (var kind in Kinds)
        {
            var id = new ID<MissionMetatype>(kind.Metatype);
            Autotest.Fields[$"mech_offers_{kind.Name}"] = () => MissionArea.Offers(RequireSim()).Count(m => m.MetatypeID.Equals(id));
            Autotest.Fields[$"mech_active_{kind.Name}"] = () => RequireRun().Mode.Missions.Count(m => m.MetatypeID.Equals(id) && m.Completion == CompletionState.InProgress);
            // Completion of the last accepted mission of the kind: 0 in progress, 1 completed, 2 failed. The game drops failed missions from the log,
            // so the test keeps the mission object. A mission waits in progress for its rewards after the main goal completes.
            Autotest.Fields[$"mech_state_{kind.Name}"] = () => (int)Accepted(kind).Completion;
            Autotest.Fields[$"mech_goal_{kind.Name}"] = () => (int)Goal(Accepted(kind)).Completion;
        }
        Autotest.Commands["mech_generate"] = TestGenerate;
        // mech_set <MechanicsTunables field> <value>: changes a live tunable for this run (spawn chances apply from the next system). Test only.
        Autotest.Commands["mech_set"] = arg => Autotest.SetField(RequireRun().T.Mechanics, arg);
        // Placement census (spec 105 smaller systems): asteroid fields that Claim Jumpers can use, mission objects outside the
        // gate ring, and a log line per kind with its offers and object distances.
        Autotest.Fields["mech_asteroid_fields"] = () => Tagged(MissionArea.ExistingSpawnTags(RequireSim()), "asteroid_field_poi").Count;
        Autotest.Fields["mech_beyond_gates"] = () => Census(log: false);
        Autotest.Commands["mech_census"] = _ => Census(log: true);
        Autotest.Commands["mech_accept"] = arg => _accepted.Add((Find(arg), MissionArea.AcceptOffer(Find(arg).Metatype)));
        Autotest.Commands["mech_expire"] = TestExpire;
        Autotest.Fields["mech_chance_siege"] = () => MissionArea.FindGroup(RequireSim(), Find("siege").GroupTag).SpawnChance;

        // Hold-outs (spec 132, 137).
        Autotest.Commands["mech_hold_goto"] = _ => MissionArea.TeleportPlayers(Hold().Target.Location ?? throw new InvalidOperationException("hold-out target is not in this system"));
        Autotest.Commands["mech_hold_skip"] = arg => Hold().GuardedSeconds += double.Parse(arg, CultureInfo.InvariantCulture);
        Autotest.Commands["mech_hold_kill"] = _ => RemoveShip(Hold().Target.TargetShip ?? throw new InvalidOperationException("hold-out target is not live"));
        Autotest.Commands["mech_order"] = arg =>
        {
            var o = Hold();
            o.Order(RequireSim(), Enum.Parse<HoldOutOrder>(arg, ignoreCase: true), RequireRun().Game.LocalPlayerIndex);
            Autotest.Wait(1);
        };
        Autotest.Commands["mech_scan_range"] = _ =>
        {
            var o = Hold();
            Vector2 b = o.BaseLocation ?? throw new InvalidOperationException("hold-out has no base");
            Ship scout = o.Target.TargetShip ?? throw new InvalidOperationException("scout is not live; run mech_hold_goto and wait");
            Vector2 away = scout.DetWorldCenter - b;
            Vector2 at = b + away / away.Length * (TunablesFor(RequireRun().Game).ScanRange - 500f);
            Autotest.Teleport(scout, at);
            MissionArea.TeleportPlayers(at);
        };
        Autotest.Fields["mech_hold_phase"] = () => (int)Hold().Phase;
        Autotest.Fields["mech_hold_pay"] = () => Hold().PayFactor;
        Autotest.Fields["mech_hold_earned"] = () => Hold().EarnedFraction(RequireRun().Game);
        Autotest.Fields["mech_hold_settled"] = () => Hold().Settled ?? -1f;
        Autotest.Fields["mech_reward_money"] = () => Hold().Mission?.Rewards.OfType<Cosmoteer.Modes.Career.Missions.Rewards.MoneyReward>().Sum(m => m.Money) ?? -1;
        Autotest.Fields["mech_hold_seconds"] = () => Hold().GuardedSeconds;
        Autotest.Fields["mech_hold_guarded"] = () => Hold().Guarded ? 1 : 0;
        Autotest.Fields["mech_hold_tier"] = () => Hold().Tier;
        Autotest.Fields["mech_wave_ships"] = () => Hold() is var o && o.WaveAllegiance != 0
            ? SimQueries.TaggedShips(RequireSim(), o.WaveTag).Count(s => s.PlayerIndex == o.WaveAllegiance) : 0;
        Autotest.Fields["mech_waves_vs_player"] = () =>
        {
            var o = Hold();
            var sim = RequireSim();
            return SimQueries.TaggedShips(sim, o.WaveTag).Count(s => sim.RequireGame().HumanPlayersIndexes.All(h => sim.Mode.AreEnemies(s.PlayerIndex, h)));
        };

        // Prototype (spec 133).
        Autotest.Commands["mech_proto_goto"] = _ => MissionArea.TeleportPlayers(Proto().Holder.Location(RequireSim()) ?? throw new InvalidOperationException("carrier is not in this system"));
        Autotest.Commands["mech_proto_break"] = arg => { var core = Core(out var holder); core.OnHit(core.Health); };
        // mech_proto_wreck: moves the players out of recover range of the core, then wrecks the carrier. So a wreck alone does not
        // complete the objective: the players must come alongside again (mech_proto_near). Without the move, the result depends on
        // whether the carrier's AI flew away from the players in the seconds before.
        Autotest.Commands["mech_proto_wreck"] = arg =>
        {
            var core = Core(out var holder);
            var sim = RequireSim();
            var ship = SimQueries.FirstHumanShip(sim) ?? throw new InvalidOperationException("no human ship");
            MissionArea.TeleportPlayers(Autotest.ClearSpotNear(sim, holder.DetTransformPointToWorld(core.LocalCenter), 3000f, ship.DetWorldBoundingCircle.Radius));
            Wreck(holder);
        };
        Autotest.Commands["mech_proto_surrender"] = arg => { Core(out var holder); ((CareerSimModeManager)RequireSim().Mode).SurrenderedAIs.Add(holder.UniqueID); Autotest.Wait(1); };
        Autotest.Commands["mech_proto_near"] = _ => MissionArea.TeleportPlayers(Proto().LastCoreLocation ?? throw new InvalidOperationException("core is not installed"));
        Autotest.Fields["mech_proto_core"] = () => Proto() is { CoreCell: not null } && TryCore() is not null ? 1 : 0;

        // Tow (spec 134).
        Autotest.Commands["mech_tow_goto"] = _ => MissionArea.TeleportPlayers(Tow().Hulk.Location(RequireSim()) ?? throw new InvalidOperationException("hulk is not in this system"));
        Autotest.Commands["mech_tow_deliver"] = _ => TestTowDeliver();
        Autotest.Commands["mech_tow_remove"] = _ => RemoveShip(Tow().Hulk.Live(RequireSim()) ?? throw new InvalidOperationException("hulk is not live"));
        Autotest.Fields["mech_tow_junk"] = () => Tow().Hulk.Live(RequireSim()) is { IsJunk: true } ? 1 : 0;

        // Capture (spec 135).
        Autotest.Commands["mech_capture_goto"] = _ => MissionArea.TeleportPlayers(Capture().Target.Location(RequireSim()) ?? throw new InvalidOperationException("capture target is not in this system"));
        Autotest.Commands["mech_capture_surrender"] = _ => ((CareerSimModeManager)RequireSim().Mode).SurrenderedAIs.Add(Capture().Target.ShipId);
        Autotest.Commands["mech_capture_claim"] = _ => TestClaim();
        Autotest.Commands["mech_capture_remove"] = _ => RemoveShip(Capture().Target.Live(RequireSim()) ?? throw new InvalidOperationException("capture target is not live"));
        Autotest.Fields["mech_capture_hostile"] = () => Capture().Target.Live(RequireSim()) is { } s && RequireSim().Mode.AreEnemies(s.Metadata.PlayerIndex, RequireRun().Game.HumanPlayersIndexes[0]) ? 1 : 0;

        // Survey (spec 136).
        Autotest.Commands["mech_survey_next"] = _ => MissionArea.TeleportPlayers(Survey().Markers.First(m => m.Seconds < TunablesFor(RequireRun().Game).SurveyScanSeconds).Location);
        Autotest.Fields["mech_survey_done"] = () => Survey().Done(RequireRun().Game);
        Autotest.Fields["mech_survey_markers"] = () => Survey().Markers.Count;
        Autotest.Fields["mech_survey_progress"] = () => Survey().Markers.Sum(m => m.Seconds);
        Autotest.Fields["mech_sensor"] = () => SimQueries.FirstHumanShip(RequireSim()) is { } s && s.Parts.Any(p => p.Components.Any(c => c is Cosmoteer.Ships.Parts.Sensors.Sensor)) ? 1 : 0;

        // Sun mining (spec 138): helion ore deposits on live asteroids, and their distance to the nearest sun.
        Autotest.Commands["mech_sun_goto"] = _ => MissionArea.TeleportPlayers(SunOreShips().FirstOrDefault()?.DetWorldCenter ?? SunOreStasis().First());
        Autotest.Commands["mech_deliver_goto"] = _ => MissionArea.TeleportPlayers(Deliver().ToShip.Location ?? throw new InvalidOperationException("delivery station is not in this system"));
        Autotest.Commands["mech_deliver"] = arg => TestDeliver(int.Parse(arg, CultureInfo.InvariantCulture));
        Autotest.Fields["mech_sun_ore_asteroids"] = () => SunOreShips().Count() + SunOreStasis().Count();
        Autotest.Fields["mech_sun_ore_units"] = () => SunOreShips().Sum(s => s.Parts.Where(p => p.Rules.Resources.Any(r => r.Type == OreId)).Sum(p => p.Rules.Resources.First(r => r.Type == OreId).Quantity));
        Autotest.Fields["mech_sun_ore_maxdist"] = () => SunOreShips().Select(s => s.DetWorldCenter).Concat(SunOreStasis()).Max(SunDistance);
        Autotest.Fields["mech_sun_ore_mindist"] = () => SunOreShips().Select(s => s.DetWorldCenter).Concat(SunOreStasis()).Min(SunDistance);
        Autotest.Fields["mech_deliver_count"] = () => Deliver().Count;
        Autotest.Fields["mech_deliver_target"] = () => Deliver().TargetCount;
    }

    /// <summary>
    /// Mechanics tunables for code that runs in a game. Without an active run (the first system of a new career generates before
    /// the run starts) it uses the defaults: in co-op each machine can have a different tunables file, and a file value here would desync
    /// system generation. Limit: a tuned spawn chance does not apply to the first system. Revisit if Core shares the host's tunables before the run starts.
    /// </summary>
    internal static MechanicsTunables TunablesFor(GameRoot? game) =>
        Run.Current is { } run && run.Game == game ? run.T.Mechanics : Defaults;

    static readonly MechanicsTunables Defaults = new();

    /// <summary>
    /// Spec 131: sets the spawn chance of each mechanics group from the tunables before a system generates.
    /// The groups are rules objects shared by all systems of a sector type, so the value applies to every new system.
    /// </summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.InitSimRoot))]
    static class ChancePatch
    {
        static void Prefix(CareerSimModeManager __instance) => Errors.Guard("mechanics spawn chances", () =>
        {
            if (!Activation.IsModEnabled()) return;
            var t = TunablesFor(__instance.Sim.Game);
            var groups = MissionArea.AllSpawners(__instance.SectorType.SystemGenerator.Spawners).OfType<SimObjectSpawner>().ToList();
            foreach (var kind in Kinds)
            {
                var tag = new ID<SimObjectSpawner>(kind.GroupTag);
                var group = groups.FirstOrDefault(g => g.Tags?.Contains(tag) == true)
                    ?? throw new InvalidOperationException($"spawner group {kind.GroupTag} is missing in sector {__instance.SectorType.ID}");
                group.SpawnChance = kind.Chance(t);
            }
        });
    }

    static List<Vector2> Tagged(SpawnedObjectCollection objects, string tag) =>
        objects.TryGetValue(new ID<SimObjectSpawner>(tag), out var list) ? [.. list.Select(so => so.Location.Center)] : [];

    /// <summary>
    /// Counts the mechanics mission objects (objects with an hc_ tag of a mechanics group) that lie farther from the system center
    /// than the farthest FTL gate. With log, it also writes one line per kind: offers, and each object's distance from the center and the nearest gate.
    /// </summary>
    static int Census(bool log)
    {
        var sim = RequireSim();
        var objects = MissionArea.ExistingSpawnTags(sim);
        var gates = Tagged(objects, "ftl_gate");
        if (gates.Count == 0) throw new InvalidOperationException("no FTL gate in this system");
        float gateRing = gates.Max(g => g.Length);
        int beyond = 0;
        foreach (var kind in Kinds)
        {
            var group = MissionArea.FindGroup(sim, kind.GroupTag);
            var tags = MissionArea.AllSpawners([group]).OfType<SimObjectSpawner>().SelectMany(sp => sp.Tags ?? [])
                .Select(t => t.ToString()).Where(t => t.StartsWith("hc_", StringComparison.Ordinal) && t != kind.GroupTag).Distinct().Order(StringComparer.Ordinal);
            var parts = new List<string>();
            foreach (string tag in tags)
            {
                var at = Tagged(objects, tag);
                beyond += at.Count(p => p.Length > gateRing);
                if (at.Count > 0) parts.Add($"{tag}=[{string.Join(" ", at.Select(p => $"{p.Length:0}/{gates.Min(g => g.DistanceTo(p)):0}"))}]");
            }
            if (log)
            {
                var id = new ID<MissionMetatype>(kind.Metatype);
                Log.Info($"AUTOTEST CENSUS {RequireRun().Mode.CurrentSectorType.ID} {kind.Name}: offers={MissionArea.Offers(sim).Count(m => m.MetatypeID.Equals(id))} "
                    + $"objects (center/gate): {string.Join(" ", parts)}");
            }
        }
        if (log) Log.Info($"AUTOTEST CENSUS gates at {string.Join(" ", gates.Select(g => $"{g.Length:0}"))}; asteroid fields={Tagged(objects, "asteroid_field_poi").Count}; military stations={Tagged(objects, "military_station").Count}; mission objects past the farthest gate={beyond}");
        return beyond;
    }

    static Kind Find(string name) => Kinds.FirstOrDefault(k => k.Name == name) ?? throw new InvalidOperationException($"unknown mechanics mission '{name}'");

    static Run RequireRun() => Autotest.RequireRun();
    static SimRoot RequireSim() => RequireRun().Game.Sim ?? throw new InvalidOperationException("no sim");

    /// <summary>Missions that mech_accept accepted, in order. Test state only.</summary>
    static readonly List<(Kind Kind, Mission Mission)> _accepted = [];

    static Mission Accepted(Kind kind) =>
        _accepted.LastOrDefault(a => a.Kind == kind).Mission ?? throw new InvalidOperationException($"no {kind.Name} mission accepted in this test");

    /// <summary>The objective that holds the mission's real work (not the Sequential, TimeLimit or ReceiveRewards around it).</summary>
    static Objective Goal(Mission m)
    {
        var all = new List<Objective>();
        foreach (var o in m.Objectives) o.GetObjectives(all);
        return all.FirstOrDefault(o => o is HoldOutObjective or PrototypeObjective or TowObjective or CaptureObjective or SurveyObjective or DeliverResourcesObjective)
            ?? throw new InvalidOperationException($"{m.MetatypeID} has no mechanics objective");
    }

    /// <summary>
    /// The objective of the type in the latest mission in the log that has one (this also works after a load), else in the last mission
    /// that mech_accept accepted (the game drops failed missions from the log).
    /// </summary>
    static T Require<T>() where T : Objective
    {
        if (MissionArea.FindObjective<T>() is { } inLog) return inLog;
        for (int i = _accepted.Count - 1; i >= 0; i--)
        {
            var found = new List<T>();
            foreach (var o in _accepted[i].Mission.Objectives) o.GetObjectives(found);
            if (found.Count > 0) return found[0];
        }
        throw new InvalidOperationException($"no accepted mission has a {typeof(T).Name}");
    }

    static HoldOutObjective Hold() => Require<HoldOutObjective>();
    static PrototypeObjective Proto() => Require<PrototypeObjective>();
    static TowObjective Tow() => Require<TowObjective>();
    static CaptureObjective Capture() => Require<CaptureObjective>();
    static SurveyObjective Survey() => Require<SurveyObjective>();
    static DeliverResourcesObjective Deliver() => Require<DeliverResourcesObjective>();

    /// <summary>
    /// Runs one mechanics group with SpawnChance 1. Arg: kind name, then "nonebula" to lift the in-nebula rules of the group for this one run
    /// (the start system has no ion storm, and its gas cloud may not reach a free spot).
    /// </summary>
    static void TestGenerate(string arg)
    {
        var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kind = Find(parts.Length > 0 ? parts[0] : "");
        var sim = RequireSim();
        var lifted = new List<(SimObjectSpawner Spawner, List<(ID<Cosmoteer.Nebulas.NebulaTypeRules>, float)>? Old)>();
        if (parts.Contains("nonebula"))
            foreach (var s in MissionArea.AllSpawners([MissionArea.FindGroup(sim, kind.GroupTag)]).OfType<SimObjectSpawner>().Where(s => s.MaxDistanceFromNebulas is not null))
            {
                lifted.Add((s, s.MaxDistanceFromNebulas));
                s.MaxDistanceFromNebulas = null;
            }
        try { MissionArea.GenerateGroups(sim, [kind.GroupTag]); }
        finally { foreach (var (s, old) in lifted) s.MaxDistanceFromNebulas = old; }
    }

    /// <summary>Moves the start of the time limit of the latest mission of the kind into the past, so it expires on the next tick.</summary>
    static void TestExpire(string arg)
    {
        var mission = Accepted(Find(arg));
        var limits = new List<TimeLimitObjective>();
        foreach (var o in mission.Objectives) o.GetObjectives(limits);
        var limit = limits.FirstOrDefault() ?? throw new InvalidOperationException($"{arg} has no time limit");
        limit.StartTime = RequireSim().LogicalTime - limit.Duration - 1.0;
        Autotest.Wait(2);
    }

    static void RemoveShip(Ship ship)
    {
        RequireSim().Ships.Remove(ship, dispose: true, handleLostShip: false);
        Autotest.Wait(2);
    }

    /// <summary>Makes a ship a wreck the way the game does when its crew or key parts are gone.</summary>
    static void Wreck(Ship ship) => RequireSim().Mode.SetReducedShipAllegiance(ship, -3, -1);

    static Part? TryCore()
    {
        var o = Proto();
        return o.Holder.Live(RequireSim()) is { } holder && o.CoreCell is { } cell && holder.Parts[cell] is { } p && p.Rules.ID == PrototypeObjective.CoreId ? p : null;
    }

    static Part Core(out Ship holder)
    {
        holder = Proto().Holder.Live(RequireSim()) ?? throw new InvalidOperationException("carrier is not live; run mech_proto_goto and wait");
        return TryCore() ?? throw new InvalidOperationException("the core is not on the carrier");
    }

    /// <summary>
    /// Puts the live hulk next to the station, then the players there. The station may be in stasis, so the spot uses only its center:
    /// 700 past the hulk's own radius is inside DeliveryRadius for any station radius.
    /// </summary>
    static void TestTowDeliver()
    {
        var o = Tow();
        Ship hulk = o.Hulk.Live(RequireSim()) ?? throw new InvalidOperationException("hulk is not live; run mech_tow_goto and wait");
        Vector2 station = o.Station.Location ?? throw new InvalidOperationException("station is not in this system");
        Vector2 at = station + new Vector2(hulk.DetWorldBoundingCircle.Radius + 700f, 0f);
        Autotest.Teleport(hulk, at);
        MissionArea.TeleportPlayers(at + new Vector2(0f, 800f));
    }

    /// <summary>The capture path through crew: the target becomes a wreck, then the first player's crew claim it.</summary>
    static void TestClaim()
    {
        var sim = RequireSim();
        Ship ship = Capture().Target.Live(sim) ?? throw new InvalidOperationException("capture target is not live");
        Wreck(ship);
        ship.Metadata.PlayerIndex = RequireRun().Game.HumanPlayersIndexes[0];
        Autotest.Wait(2);
    }

    /// <summary>Raises the trade delivery event of a sale of helion ore to the delivery station, as the crew do at the end of a sale.</summary>
    static void TestDeliver(int amount)
    {
        var o = Deliver();
        var sim = RequireSim();
        Ship station = o.ToShip.TargetShip ?? throw new InvalidOperationException("delivery station is not live");
        Ship player = SimQueries.FirstHumanShip(sim) ?? throw new InvalidOperationException("no player ship");
        var crew = player.Crew.Assigned.FirstOrDefault() ?? throw new InvalidOperationException("player ship has no crew");
        RequireRun().Game.EventHub.OnResourceTradeDelivery(TradeType.Sell, OreId, amount, crew, player, station);
        Autotest.Wait(1);
    }

    static IEnumerable<Ship> SunOreShips()
    {
        var sim = RequireSim();
        for (int i = 0; i < sim.Ships.Count; i++)
            if (sim.Ships[i].HasTag(SunOreTag)) yield return sim.Ships[i];
    }

    static IEnumerable<Vector2> SunOreStasis() =>
        RequireSim().Stasis.Where(s => s is Cosmoteer.Simulation.Stasis.StasisDoodadSpawner d && d.Tags?.Contains(SunOreTag) == true).Select(s => s.Location);

    static float SunDistance(Vector2 at) =>
        RequireSim().Doodads.Where(d => d.Tags?.Contains(SunTag) == true).Select(d => d.Location.DistanceTo(at)).DefaultIfEmpty(float.NaN).Min();
}

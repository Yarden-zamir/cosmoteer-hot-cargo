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
        new("tow", "HotCargoTow", t => t.TowChance),
        new("capture", "HotCargoCapture", t => t.CaptureChance),
        new("survey", "HotCargoSurvey", t => t.SurveyChance),
        new("guard", "HotCargoClaimJumpers", t => t.MiningGuardChance),
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
            Autotest.Fields[$"mech_active_{kind.Name}"] = () => Missions(kind).Count(m => m.Completion == CompletionState.InProgress);
            Autotest.Fields[$"mech_completed_{kind.Name}"] = () => Missions(kind).Count(m => m.Completion == CompletionState.Completed);
            Autotest.Fields[$"mech_failed_{kind.Name}"] = () => Missions(kind).Count(m => m.Completion == CompletionState.Failed);
        }
        Autotest.Commands["mech_generate"] = TestGenerate;
        Autotest.Commands["mech_accept"] = arg => MissionArea.AcceptOffer(Find(arg).Metatype);
        Autotest.Commands["mech_expire"] = TestExpire;
        Autotest.Fields["mech_chance_siege"] = () => MissionArea.FindGroup(RequireSim(), Find("siege").GroupTag).SpawnChance;

        // Hold-outs (spec 132, 137).
        Autotest.Commands["mech_hold_goto"] = _ => MissionArea.TeleportPlayers(Hold().Target.Location ?? throw new InvalidOperationException("hold-out target is not in this system"));
        Autotest.Commands["mech_hold_skip"] = arg => Hold().GuardedSeconds += double.Parse(arg, CultureInfo.InvariantCulture);
        Autotest.Commands["mech_hold_kill"] = _ => RemoveShip(Hold().Target.TargetShip ?? throw new InvalidOperationException("hold-out target is not live"));
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
        Autotest.Commands["mech_proto_wreck"] = arg => { Core(out var holder); Wreck(holder); };
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
        Autotest.Commands["mech_deliver"] = arg => TestDeliver(int.Parse(arg, CultureInfo.InvariantCulture));
        Autotest.Fields["mech_sun_ore_asteroids"] = () => SunOreShips().Count() + SunOreStasis().Count();
        Autotest.Fields["mech_sun_ore_units"] = () => SunOreShips().Sum(s => s.Parts.Where(p => p.Rules.Resources.Any(r => r.Type == OreId)).Sum(p => p.Rules.Resources.First(r => r.Type == OreId).Quantity));
        Autotest.Fields["mech_sun_ore_maxdist"] = () => SunOreShips().Select(s => s.DetWorldCenter).Concat(SunOreStasis()).Max(SunDistance);
        Autotest.Fields["mech_sun_ore_mindist"] = () => SunOreShips().Select(s => s.DetWorldCenter).Concat(SunOreStasis()).Min(SunDistance);
        Autotest.Fields["mech_deliver_count"] = () => Deliver().Count;
        Autotest.Fields["mech_deliver_target"] = () => Deliver().TargetCount;
    }

    /// <summary>
    /// Mechanics tunables for code that runs in a game. The first system of a new career generates before the run starts,
    /// so it reads the tunables file like Run.Start does.
    /// </summary>
    internal static MechanicsTunables TunablesFor(GameRoot? game) =>
        Run.Current is { } run && run.Game == game ? run.T.Mechanics : (TunablesFile.Read()?.Values ?? new Tunables()).Mechanics;

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

    static Kind Find(string name) => Kinds.FirstOrDefault(k => k.Name == name) ?? throw new InvalidOperationException($"unknown mechanics mission '{name}'");

    static Run RequireRun() => Autotest.RequireRun();
    static SimRoot RequireSim() => RequireRun().Game.Sim ?? throw new InvalidOperationException("no sim");

    static IEnumerable<Mission> Missions(Kind kind)
    {
        var id = new ID<MissionMetatype>(kind.Metatype);
        return RequireRun().Mode.Missions.Where(m => m.MetatypeID.Equals(id));
    }

    static T Require<T>() where T : Objective =>
        MissionArea.FindObjective<T>() ?? throw new InvalidOperationException($"no accepted mission has a {typeof(T).Name}");

    static HoldOutObjective Hold() => Require<HoldOutObjective>();
    static PrototypeObjective Proto() => Require<PrototypeObjective>();
    static TowObjective Tow() => Require<TowObjective>();
    static CaptureObjective Capture() => Require<CaptureObjective>();
    static SurveyObjective Survey() => Require<SurveyObjective>();
    static DeliverResourcesObjective Deliver() => Require<DeliverResourcesObjective>();

    /// <summary>
    /// Runs one mechanics group with SpawnChance 1. Arg: kind name, then "nostorm" for the survey: the start system has no ion storm,
    /// so the test lifts the in-storm rule of the markers for this one run.
    /// </summary>
    static void TestGenerate(string arg)
    {
        var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kind = Find(parts.Length > 0 ? parts[0] : "");
        var sim = RequireSim();
        var lifted = new List<(SimObjectSpawner Spawner, List<(ID<Cosmoteer.Nebulas.NebulaTypeRules>, float)>? Old)>();
        if (parts.Contains("nostorm"))
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
        var mission = Missions(Find(arg)).LastOrDefault() ?? throw new InvalidOperationException($"no accepted {arg} mission");
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

    static void TestTowDeliver()
    {
        var o = Tow();
        Ship hulk = o.Hulk.Live(RequireSim()) ?? throw new InvalidOperationException("hulk is not live; run mech_tow_goto and wait");
        Ship station = o.Station.TargetShip ?? throw new InvalidOperationException("station is not live");
        Circle s = station.DetWorldBoundingCircle;
        Vector2 at = s.Center + new Vector2(s.Radius + hulk.DetWorldBoundingCircle.Radius + 200f, 0f);
        Autotest.Teleport(hulk, at);
        MissionArea.TeleportPlayers(at + new Vector2(0f, 600f));
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

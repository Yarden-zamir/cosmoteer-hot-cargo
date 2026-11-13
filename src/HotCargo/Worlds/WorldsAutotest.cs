using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Map;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using Cosmoteer.Simulation.Stasis;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Missions;

namespace HotCargo.Worlds;

/// <summary>Autotest commands and fields of the Worlds area. Test only, single player: they move ships from a GUI frame.</summary>
static class WorldsAutotest
{
    /// <summary>Worlds missions by the short name that the commands use. The group tag is hc_group_&lt;name&gt;.</summary>
    static readonly Dictionary<string, string> Metatypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pass"] = "HotCargoHoldPass",
        ["sundive"] = "HotCargoSundive",
        ["lair"] = "HotCargoLairRaid",
    };

    static readonly ID<SimObjectSpawner> BeltRock = new("hc_belt_rock"), Pass = new("hc_pass"), Lair = new("hc_lair");

    static float _markedHeat, _keptGain;
    /// <summary>The mission that "worlds accept" accepted last. Objective fields read it.</summary>
    static Mission? _accepted;

    internal static void Register()
    {
        Autotest.Commands["worlds"] = Command;
        Autotest.Fields["worlds_current_tris"] = () => Current.TriangleCenters(Sim(), Current.NebulaId).Count;
        // Strength of the current at the ship's location (0 to 1), and as the ship's nebula tracker sees it (what the push uses).
        Autotest.Fields["worlds_in_current"] = () => Sim().Nebulas.GetInsideStrength(Ship().DetWorldCenter, Current.NebulaId);
        Autotest.Fields["worlds_ship_in_current"] = () => Ship().Nebulas.GetNebulaStrength(Current.NebulaId);
        Autotest.Fields["worlds_flow_speed"] = () => ((Vector2)(Ship().Physics.Body ?? throw new InvalidOperationException("ship has no body")).LinearVelocity).Dot(Current.FlowVector(Sim()));
        Autotest.Fields["worlds_belt_rocks"] = () => Tagged(Sim(), BeltRock).Count;
        Autotest.Fields["worlds_passes"] = () => Doodads(Sim(), Pass).Count;
        // Belt rocks within 75 % of a pass ring's radius from its center. The belt spawner keeps rocks out of the passes, so expect 0.
        Autotest.Fields["worlds_pass_rocks"] = () =>
        {
            var sim = Sim();
            var passes = Doodads(sim, Pass);
            if (passes.Any(p => p.Radius <= 0f)) throw new InvalidOperationException("a pass ring has no radius");
            var near = Tagged(sim, BeltRock).Where(r => passes.Any(p => r.DistanceTo(p.Center) < p.Radius * 0.75f)).ToList();
            Log.Info($"AUTOTEST pass rocks: {near.Count} rocks inside 75 % of {passes.Count} pass rings (radius {passes[0].Radius:0})");
            return near.Count;
        };
        Autotest.Fields["worlds_suns"] = () => HeatZone.Suns(Sim()).Count;
        Autotest.Fields["worlds_heat_depth"] = () => HeatZone.Depth(Sim(), Ship().DetWorldCenter);
        // Heat status (the vanilla sun damage) that the first own ship gained since "worlds mark".
        Autotest.Fields["worlds_heat_gain"] = () => Heat(Ship()) - _markedHeat;
        // Heat gain since "worlds mark" minus the gain that "worlds keep" saved: above 0 when the second exposure burned more.
        Autotest.Fields["worlds_heat_gain_more"] = () => Heat(Ship()) - _markedHeat - _keptGain;
        Autotest.Fields["worlds_lairs"] = () => SimQueries.TaggedShips(Sim(), Lair).Count;
        Autotest.Fields["worlds_hc_sector"] = () => SectorId().StartsWith("hc_", StringComparison.Ordinal) ? 1 : 0;
        Autotest.Fields["worlds_pass_held"] = () => Objective<HoldPassObjective>().HeldSeconds;
        Autotest.Fields["worlds_pass_waves"] = () => Objective<HoldPassObjective>().WavesSpawned;
        // Objective states of the latest accepted mission: 1 completed, -1 failed, 0 in progress. A completed mission still waits
        // for its reward at the station (ReceiveRewards), so these read the Worlds objective, not the mission.
        Autotest.Fields["worlds_pass_done"] = () => State(Objective<HoldPassObjective>());
        Autotest.Fields["worlds_sundive_done"] = () => State(Objective<SundiveObjective>());
        Autotest.Fields["worlds_lair_found"] = () => State(Objective<FindLairObjective>());
        Autotest.Fields["worlds_sundive_salvage"] = () => Objective<SundiveObjective>().SalvageSeconds;
        Autotest.Fields["worlds_sundive_carrier"] = () => Objective<SundiveObjective>().Carrier is null ? 0 : 1;
        // Ring radii of the smaller systems (spec 105), from the system center. The trade station ring lies inside the gate ring,
        // and no station is in a sun's heat zone.
        Autotest.Fields["worlds_gate_min"] = () => Radii(Gate).Min();
        Autotest.Fields["worlds_gate_max"] = () => Radii(Gate).Max();
        Autotest.Fields["worlds_trade_station_max"] = () => Radii(TradeStation).Max();
        Autotest.Fields["worlds_station_heat_gap"] = () =>
        {
            var suns = HeatZone.Suns(Sim());
            if (suns.Count == 0) return double.MaxValue;
            return At(Station).Min(p => suns.Min(sun => p.DistanceTo(sun.Center) - sun.EdgeRadius));
        };
        foreach (var (name, metatype) in Metatypes)
        {
            var id = new ID<MissionMetatype>(metatype);
            Autotest.Fields[$"worlds_offers_{name}"] = () => MissionArea.Offers(Sim()).Count(m => m.MetatypeID.Equals(id));
            // The mission that "worlds accept" accepted last, if it is of this kind: 1 completed, -1 failed, 0 in progress, -2 none.
            Autotest.Fields[$"worlds_state_{name}"] = () => _accepted is { } m && m.MetatypeID.Equals(id) ? State(m.Completion) : -2;
        }
    }

    static void Command(string arg)
    {
        var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts)
        {
            // set <Field> <number|on|off>: one WorldsTunables field for this run.
            case ["set", var name, var value]:
                Autotest.SetField(Autotest.RequireRun().T.Worlds, $"{name} {value}");
                break;
            // jumpto <sector type ID>: gives an unvisited neighbor that sector type, then jumps there (no FTL animation, no fuel).
            case ["jumpto", var type]:
            {
                var run = Autotest.RequireRun();
                var id = new ID<SectorTypeRules>(type);
                if (!GameApp.Rules.CareerMode.SectorTypesByID.ContainsKey(id)) throw new InvalidOperationException($"no sector type '{type}'");
                var next = run.Game.CurrentMapNode.Connections.FirstOrDefault(n => !run.State.VisitedNodes.Contains(n.UniqueID))
                    ?? throw new InvalidOperationException("no unvisited neighbor system");
                run.Mode.GetMapNodeData(next).SectorTypeID = id;
                run.Game.JumpToNode(next, doFtlAnimation: false, run.Game.Sim.Ships.HumanPlayerShips.ToList())
                    .ContinueWith(t => Log.Error("AUTOTEST jumpto failed", t.Exception), TaskContinuationOptions.OnlyOnFaulted);
                Autotest.Wait(8);
                break;
            }
            // paint: paints the trade hub current (the HotCargoCurrent spawner of the hc_trade_hub rules) centered on the first
            // own ship. For co-op tests, which cannot jump: run it as "mp worlds paint", so it changes the sim on every machine.
            case ["paint"]:
            {
                var sim = Sim();
                var spawner = MissionArea.AllSpawners(GameApp.Rules.CareerMode.SectorTypesByID[new ID<SectorTypeRules>("hc_trade_hub")].SystemGenerator.Spawners)
                    .OfType<CurrentSpawner>().FirstOrDefault() ?? throw new InvalidOperationException("the trade hub rules have no HotCargoCurrent spawner");
                spawner.DoSpawn(sim, sim.Rand, 0, 0, 1, null, Ship().DetWorldCenter, [], new SpawnedObjectCollection(), 0f, loadInstantly: true, []);
                break;
            }
            case ["type", var type]:
                Autotest.Check(SectorId() == type, $"sector type is {type} (actual {SectorId()})");
                break;
            // map: logs the sector types of the run map and checks that every archetype is on it and the start is basic.
            case ["map"]:
            {
                var run = Autotest.RequireRun();
                var counts = run.Game.Map.Nodes.GroupBy(n => run.Mode.GetMapNodeData(n).SectorType.ID.ToString())
                    .ToDictionary(g => g.Key, g => g.Count());
                Log.Info($"AUTOTEST MAP sector types: {string.Join(", ", counts.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"))}");
                foreach (var sector in GameApp.Rules.CareerMode.SectorTypes.Where(s => s.ID.ToString().StartsWith("hc_", StringComparison.Ordinal)))
                    Autotest.Check(counts.GetValueOrDefault(sector.ID.ToString()) > 0, $"archetype {sector.ID} is on the run map");
                var start = run.Game.Map.Nodes.Single(n => run.Mode.GetMapNodeData(n).IsInitNode);
                Autotest.Check(run.Mode.GetMapNodeData(start).SectorType.ID.ToString() == "basic", "the starting system is basic");
                break;
            }
            case ["hazard"]:
            {
                var run = Autotest.RequireRun();
                string? tip = run.Mode.GetMapNodeToolTip(run.Game.CurrentMapNode);
                string? hazard = WorldsArea.Hazard(WorldsArea.SectorOf(run.Mode, run.Game.CurrentMapNode));
                Autotest.Check(hazard is not null && tip?.Contains(hazard, StringComparison.Ordinal) == true, $"tooltip shows the hazard (tooltip: {tip})");
                break;
            }
            // always: every archetype mission group spawns (SpawnChance 1) in systems that generate from now on. Rules objects, test only.
            case ["always"]:
            {
                var tags = Metatypes.Keys.Select(k => new ID<SimObjectSpawner>($"hc_group_{k}")).ToHashSet();
                var groups = GameApp.Rules.CareerMode.SectorTypes.Where(t => t.ID.ToString().StartsWith("hc_", StringComparison.Ordinal))
                    .SelectMany(t => MissionArea.AllSpawners(t.SystemGenerator.Spawners)).OfType<SimObjectSpawner>()
                    .Where(g => g.Tags?.Overlaps(tags) == true).Distinct().ToList();
                foreach (var g in groups) g.SpawnChance = 1f;
                Log.Info($"AUTOTEST {groups.Count} archetype mission groups always spawn");
                break;
            }
            // clearance: archetype objects keep clear of station and gate centers (spec 229). Checks only the objects in this system.
            case ["clearance"]:
                Clearance();
                break;
            case ["generate", var kind]:
                MissionArea.GenerateGroups(Sim(), [$"hc_group_{Kind(kind)}"]);
                break;
            case ["accept", var kind]:
                _accepted = MissionArea.AcceptOffer(Metatypes[Kind(kind)]);
                break;
            // reselect <kind>: after a load, the newest accepted mission of that kind becomes the one that the fields read.
            case ["reselect", var kind]:
            {
                var id = new ID<MissionMetatype>(Metatypes[Kind(kind)]);
                _accepted = Autotest.RequireRun().Mode.Missions.LastOrDefault(m => m.MetatypeID.Equals(id))
                    ?? throw new InvalidOperationException($"no {kind} mission in the log");
                break;
            }
            case ["mark"]:
                _markedHeat = Heat(Ship());
                break;
            // cool: clears the heat status of the first own ship and repairs it, so the next exposure starts cold.
            case ["cool"]:
            {
                var ship = Ship();
                foreach (var h in ship.Statuses.TileHandlers.Where(h => h.StatusType.ID.ToString() == "cosmoteer.heat")) h.ClearStatuses();
                foreach (var part in ship.Parts) part.Health = part.Rules.MaxHealth;
                break;
            }
            case ["keep"]:
                _keptGain = Heat(Ship()) - _markedHeat;
                Log.Info($"AUTOTEST kept heat gain {_keptGain:0}");
                break;
            // goto current: the middle triangle of the current, at rest.
            case ["goto", "current"]:
            {
                var centers = Current.TriangleCenters(Sim(), Current.NebulaId);
                if (centers.Count == 0) throw new InvalidOperationException("no current in this system");
                Vector2 mean = centers.Aggregate(Vector2.Zero, (a, c) => a + c) / centers.Count;
                Teleport(centers.MinBy(c => c.DistanceTo(mean)));
                break;
            }
            // goto nocurrent: open space beside the current, at rest (a control for the drift test).
            case ["goto", "nocurrent"]:
            {
                var sim = Sim();
                var centers = Current.TriangleCenters(sim, Current.NebulaId);
                if (centers.Count == 0) throw new InvalidOperationException("no current in this system");
                Vector2 mean = centers.Aggregate(Vector2.Zero, (a, c) => a + c) / centers.Count;
                Vector2 flow = Current.FlowVector(sim);
                Vector2 side = new(-flow.Y, flow.X);
                var suns = HeatZone.Suns(sim);
                Vector2? at = Enumerable.Range(2, 8).SelectMany(k => new[] { k, -k }).Select(k => (Vector2?)(mean + side * (k * 2000f)))
                    .FirstOrDefault(p => sim.Nebulas.GetInsideStrength(p!.Value, Current.NebulaId) <= 0f
                        && suns.All(sun => p.Value.DistanceTo(sun.Center) > sun.EdgeRadius + 1000f));
                Teleport(at ?? throw new InvalidOperationException("no open space beside the current and away from suns"));
                break;
            }
            // goto sun <depth>: the point at that depth in the heat zone of the first sun, on its +X side.
            case ["goto", "sun", var depthText]:
            {
                float depth = float.Parse(depthText, CultureInfo.InvariantCulture);
                var sun = HeatZone.Suns(Sim()).FirstOrDefault();
                if (sun == default) throw new InvalidOperationException("no sun in this system");
                Teleport(sun.Center + new Vector2(sun.EdgeRadius - depth * (sun.EdgeRadius - sun.KillRadius), 0f));
                break;
            }
            case ["goto", "pass"]:
                Teleport(Objective<HoldPassObjective>().Center);
                break;
            case ["goto", "away"]:
                Teleport(Objective<HoldPassObjective>().Center + new Vector2(Objective<HoldPassObjective>().Radius + 1500f, 0f));
                break;
            case ["goto", "sundive"]:
                Teleport(Objective<SundiveObjective>().Site);
                break;
            // goto outside: just outside the heat zone of the first sun.
            case ["goto", "outside"]:
            {
                var sun = HeatZone.Suns(Sim()).FirstOrDefault();
                if (sun == default) throw new InvalidOperationException("no sun in this system");
                Teleport(sun.Center + new Vector2(sun.EdgeRadius + 600f, 0f));
                break;
            }
            // goto lair <distance>: that far from the lair of the accepted raid, on its +X side.
            case ["goto", "lair", var distText]:
            {
                var lair = Objective<FindLairObjective>().Lair.Location ?? throw new InvalidOperationException("lair is not in this system");
                Teleport(lair + new Vector2(float.Parse(distText, CultureInfo.InvariantCulture), 0f));
                break;
            }
            // kill <tag>: removes the live ships with the spawn tag.
            case ["kill", var tag]:
            {
                var sim = Sim();
                int killed = 0;
                foreach (var t in SimQueries.TaggedShips(sim, new ID<SimObjectSpawner>(tag)))
                    if (t.Live is { } ship) { sim.Ships.Remove(ship, dispose: true, handleLostShip: false); killed++; }
                Log.Info($"AUTOTEST killed {killed} ships tagged {tag}");
                Autotest.Wait(2);
                break;
            }
            default:
                throw new InvalidOperationException("worlds needs: set F V | jumpto TYPE | paint | type TYPE | map | hazard | always | clearance | generate K | accept K | reselect K | mark | cool | keep | "
                    + "goto current|sun D|pass|away|sundive|outside|lair D | kill TAG");
        }
    }

    static readonly ID<SimObjectSpawner> HubPlanet = new("hc_hub_planet"), Station = new("station"), Gate = new("ftl_gate"),
        TradeStation = new("trade_station");

    /// <summary>Centers of the spawned objects with a tag in this system, live or in stasis. Crashes when there is none.</summary>
    static List<Vector2> At(ID<SimObjectSpawner> tag) =>
        MissionArea.ExistingSpawnTags(Sim()).TryGetValue(tag, out var list) && list.Count > 0
            ? [.. list.Select(so => so.Location.Center)]
            : throw new InvalidOperationException($"no object tagged {tag} in this system");

    /// <summary>Distances of the tagged objects from the system center, logged for the test record.</summary>
    static List<double> Radii(ID<SimObjectSpawner> tag)
    {
        var radii = At(tag).Select(p => (double)p.Length).Order().ToList();
        Log.Info($"AUTOTEST {tag} radii: {string.Join(" ", radii.Select(r => r.ToString("0", CultureInfo.InvariantCulture)))}");
        return radii;
    }

    static void Clearance()
    {
        var sim = Sim();
        var objects = MissionArea.ExistingSpawnTags(sim);
        List<Vector2> At(ID<SimObjectSpawner> tag) => objects.TryGetValue(tag, out var list) ? [.. list.Select(so => so.Location.Center)] : [];
        // The tags of the objects at a point, for the log.
        string TagsAt(Vector2 p) => string.Join(" ", objects.Where(kv => kv.Value.Any(so => so.Location.Center == p)).Select(kv => kv.Key.ToString()).Order(StringComparer.Ordinal));
        var lairs = At(Lair);
        var fixedPoints = At(Gate).Concat(At(Station).Where(p => !lairs.Contains(p))).ToList();
        if (fixedPoints.Count == 0) throw new InvalidOperationException("no station or gate in this system");
        // what: object kind; items: each object's center and the radius to subtract (a pass ring's radius, else 0).
        void CheckMin(string what, IReadOnlyList<Vector2> to, IEnumerable<(Vector2 At, float Radius)> items, float min)
        {
            var gaps = items.Select(i => (i.At, Near: to.MinBy(q => q.DistanceTo(i.At)), Gap: to.Min(q => q.DistanceTo(i.At)) - i.Radius)).ToList();
            if (gaps.Count == 0) { Log.Info($"AUTOTEST clearance: no {what}"); return; }
            var worst = gaps.MinBy(g => g.Gap);
            Autotest.Check(worst.Gap >= min, $"{what}: closest {worst.Gap:0} >= {min} ({gaps.Count} objects; {worst.At} to [{TagsAt(worst.Near)}] at {worst.Near})");
        }
        CheckMin("pass ring edge to station or gate center", fixedPoints, Doodads(sim, Pass).Select(c => (c.Center, c.Radius)), 600f);
        CheckMin("belt rock to station or gate center", fixedPoints, Tagged(sim, BeltRock).Select(p => (p, 0f)), 600f);
        CheckMin("hub planet to gate center", At(Gate), At(HubPlanet).Select(p => (p, 0f)), 2000f);
        // The lair keeps the vanilla military gaps times the system scale 0.5: 3000 from stations, 1500 from gates (spec 105).
        CheckMin("lair to station center", At(Station).Where(p => !lairs.Contains(p)).ToList(), lairs.Select(p => (p, 0f)), 3000f);
        CheckMin("lair to gate center", At(Gate), lairs.Select(p => (p, 0f)), 1500f);
    }

    static int State(CompletionState c) => c switch { CompletionState.Completed => 1, CompletionState.Failed => -1, _ => 0 };

    static int State(Cosmoteer.Modes.Career.Missions.Objectives.Objective o) => State(o.Completion);

    static string Kind(string kind) => Metatypes.ContainsKey(kind) ? kind : throw new InvalidOperationException($"unknown Worlds mission '{kind}'");

    static SimRoot Sim() => Autotest.RequireRun().Game.Sim ?? throw new InvalidOperationException("no sim");

    static Ship Ship() => SimQueries.FirstHumanShip(Sim()) ?? throw new InvalidOperationException("no human ship");

    static string SectorId() => Autotest.RequireRun().Mode.CurrentSectorType.ID.ToString();

    /// <summary>Moves the first own ship and unpauses: test slots pause by themselves when an enemy comes into view.</summary>
    static void Teleport(Vector2 at)
    {
        var ship = Ship();
        Autotest.Teleport(ship, at);
        Autotest.RequireRun().Game.NetManager.EnqueueInput(new MPSetPausedInput(false));
        Log.Info($"AUTOTEST teleported {ship.Name} to {at}");
        Autotest.Wait(2);
    }

    /// <summary>The sum of the cosmoteer.heat status values on the tiles of a ship.</summary>
    static float Heat(Ship ship) =>
        ship.Statuses.TileHandlers.Where(h => h.StatusType.ID.ToString() == "cosmoteer.heat")
            .Sum(h => h.GetActiveStatusLocations().Sum(l => l.info.CombinedValue));

    /// <summary>Locations of the objects with a spawn tag: ships (live or in stasis) and doodads (live or in stasis).</summary>
    static List<Vector2> Tagged(SimRoot sim, ID<SimObjectSpawner> tag) =>
        [.. SimQueries.TaggedShips(sim, tag).Select(t => t.Location), .. Doodads(sim, tag).Select(c => c.Center)];

    static T Objective<T>() where T : Cosmoteer.Modes.Career.Missions.Objectives.Objective
    {
        var found = new List<T>();
        foreach (var o in (_accepted ?? throw new InvalidOperationException("no mission accepted with worlds accept")).Objectives) o.GetObjectives(found);
        return found.SingleOrDefault() ?? throw new InvalidOperationException($"the accepted mission has no {typeof(T).Name}");
    }

    /// <summary>The doodads with a spawn tag, live or in stasis. The radius is the scale, which is the ring radius of a ring POI.</summary>
    static List<Circle> Doodads(SimRoot sim, ID<SimObjectSpawner> tag) =>
        [.. sim.Doodads.Where(d => d.Tags?.Contains(tag) == true).Select(d => new Circle(d.Location, d is RingedPOIDoodad r ? r.UniformScale : 0f)),
         .. sim.Stasis.OfType<StasisDoodadSpawner>().Where(s => s.Tags?.Contains(tag) == true).OrderBy(s => s.ReservedObjectID).Select(s => new Circle(s.Location, s.ScaleFactor))];
}

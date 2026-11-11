using System.Globalization;
using System.Reflection;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Map;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
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

    static float _markedHull;

    internal static void Register()
    {
        Autotest.Commands["worlds"] = Command;
        Autotest.Fields["worlds_current_tris"] = () => Current.TriangleCenters(Sim(), Current.NebulaId).Count;
        Autotest.Fields["worlds_in_current"] = () => Ship().Nebulas.GetNebulaStrength(Current.NebulaId);
        Autotest.Fields["worlds_flow_speed"] = () => ((Vector2)(Ship().Physics.Body ?? throw new InvalidOperationException("ship has no body")).LinearVelocity).Dot(Current.FlowVector(Sim()));
        Autotest.Fields["worlds_belt_rocks"] = () => SimQueries.TaggedShips(Sim(), BeltRock).Count;
        Autotest.Fields["worlds_passes"] = () => Doodads(Sim(), Pass).Count;
        // Belt rocks within 75 % of the pass radius of a pass center. The belt spawner keeps rocks out of the passes, so expect 0.
        Autotest.Fields["worlds_pass_rocks"] = () =>
        {
            var sim = Sim();
            var passes = Doodads(sim, Pass);
            return SimQueries.TaggedShips(sim, BeltRock).Count(r => passes.Any(p => r.Location.DistanceTo(p) < PassRadius() * 0.75f));
        };
        Autotest.Fields["worlds_suns"] = () => HeatZone.Suns(Sim()).Count;
        Autotest.Fields["worlds_heat_depth"] = () => HeatZone.Depth(Sim(), Ship().DetWorldCenter);
        // Hull points lost by the first own ship since "worlds mark".
        Autotest.Fields["worlds_hull_loss"] = () => _markedHull - Hull(Ship());
        Autotest.Fields["worlds_lairs"] = () => SimQueries.TaggedShips(Sim(), Lair).Count;
        Autotest.Fields["worlds_hc_sector"] = () => SectorId().StartsWith("hc_", StringComparison.Ordinal) ? 1 : 0;
        Autotest.Fields["worlds_pass_held"] = () => Objective<HoldPassObjective>().HeldSeconds;
        Autotest.Fields["worlds_pass_waves"] = () => Objective<HoldPassObjective>().WavesSpawned;
        Autotest.Fields["worlds_sundive_carrier"] = () => Objective<SundiveObjective>().Carrier is null ? 0 : 1;
        foreach (var (name, metatype) in Metatypes)
        {
            var id = new ID<MissionMetatype>(metatype);
            Autotest.Fields[$"worlds_offers_{name}"] = () => MissionArea.Offers(Sim()).Count(m => m.MetatypeID.Equals(id));
            // 1 completed, -1 failed, 0 in progress, -2 never accepted. The latest accepted mission counts.
            Autotest.Fields[$"worlds_state_{name}"] = () =>
                Autotest.RequireRun().Mode.Missions.LastOrDefault(m => m.MetatypeID.Equals(id)) is not { } m ? -2
                : m.Completion switch { CompletionState.Completed => 1, CompletionState.Failed => -1, _ => 0 };
        }
    }

    static void Command(string arg)
    {
        var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts)
        {
            // set <Field> <number|on|off>: one WorldsTunables field for this run.
            case ["set", var name, var value]:
            {
                var field = typeof(WorldsTunables).GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                    ?? throw new InvalidOperationException($"unknown Worlds field '{name}'");
                object v = field.FieldType == typeof(bool) ? value == "on"
                    : field.FieldType == typeof(float) ? float.Parse(value, CultureInfo.InvariantCulture)
                    : throw new InvalidOperationException($"Worlds field '{name}' has type {field.FieldType.Name}");
                field.SetValue(Autotest.RequireRun().T.Worlds, v);
                break;
            }
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
            case ["generate", var kind]:
                MissionArea.GenerateGroups(Sim(), [$"hc_group_{Kind(kind)}"]);
                break;
            case ["accept", var kind]:
                MissionArea.AcceptOffer(Metatypes[Kind(kind)]);
                break;
            case ["mark"]:
                _markedHull = Hull(Ship());
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
                throw new InvalidOperationException("worlds needs: set F V | jumpto TYPE | type TYPE | map | hazard | generate K | accept K | mark | "
                    + "goto current|sun D|pass|away|sundive|outside|lair D | kill TAG");
        }
    }

    static string Kind(string kind) => Metatypes.ContainsKey(kind) ? kind : throw new InvalidOperationException($"unknown Worlds mission '{kind}'");

    static SimRoot Sim() => Autotest.RequireRun().Game.Sim ?? throw new InvalidOperationException("no sim");

    static Ship Ship() => SimQueries.FirstHumanShip(Sim()) ?? throw new InvalidOperationException("no human ship");

    static string SectorId() => Autotest.RequireRun().Mode.CurrentSectorType.ID.ToString();

    static float PassRadius() => Objective<HoldPassObjective>() is { } o ? o.Radius : 800f;

    static void Teleport(Vector2 at)
    {
        var ship = Ship();
        Autotest.Teleport(ship, at);
        Log.Info($"AUTOTEST teleported {ship.Name} to {at}");
        Autotest.Wait(2);
    }

    static float Hull(Ship ship) => ship.Parts.Sum(p => (float)p.Health);

    static T Objective<T>() where T : Cosmoteer.Modes.Career.Missions.Objectives.Objective =>
        MissionArea.FindObjective<T>() ?? throw new InvalidOperationException($"no accepted mission has a {typeof(T).Name}");

    /// <summary>Locations of the doodads with a spawn tag, live or in stasis.</summary>
    static List<Vector2> Doodads(SimRoot sim, ID<SimObjectSpawner> tag) =>
        [.. sim.Doodads.Where(d => d.Tags?.Contains(tag) == true).Select(d => d.Location),
         .. sim.Stasis.OfType<StasisDoodadSpawner>().Where(s => s.Tags?.Contains(tag) == true).OrderBy(s => s.ReservedObjectID).Select(s => s.Location)];
}

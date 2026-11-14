using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling;
using Halfling.Random;
using HotCargo.Autonomy;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Scavenge;

/// <summary>
/// Autotest commands and fields for scavenging (spec 1339). The scav_ fields read the ship that scav_spawn made last.
/// scav_spawn changes the sim: use "mp" in co-op tests.
/// </summary>
internal static class ScavengeAutotest
{
    static SimRoot Sim() => Autotest.RequireRun().Game.Sim;

    static string _name = "";

    static Ship TestShip() => Sim().Ships.AllShips.Where(s => s.Metadata.PlainShipName == _name && !s.IsJunk).MinBy(s => s.UniqueID)
        ?? throw new InvalidOperationException($"no live ship named '{_name}'");

    static ScavengeStrategy Module(Ship s) => s.AI?.GetStrategyModule<ScavengeStrategy>() ?? throw new InvalidOperationException($"{s.Metadata.PlainShipName} has no HcScavenge module");

    internal static void Register()
    {
        // scav_set <Field> <value>: sets a Scavenge tunable for this run (setup only; no validation).
        Autotest.Commands["scav_set"] = arg => Autotest.SetField(Autotest.RequireRun().T.Scavenge, arg);
        // scav_spawn <name> <where> [keep]: a tier 1 to 3 faction trade ship (crew, empty storage bays) with the StationGuard AI (patrols 500 around its spawn point) and the
        // HcScavenge module, renamed to name. where: player or station (next to the nearest live trading station). keep: Sells = false.
        // The auto_ fields of the autonomy tests read it after "auto_ship <name>".
        Autotest.Commands["scav_spawn"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length is < 2 or > 3) throw new InvalidOperationException("scav_spawn needs: name where [keep]");
            _name = p[0];
            Spawn(p[0], p[1], sells: p.Length < 3 || p[2] != "keep");
        };
        Autotest.Commands["scav_ship"] = arg => _name = arg.Trim();
        Autotest.Fields["scav_live"] = () => Sim().Ships.AllShips.Count(s => s.Metadata.PlainShipName == _name && !s.IsJunk);
        Autotest.Fields["scav_step"] = () => (int)Module(TestShip()).Step;
        // 0 no target, 1 a wreck, 2 a loose resource.
        Autotest.Fields["scav_target"] = () => Scavenger.FindTarget(Autotest.RequireRun(), TestShip()) switch
        {
            null => 0,
            WreckTarget => 1,
            LootTarget => 2,
            var t => throw new InvalidOperationException($"unknown target {t}"),
        };
        Autotest.Fields["scav_targets"] = () => Scavenger.Targets(Autotest.RequireRun(), TestShip()).Count;
        Autotest.Fields["scav_seen"] = () => Autotest.RequireRun().State.Scavenge.Seen.Count;
        // 1 idle; else why not: -1 no command, -2 an attack order, -3 the exit module runs, -4 an enemy in sight (logged).
        Autotest.Fields["scav_idle"] = () =>
        {
            var s = TestShip();
            var ai = s.AI ?? throw new InvalidOperationException($"{s.Metadata.PlainShipName} has no AI");
            if (Scavenger.Idle(s, ai)) return 1;
            if (!s.Commands.CanExecuteCommands) return -1;
            if (s.Commands.CurConfirmedCommand is Cosmoteer.Ships.Commands.IAttackCommand) return -2;
            if (ai.GetStrategyModule<Cosmoteer.Ships.AI.StrategyModules.AIExitSectorModule>() is { IsActivated: true }) return -3;
            var enemy = Scavenger.EnemyInSight(s);
            Log.Info($"AUTOTEST scav_idle: enemy {enemy?.Metadata.PlainShipName} player={enemy?.Metadata.PlayerIndex} at {(enemy is null ? 0 : Hunters.HunterOrders.EdgeDistance(s, enemy)):0}");
            return -4;
        };
        Autotest.Fields["scav_cargo"] = () => AutonomyWorld.CargoByType(TestShip()).Values.Sum();
        Autotest.Fields["scav_sold"] = () => Scavenger.SoldUnits;
        // Parts left on the junk pieces named "Test wreck" (auto_wreck).
        Autotest.Fields["scav_wreck_parts"] = () => Sim().Ships.AllShips.Where(s => s.IsJunk && s.Metadata.PlainShipName == "Test wreck").Sum(s => s.Parts.Count);
    }

    static void Spawn(string name, string where, bool sells)
    {
        var sim = Sim();
        var me = Autotest.ActingShip(sim) ?? throw new InvalidOperationException("no player ship");
        var faction = sim.MapNode?.ControllingFaction is { } fid ? GameApp.Rules.Factions.First(f => f.ID == fid) : GameApp.Rules.Factions.First();
        var rand = new FastRand((ulong)(uint)sim.Tick, 0x5343_4156UL);
        var built = GameApp.Rules.BuiltinShips.GetRandomMatchingShip(rand, [faction.ID], new Range<int>(1, 3), null, [new ID<BuiltinShipTag>("trade")])
            ?? GameApp.Rules.BuiltinShips.GetRandomMatchingShip(rand, null, new Range<int>(1, 3), null, [new ID<BuiltinShipTag>("trade")])
            ?? throw new InvalidOperationException("no built-in trade ship of tier 1 to 3");
        var near = where switch
        {
            "player" => me.DetWorldCenter,
            "station" => AutonomyWorld.Stations(me).FirstOrDefault(s => s.Live is not null).Live?.DetWorldCenter ?? throw new InvalidOperationException("no live trading station"),
            _ => throw new InvalidOperationException($"scav_spawn: unknown place '{where}'"),
        };
        var loc = Autotest.ClearSpotNear(sim, near, 700f, 150f);
        var spawner = new StasisShipSpawner(sim, built.ID, loc, SpawnedShipFeatures.None, rand.UInt64(), [])
        {
            AI = new ID<ShipAIRules>("StationGuard"),
            Direction = loc.DirectionTo(near),
            ObjectAllegiance = faction.CivilianPlayerIndex,
        };
        spawner.Spawn(sim);
        var ship = sim.Ships[spawner.ReservedObjectID] ?? throw new InvalidOperationException($"{built.ID} did not spawn");
        ship.Metadata.PlainShipName = name;
        var ai = ship.AI ?? throw new InvalidOperationException($"{name} has no AI");
        ai.AddModule(new ScavengeStrategy(new ScavengeStrategyRules { Sells = sells }));
        Log.Info($"AUTOTEST scav_spawn {name} ({built.ID}) id={ship.UniqueID} player={ship.Metadata.PlayerIndex} crew={ship.Crew.Assigned.Count} " +
                 $"cargo={AutonomyWorld.CargoFill(ship)?.ToString("0.00") ?? "none"} sells={sells}");
    }
}

using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Commands;
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
        RegisterFaction();
        // scav_set <Field> <value>: sets a Scavenge tunable for this run (setup only; no validation).
        Autotest.Commands["scav_set"] = arg => Autotest.SetField(Autotest.RequireRun().T.Scavenge, arg);
        // scav_spawn <name> <where>: a tier 1 to 3 faction trade ship (crew, empty storage bays) with the StationGuard AI (patrols 500 around
        // its spawn point; the AI has the HcScavenge module, spec 1350), renamed to name. where: player or station (next to the nearest live
        // trading station).
        // The auto_ fields of the autonomy tests read it after "auto_ship <name>".
        Autotest.Commands["scav_spawn"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 2) throw new InvalidOperationException("scav_spawn needs: name where");
            _name = p[0];
            Spawn(p[0], p[1]);
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

    static Ship Me() => Autotest.ActingShip(Sim()) ?? throw new InvalidOperationException("no player ship");

    static int Enemies(int a, int b) => Sim().Mode.AreEnemies(a, b) ? 1 : 0;

    static void RegisterFaction()
    {
        // scav_new <where>: a real scavenger (spec 1342) next to the player's first ship (player) or the nearest live trading station
        // (station). The scav_ fields then read it.
        Autotest.Commands["scav_new"] = arg =>
        {
            var sim = Sim();
            var me = Me();
            var near = arg.Trim() switch
            {
                "player" => me.DetWorldCenter,
                "station" => AutonomyWorld.Stations(me).FirstOrDefault(s => s.Live is not null).Live?.DetWorldCenter ?? throw new InvalidOperationException("no live trading station"),
                var w => throw new InvalidOperationException($"scav_new: unknown place '{w}'"),
            };
            var at = Autotest.ClearSpotNear(sim, near, 400f, 60f);
            _name = ScavengerShip.Spawn(sim, at, at.DirectionTo(near)).Metadata.PlainShipName;
        };
        // scav_age <seconds>: makes every wreck and loose resource that the age check saw so far this much older (setup only), so a test
        // need not wait out the cooldown. Pieces that come later keep their real age.
        Autotest.Commands["scav_age"] = arg =>
        {
            var seen = Autotest.RequireRun().State.Scavenge.Seen;
            double by = double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
            foreach (var id in seen.Keys.ToList()) seen[id] -= by;
        };
        // 1 when the age check saw every junk piece named "Test wreck" (auto_wreck).
        Autotest.Fields["scav_wreck_seen"] = () =>
        {
            var wrecks = Sim().Ships.AllShips.Where(s => s.IsJunk && s.Metadata.PlainShipName == "Test wreck").ToList();
            return wrecks.Count > 0 && wrecks.All(w => Autotest.RequireRun().State.Scavenge.Seen.ContainsKey(w.UniqueID)) ? 1 : 0;
        };
        // scav_npc <pirate|faction> <distance>: a tier 1 to 4 combat ship with storage bays, at least distance from the player's first ship,
        // with the StationGuard AI (it patrols 500 around its spawn point; the AI has HcScavenge, spec 1350). pirate: a new hostile side;
        // faction: the military of the system's faction. The scav_ fields then read it.
        Autotest.Commands["scav_npc"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 2) throw new InvalidOperationException("scav_npc needs: pirate|faction distance");
            var sim = Sim();
            var me = Me();
            var faction = sim.MapNode?.ControllingFaction is { } fid ? GameApp.Rules.Factions.First(f => f.ID == fid) : GameApp.Rules.Factions.First();
            int allegiance = p[0] switch
            {
                "pirate" => Diplomacy.Situation.Career(sim).AllocUniquePlayerIndex(),
                "faction" => faction.MilitaryPlayerIndex,
                var k => throw new InvalidOperationException($"scav_npc: unknown kind '{k}'"),
            };
            var ships = new List<BuiltinShipRules>();
            GameApp.Rules.BuiltinShips.GetMatchingShips(ships, null, new Range<int>(1, 4), null, [new ID<BuiltinShipTag>("combat")]);
            var built = ships.OrderBy(b => b.ID.ToString(), StringComparer.Ordinal)
                .FirstOrDefault(b => b.File.LoadShipBlueprints().BlueprintParts.Any(bp => bp.Rules.ID.ToString().StartsWith("cosmoteer.storage_", StringComparison.Ordinal)))
                ?? throw new InvalidOperationException("no built-in combat ship of tier 1 to 4 with storage bays");
            var loc = Autotest.ClearSpotNear(sim, me.DetWorldCenter, float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), 150f);
            var spawner = new StasisShipSpawner(sim, built.ID, loc, SpawnedShipFeatures.None, 77UL, [])
            {
                AI = new ID<ShipAIRules>("StationGuard"),
                Direction = loc.DirectionTo(me.DetWorldCenter),
                ObjectAllegiance = allegiance,
            };
            spawner.Spawn(sim);
            var ship = sim.Ships[spawner.ReservedObjectID] ?? throw new InvalidOperationException($"{built.ID} did not spawn");
            _name = $"Test {p[0]}";
            ship.Metadata.PlainShipName = _name;
            Log.Info($"AUTOTEST scav_npc {_name} ({built.ID}) player={allegiance} crew={ship.Crew.Assigned.Count} fill={AutonomyWorld.CargoFill(ship)?.ToString("0.00") ?? "none"}");
        };
        Autotest.Fields["scav_excess"] = () => TestShip() is var s ? Module(s).Excess(s) : 0;
        Autotest.Fields["scav_count"] = () => Scavengers.InSystem(Sim());
        Autotest.Fields["scav_lasers"] = () => TestShip().Weapons.OfType<Cosmoteer.Ships.Parts.Weapons.TurretWeapon>().Count(AutonomyWorld.IsSalvageTurret);
        Autotest.Fields["scav_crew"] = () => TestShip().Crew.Assigned.Count;
        Autotest.Fields["scav_enemy_player"] = () => Enemies(Scavengers.PlayerIndex, Me().Metadata.PlayerIndex);
        Autotest.Fields["scav_enemy_faction"] = () => Enemies(Scavengers.PlayerIndex, GameApp.Rules.Factions.First().MilitaryPlayerIndex);
        // The scavengers and the nearest pirate (an allocated side: index 100,000 or more); -1 with no pirate in the system.
        Autotest.Fields["scav_enemy_pirate"] = () => Sim().Ships.AllShips.Where(s => !s.IsJunk && s.Metadata.PlayerIndex >= 100000).MinBy(s => s.UniqueID) is { } pirate
            ? Enemies(Scavengers.PlayerIndex, pirate.Metadata.PlayerIndex) : -1;
        Autotest.Fields["scav_provoked"] = () => Autotest.RequireRun().State.Scavenge.ProvokedUntil > Autotest.RequireRun().State.GameSeconds ? 1 : 0;
        Autotest.Fields["scav_fleeing"] = () => TestShip().AI?.GetStrategyModule<ShyStrategy>() is { Fleeing: true } ? 1 : 0;
        // scav_attack: the player's first ship attacks the test scavenger (a real attack order, so its shots provoke them). scav_ceasefire stops it.
        Autotest.Commands["scav_attack"] = _ =>
        {
            var me = Me();
            me.Commands.EnqueueCommand(new AttackCommand(me, me.Commands.AllocGlobalCommandID(), TestShip(), CollisionAvoidanceFlags.All, isTentative: false, isConfirmed: true),
                append: false, confirmed: true);
        };
        Autotest.Commands["scav_ceasefire"] = _ =>
        {
            var me = Me();
            me.Commands.RemoveAll(c => c is IAttackCommand { Target: { } t } && Scavengers.IsScavenger(t));
            me.Weapons.ClearAllWeaponTargetsAtPlayerIndex(Scavengers.PlayerIndex);
        };
    }

    static void Spawn(string name, string where)
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
        if (ai.GetStrategyModule<ScavengeStrategy>() is null) throw new InvalidOperationException($"{name}: the StationGuard AI has no HcScavenge module");
        Log.Info($"AUTOTEST scav_spawn {name} ({built.ID}) id={ship.UniqueID} player={ship.Metadata.PlayerIndex} crew={ship.Crew.Assigned.Count} " +
                 $"cargo={AutonomyWorld.CargoFill(ship)?.ToString("0.00") ?? "none"}");
    }
}

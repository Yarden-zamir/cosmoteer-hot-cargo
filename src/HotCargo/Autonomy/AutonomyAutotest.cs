using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Crew;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Ships.Parts.UI;
using Cosmoteer.Ships.Parts.Weapons;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using Cosmoteer.Simulation.Stasis;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Hunters;
using HotCargo.Tech;

namespace HotCargo.Autonomy;

/// <summary>
/// Autotest commands for autonomous ships (spec 1082). Test ships are built in code from a part list, so the repo holds no ship files.
/// Sim changes (auto_build, auto_loot, auto_cargo, auto_wreck, auto_kill) run inside the sim: use "mp" in co-op tests.
/// </summary>
internal static class AutonomyAutotest
{
    static SimRoot Sim() => Autotest.RequireRun().Game.Sim;

    /// <summary>The ship that the auto_ fields read: the last one built, or the one auto_ship picked.</summary>
    static string _shipName = "";

    static Ship TestShip() => Sim().Ships.AllShips.Where(s => s.Metadata.PlainShipName == _shipName && !s.IsJunk).MinBy(s => s.UniqueID)
        ?? throw new InvalidOperationException($"no live ship named '{_shipName}'");

    static Ship Home() => TechAutotest.PlayerShip();

    static int _moneyMark, _homeMark;
    static ObjectID? _rock;
    /// <summary>The ship that auto_mark ran for, so a test can wait until a co-op mark reached the sim.</summary>
    static ObjectID? _marked;

    static int OwnerMoney() => Sim().RequireGame().GetPlayerState(TestShip().Metadata.PlayerIndex, deterministic: true).Money;

    static int OwnerHomeSteel() => (Autonomous.FirstCrewed(TestShip()) ?? throw new InvalidOperationException("the test ship's owner has no crewed ship"))
        .Resources.GetResourceTotal(new ID<ResourceRules>("steel"));

    /// <summary>The test ship's mining state in one line, for the failure message of a timed-out wait.</summary>
    static string MiningContext()
    {
        var ship = TestShip();
        var sim = Sim();
        var cargo = ShipAi.ShipTactics.Of(ship)?.Cargo ?? CargoSettings.Default;
        var at = ship.DetWorldCenter;
        string cmd = ship.Commands.CurConfirmedCommand switch
        {
            MoveCommand { Location: { } to } m => $"{m.GetType().Name} to {to} ({to.DistanceTo(at):0} away)",
            { } c => c.GetType().Name,
            null => "none",
        };
        string rock = AutonomyWorld.NearestAsteroid(ship, cargo) is { } a
            ? $"{a.Id} live={a.Live is not null} edge={Math.Max(0f, AutonomyWorld.EdgeDistance(ship, a.Center) - a.Radius):0}" +
              (a.Live is { } live ? $" parts={live.Parts.Count} salvaging={live.Parts.Count(p => ship.Resources.IsSalvaging(p, MPValueType.Confirmed))}" : "")
            : "none";
        string weapons = string.Join("; ", ship.Weapons.Select(w =>
        {
            string target = w is Cosmoteer.Ships.Parts.Weapons.TurretWeapon { Target: { } t } ? $"{t.Ship?.UniqueID} asteroid={t.Ship?.Rules.IsAsteroid}" : "none";
            string explicitTarget = w.ExplicitTargets.CurConfirmedTarget?.Target is Part p ? $"{p.Rules.ID}@{p.Ship.UniqueID}" : "none";
            return $"{w.Part.Rules.ID} manned={(w.Part is Part own && Autonomous.IsManned(own))} fire={w.HasPermissionToFire()} " +
                   $"range={w.GetEffectiveRange(Cosmoteer.Ships.Parts.Weapons.WeaponCoverageMode.Confirmed):0} target={target} explicit={explicitTarget}";
        }));
        var funnels = ship.Parts.Where(p => p.Rules.ID == Autonomous.FunnelId).Select(f => Funnel.ChoiceOf(f).ToString());
        // Loose resources within 1000 of the ship, by type, and whether the ship's Funnels take that type.
        var loose = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < sim.Nuggets.Count; i++)
        {
            var n = sim.Nuggets[i];
            if (n.DetWorldLocation.DistanceTo(at) > 1000f) continue;
            string type = n.ResourceType.ToString();
            loose[type] = loose.GetValueOrDefault(type) + n.Quantity;
        }
        string Accepted(string type) => ship.Parts.Where(p => p.Rules.ID == Autonomous.FunnelId)
            .Any(f => AutonomyModel.FunnelAccepts(Funnel.ChoiceOf(f), cargo, type, Funnel.UnitValue(sim, new ID<ResourceRules>(type)))) ? "taken" : "refused";
        return $"ship {ship.UniqueID} at {at} cmd={cmd} cargo={string.Join(",", AutonomyWorld.CargoByType(ship).Select(kv => $"{kv.Key}:{kv.Value}"))} " +
               $"fill={AutonomyWorld.CargoFill(ship):0.00} rock={rock} salvage_jobs={ship.Resources.GetSalvageJobs(MPValueType.Confirmed).Count()} " +
               $"weapons=[{weapons}] funnels=[{string.Join(",", funnels)}] funnel_taken={Funnel.Taken} " +
               $"loose_near=[{string.Join(", ", loose.Select(kv => $"{kv.Key}:{kv.Value} {Accepted(kv.Key)}"))}]";
    }

    internal static void Register()
    {
        // auto_build <name> <where> <part>@<x>,<y>[,<rot>] ...: builds a ship of the acting player from vanilla and mod parts, complete,
        // with full storages and batteries. where: player (next to the first own ship), station (next to the nearest trading station),
        // asteroid (next to the nearest live asteroid with ore), or x,y offset from the first own ship. The test fields then read this ship.
        Autotest.Commands["auto_build"] = arg => Build(arg);
        Autotest.Commands["auto_ship"] = arg => _shipName = arg;
        // Live ships with the auto_ship name. Unlike the other fields it does not throw with none, so a test can wait for a build
        // that runs in the sim later ("mp auto_build" in co-op).
        Autotest.Fields["auto_ship_live"] = () => Sim().Ships.AllShips.Count(s => s.Metadata.PlainShipName == _shipName && !s.IsJunk);
        // auto_focus <size>: centers the camera on the test ship's position and fits a square of this world size (screenshots, UI only).
        Autotest.Commands["auto_focus"] = arg =>
        {
            // The camera goes to the ship's position: FocusCamera does not follow a ship that the local player does not own.
            var view = Sim().Camera.ViewController;
            view.ClearFocus(clearFramesOfReferenceAndRotation: true);
            view.FRLocation = TestShip().DetWorldCenter;
            Autotest.FitCamera(Sim(), float.Parse(arg, CultureInfo.InvariantCulture));
        };
        // auto_buildmode on|off: selects the test ship and opens or closes build mode, like the build button (screenshots, UI only).
        Autotest.Commands["auto_buildmode"] = arg =>
        {
            var ship = TestShip();
            ship.Sim.PlayerInput.SelectSingleShip(ship);
            Autotest.RequireRun().Game.Gui.ShipGui.BuildButton.IsSelected = arg switch
            {
                "on" => true,
                "off" => false,
                _ => throw new InvalidOperationException($"auto_buildmode needs on or off, not '{arg}'"),
            };
        };
        // auto_buildpart <part id>: picks a part in the build toolbox, like a click in the part list (screenshots, UI only).
        Autotest.Commands["auto_buildpart"] = arg =>
        {
            var rules = TestShip().Rules.PartsByID[new ID<PartRules>(arg)];
            if (!Autotest.RequireRun().Game.Gui.ShipGui.BuildToolbox.SelectPartType(rules)) throw new InvalidOperationException($"the build toolbox did not take {arg}");
        };
        // auto_damage <part id> <fraction>: sets every such part of the test ship to this fraction of its health (screenshots of the
        // damage levels). Single player only; turn godmode off first.
        Autotest.Commands["auto_damage"] = arg =>
        {
            if (Autotest.RequireRun().IsMultiplayer) throw new InvalidOperationException("auto_damage is single player only");
            var p = Args(arg, 2);
            float f = float.Parse(p[1], CultureInfo.InvariantCulture);
            var parts = TestShip().Parts.Where(part => part.Rules.ID.ToString() == p[0]).ToList();
            if (parts.Count == 0) throw new InvalidOperationException($"no {p[0]} on {_shipName}");
            foreach (var part in parts) part.Health = Math.Max(1, (int)(part.Rules.MaxHealth * f));
        };
        // auto_log <label>: logs the test ship's state, so a failed wait shows what the ship did.
        Autotest.Commands["auto_log"] = arg =>
        {
            var named = Sim().Ships.AllShips.Where(s => s.Metadata.PlainShipName == _shipName).ToList();
            foreach (var s in named)
                Log.Info($"AUTOTEST auto_log {arg}: {s.Metadata.PlainShipName} id={s.UniqueID} owner={s.Metadata.PlayerIndex} junk={s.IsJunk} parts={s.Parts.Count} " +
                         $"computers={s.Parts.Count(p => p.Rules.ID == Autonomous.ComputerId)} points={s.Commands.CommandPointsProvided} control={s.Commands.CanExecuteCommands} " +
                         $"crew={s.Crew.Assigned.Count} cmd={s.Commands.CurConfirmedCommand?.GetType().Name} cargo={AutonomyWorld.CargoFill(s):0.00}");
        };
        Autotest.Fields["auto_autonomous"] = () => Autonomous.IsAutonomous(TestShip()) ? 1 : 0;
        Autotest.Fields["auto_owner"] = () => Sim().Ships.AllShips.Where(s => s.Metadata.PlainShipName == _shipName).MinBy(s => s.UniqueID)?.Metadata.PlayerIndex ?? -99;
        Autotest.Fields["auto_crew"] = () => TestShip().Crew.Assigned.Count;
        Autotest.Fields["auto_computers"] = () => TestShip().Parts.Count(p => p.Rules.ID == Autonomous.ComputerId);
        Autotest.Fields["auto_points"] = () => TestShip().Commands.CommandPointsProvided;
        Autotest.Fields["auto_control"] = () => TestShip().Commands.CanExecuteCommands ? 1 : 0;
        Autotest.Fields["auto_manned"] = () => TestShip().Parts.Count(Autonomous.IsManned);
        // Parts with crew slots whose crew toggles are all on (what vanilla reads as crewed), manned or not.
        Autotest.Fields["auto_crewed_parts"] = () => TestShip().Parts.Count(p => p.Components.OfType<PartCrew>().Any() && p.Components.OfType<PartCrew>().All(c => c.IsToggleOn));
        // Battery in the ship's parts that use power, without reactors (what a targeted reactor delivered).
        Autotest.Fields["auto_power"] = () => TestShip().Parts.Where(p => p.Rules.ID != Autonomous.TargetedReactorId)
            .SelectMany(p => p.Components.OfType<ResourceStorage>()).Where(rs => rs.ResourceType.ToString() == "battery").Sum(rs => rs.Resources);
        Autotest.Fields["auto_cargo"] = () => AutonomyWorld.CargoByType(TestShip()).Values.Sum();
        // A timed-out wait for mining shows where the ore stops: the move, the rock, the salvage orders, the lasers, the loose ore.
        Autotest.WaitContext["auto_cargo"] = MiningContext;
        Autotest.WaitContext["auto_salvage_jobs"] = MiningContext;
        Autotest.Fields["auto_cargo_pct"] = () => (int)MathF.Round((AutonomyWorld.CargoFill(TestShip()) ?? 0f) * 100f);
        Autotest.Fields["auto_money"] = () => Sim().RequireGame().GetPlayerState(Autotest.ActingPlayerIndex, deterministic: true).Money;
        Autotest.Fields["auto_funnel_taken"] = () => Funnel.Taken;
        Autotest.Fields["auto_salvage_jobs"] = () => TestShip().Resources.GetSalvageJobs(MPValueType.Confirmed).Count();
        Autotest.Fields["auto_player_orders"] = () => TestShip().Commands.ConfirmedCommands.Count(c => c.UniqueID.PlayerID is not null);
        Autotest.Fields["auto_loot_near"] = () => AutonomyWorld.AcceptedLoot(TestShip(), CargoSettings.Default)
            .Count(n => AutonomyWorld.EdgeDistance(TestShip(), n.DetWorldLocation) < 3000f);
        Autotest.Fields["auto_station_dist"] = () => (int)(AutonomyWorld.PickStation(TestShip(), ShipAi.StationPick.Nearest, CargoSettings.Default) is { } s
            ? Math.Max(0f, AutonomyWorld.EdgeDistance(TestShip(), s.Center) - s.Radius) : -1f);
        Autotest.Fields["auto_home_cargo"] = () => Home().Resources.GetResourceTotal(new ID<ResourceRules>("steel"));
        Autotest.Fields["auto_home_dist"] = () => (int)HunterOrders.EdgeDistance(TestShip(), Home());
        // auto_mark: remembers the money of the test ship's owner and the steel of the owner's first crewed ship, for auto_money_gain
        // and auto_home_gain. Both read the owner, not the local player, so a co-op client checks the host's autonomous ship correctly.
        // In co-op it runs in the sim ("mp auto_mark") some ticks after the line: wait for auto_marked == 1 before reading the gains.
        Autotest.Commands["auto_mark"] = _ =>
        {
            _moneyMark = OwnerMoney();
            _homeMark = OwnerHomeSteel();
            _marked = TestShip().UniqueID;
        };
        Autotest.Fields["auto_marked"] = () => _marked == TestShip().UniqueID ? 1 : 0;
        Autotest.Fields["auto_money_gain"] = () => OwnerMoney() - _moneyMark;
        Autotest.Fields["auto_home_gain"] = () => OwnerHomeSteel() - _homeMark;
        // auto_drain: empties the battery of every part of the test ship except reactors (setup: the targeted reactor must refill them).
        Autotest.Commands["auto_drain"] = _ =>
        {
            foreach (var rs in TestShip().Parts.Where(p => p.Rules.ID != Autonomous.TargetedReactorId).SelectMany(p => p.Components.OfType<ResourceStorage>())
                         .Where(rs => rs.ResourceType.ToString() == "battery"))
                rs.Resources = 0;
        };
        // auto_loot <resource> <amount> <count> <distance>: drops count loot items of amount units in a ring around the test ship, distance from its hull.
        Autotest.Commands["auto_loot"] = arg =>
        {
            var p = Args(arg, 4);
            var ship = TestShip();
            var type = Resource(p[0]);
            int amount = int.Parse(p[1], CultureInfo.InvariantCulture), count = int.Parse(p[2], CultureInfo.InvariantCulture);
            float dist = float.Parse(p[3], CultureInfo.InvariantCulture) + ship.WorldBoundingRadius;
            for (int i = 0; i < count; i++)
            {
                var at = ship.DetWorldCenter + Vector2.FromAngle(2f * MathF.PI * i / count) * dist;
                Sim().Nuggets.AddNugget(new Nugget(type, amount, at, Direction.Zero));
            }
            Log.Info($"AUTOTEST auto_loot {count} x {amount} {p[0]} at {dist:0} from {ship.Metadata.PlainShipName}");
        };
        // auto_cargo <resource> <amount>: puts cargo straight into the test ship's storage bays (setup only).
        Autotest.Commands["auto_cargo"] = arg =>
        {
            var p = Args(arg, 2);
            var ship = TestShip();
            var type = Resource(p[0]);
            int left = int.Parse(p[1], CultureInfo.InvariantCulture);
            foreach (Cosmoteer.Ships.Resources.IResourceSink t in FlexResourceGrid.GetAllOnShip(ship).SelectMany(g => g))
            {
                int room = t.GetRemainingCapacity(type, allowPreferredTypeMismatch: false);
                if (room > 0) left -= t.ReceiveResources(type, Math.Min(room, left), false, false, false);
                if (left <= 0) break;
            }
            Log.Info($"AUTOTEST auto_cargo {p[0]}: {left} did not fit");
        };
        // auto_toggle <part id> <toggle id> <mode>: sets a part setting on every such part of the test ship, like a click (real player action).
        Autotest.Commands["auto_toggle"] = arg =>
        {
            var p = Args(arg, 3);
            var toggles = TestShip().Parts.Where(part => part.Rules.ID.ToString() == p[0]).SelectMany(part => part.Components.OfType<PartUIToggle>())
                .Where(t => t.Rules.ToggleID.ToString() == p[1]).ToDictionary(t => t, _ => int.Parse(p[2], CultureInfo.InvariantCulture));
            if (toggles.Count == 0) throw new InvalidOperationException($"no {p[0]} with toggle {p[1]} on {_shipName}");
            Sim().RequireGame().NetManager.EnqueueInput(new MPSetUITogglesInput(toggles));
        };
        // auto_order: a player move order to the test ship, sent like a right click (real player action). The sim must refuse it.
        Autotest.Commands["auto_order"] = _ =>
        {
            var ship = TestShip();
            var to = ship.DetWorldCenter + new Vector2(500f, 0f);
            var move = new MoveCommand(ship, ship.Commands.AllocLocalCommandID(), to, null, CollisionAvoidanceFlags.All, isTentative: false);
            Sim().RequireGame().NetManager.EnqueueInput(new MPEnqueueCommandsInput(move, append: false, playLocalSound: false));
        };
        // auto_kill <part id>: destroys the first such part of the test ship (setup for the ownership rules).
        Autotest.Commands["auto_kill"] = arg =>
        {
            var part = TestShip().Parts.Where(p => p.Rules.ID.ToString() == arg).OrderBy(p => p.Location.X).ThenBy(p => p.Location.Y).FirstOrDefault()
                ?? throw new InvalidOperationException($"no {arg} on {_shipName}");
            part.OnHit(part.Health * 4 + 100000);
            Log.Info($"AUTOTEST auto_kill {arg} at {part.Location}");
        };
        // auto_wreck <x>,<y>: a wreck (a junk copy of the first own ship, no crew) at this offset from the test ship.
        Autotest.Commands["auto_wreck"] = arg =>
        {
            var sim = Sim();
            var home = Home();
            using var stream = new MemoryStream();
            home.SaveDesign(stream);
            var wreck = Ship.Load(stream.ToArray(), "Test wreck", -3, spawnWithCrew: false, randSeed: 4242UL);
            wreck.WorldCenter = TestShip().DetWorldCenter + Offset(arg);
            sim.Ships.Add(wreck);
            Log.Info($"AUTOTEST auto_wreck id={wreck.UniqueID} junk={wreck.IsJunk} parts={wreck.Parts.Count}");
        };
        // auto_rock <doodad> <distance> [spin]: a live asteroid doodad (for example cosmoteer.asteroid_iron_l) at a clear spot at least this
        // far from the test ship, turning at spin radians per second. Sim change: use "mp" in co-op.
        Autotest.Commands["auto_rock"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length is < 2 or > 3) throw new InvalidOperationException("auto_rock needs: doodad distance [spin]");
            var sim = Sim();
            var at = Autotest.ClearSpotNear(sim, TestShip().DetWorldCenter, float.Parse(p[1], CultureInfo.InvariantCulture), 60f);
            var spawner = new StasisDoodadSpawner(sim, new ID<DoodadRules>(p[0]), at, Direction.Zero, 77UL, null);
            // Same reservation as SimStasisManager.AddStasisSpawner, so the asteroid gets a known, deterministic ID.
            while (spawner.ReservedObjectID == default || !sim.IsObjectIDAvailable(spawner.ReservedObjectID)) spawner.ReservedObjectID = ObjectID.NewRandom(sim.Rand);
            spawner.Spawn(sim);
            var rock = sim.Ships[spawner.ReservedObjectID] ?? throw new InvalidOperationException($"{p[0]} did not spawn a live asteroid");
            if (p.Length == 3 && rock.Physics.Body is { } body) body.AngularVelocity = float.Parse(p[2], CultureInfo.InvariantCulture);
            _rock = rock.UniqueID;
            Log.Info($"AUTOTEST auto_rock {p[0]} id={rock.UniqueID} parts={rock.Parts.Count} radius={rock.WorldBoundingRadius:0} edge={HunterOrders.EdgeDistance(TestShip(), rock):0}");
        };
        // 1 when the asteroid that auto_rock made last is the nearest one with ore for the test ship (the one that Mine picks).
        Autotest.Fields["auto_rock_nearest"] = () => AutonomyWorld.NearestAsteroid(TestShip(), CargoSettings.Default)?.Id == _rock ? 1 : 0;
        // Salvage turrets (mining lasers) of the test ship that have a target now.
        Autotest.Fields["auto_laser_targets"] = () => TestShip().Weapons.Count(w => w is TurretWeapon { Target: not null } tw && AutonomyWorld.IsSalvageTurret(tw));
    }

    static string[] Args(string arg, int n)
    {
        var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != n) throw new InvalidOperationException($"expected {n} arguments, got '{arg}'");
        return p;
    }

    static ID<ResourceRules> Resource(string id) =>
        GameApp.Rules.HasResourceRules(new ID<ResourceRules>(id)) ? new ID<ResourceRules>(id) : throw new InvalidOperationException($"unknown resource '{id}'");

    static Vector2 Offset(string xy)
    {
        var p = xy.Split(',');
        return new Vector2(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture));
    }

    static void Build(string arg)
    {
        var tokens = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3) throw new InvalidOperationException("auto_build needs: name where part@x,y[,rot] ...");
        var sim = Sim();
        var home = Home();
        var ship = new Ship(home.Rules, tokens[0], home.Metadata.PlayerIndex, randSeed: 9001UL);
        using (var sui = ShipUpdateInfo.Alloc(ship))
        {
            foreach (string token in tokens[2..])
            {
                int at = token.IndexOf('@');
                if (at <= 0) throw new InvalidOperationException($"bad part '{token}'");
                var rules = home.Rules.PartsByID[new ID<PartRules>(token[..at])];
                var nums = token[(at + 1)..].Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                if (nums.Length is < 2 or > 3) throw new InvalidOperationException($"bad place '{token}'");
                if (!sui.TryAddBlueprintPart(rules, new IntVector2(nums[0], nums[1]), nums.Length > 2 ? nums[2] : 0, flipX: false, mirrorPhysical: false,
                        allowOverwrite: false, addDoors: false, hireCrew: false, out var reasons, force: true))
                    throw new InvalidOperationException($"cannot add {token}: {reasons}");
            }
            sui.Apply();
        }
        ship.MatchPhysicalToBlueprints();
        Vector2 near = tokens[1] switch
        {
            "player" => home.DetWorldCenter,
            "station" => AutonomyWorld.Stations(home).FirstOrDefault(s => s.Live is not null).Live?.DetWorldCenter
                         ?? throw new InvalidOperationException("no live trading station"),
            // 1,500 from the first own ship, on the far side from the nearest station: out of hail range.
            "away" => AutonomyWorld.Stations(home).MinBy(st => st.Center.DistanceTo(home.DetWorldCenter)) is { } st
                ? home.DetWorldCenter + Vector2.FromAngle(st.Center.DirectionTo(home.DetWorldCenter)) * 1500f
                : home.DetWorldCenter + new Vector2(1500f, 0f),
            // The nearest live asteroid with ore deposits (a plain rock gives nothing to mine), by distance, then ID.
            "asteroid" => sim.Ships.AllShips.Where(s => s.Rules.IsAsteroid && s.Parts.Any(p => p.Rules.Resources.Length > 0))
                              .OrderBy(s => s.DetWorldCenter.DistanceTo(home.DetWorldCenter)).ThenBy(s => s.UniqueID).FirstOrDefault()?.DetWorldCenter
                          ?? throw new InvalidOperationException("no live asteroid with ore"),
            _ => home.DetWorldCenter + Offset(tokens[1]),
        };
        ship.WorldCenter = Autotest.ClearSpotNear(sim, near, 400f, ship.WorldBoundingRadius);
        sim.Ships.Add(ship);
        ship.InitializeToMax();
        _shipName = tokens[0];
        Log.Info($"AUTOTEST auto_build {tokens[0]} id={ship.UniqueID} parts={ship.Parts.Count} crew={ship.Crew.Assigned.Count} " +
                 $"autonomous={Autonomous.IsAutonomous(ship)} human={ship.IsHumanPlayerShip} at {ship.WorldCenter}");
    }
}

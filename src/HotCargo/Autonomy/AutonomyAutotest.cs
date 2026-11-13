using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Crew;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Ships.Parts.UI;
using Cosmoteer.Simulation;
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

    internal static void Register()
    {
        // auto_build <name> <where> <part>@<x>,<y>[,<rot>] ...: builds a ship of the acting player from vanilla and mod parts, complete,
        // with full storages and batteries. where: player (next to the first own ship), station (next to the nearest trading station),
        // asteroid (next to the nearest asteroid), or x,y offset from the first own ship. The test fields then read this ship.
        Autotest.Commands["auto_build"] = arg => Build(arg);
        Autotest.Commands["auto_ship"] = arg => _shipName = arg;
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
        // auto_mark: remembers the money and the first own ship's steel, for auto_money_gain and auto_home_gain.
        Autotest.Commands["auto_mark"] = _ =>
        {
            _moneyMark = Sim().RequireGame().GetPlayerState(Autotest.ActingPlayerIndex, deterministic: true).Money;
            _homeMark = Home().Resources.GetResourceTotal(new ID<ResourceRules>("steel"));
        };
        Autotest.Fields["auto_money_gain"] = () => Sim().RequireGame().GetPlayerState(Autotest.ActingPlayerIndex, deterministic: true).Money - _moneyMark;
        Autotest.Fields["auto_home_gain"] = () => Home().Resources.GetResourceTotal(new ID<ResourceRules>("steel")) - _homeMark;
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
            "asteroid" => sim.Ships.AllShips.Where(s => s.Rules.IsAsteroid && s.Parts.Count > 0).MinBy(s => s.DetWorldCenter.DistanceTo(home.DetWorldCenter))?.DetWorldCenter
                          ?? throw new InvalidOperationException("no live asteroid"),
            _ => home.DetWorldCenter + Offset(tokens[1]),
        };
        ship.WorldCenter = Autotest.ClearSpotNear(sim, near, 400f, ship.WorldBoundingRadius);
        sim.Ships.Add(ship);
        ship.InitializeToMax();
        _shipName = tokens[0];
        Log.Info($"AUTOTEST auto_build {tokens[0]} id={ship.UniqueID} parts={ship.Parts.Count} crew={ship.Crew.Assigned.Count} " +
                 $"autonomous={Autonomous.IsAutonomous(ship)} human={ship.IsHumanPlayerShip} at {ship.WorldCenter}");
        Autotest.Wait(1);
    }
}

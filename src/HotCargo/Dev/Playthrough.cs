using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Modes.Career.TechTree;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Resources;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Tech;

namespace HotCargo.Dev;

/// <summary>
/// Spec 1004 to 1006: test commands for a playthrough without cheats (tests/autotest/core-playthrough.txt).
/// Each command sends the same input as one player click: a right click order, a comms button, a trade window transfer,
/// a build confirmation. None of them moves a ship, changes health or gives money or tech. Single player.
/// </summary>
public static class Playthrough
{
    static Mission? _mission;
    static Func<Circle?>? _target;

    internal static void Register()
    {
        // pt_goto station|mission|relay: a right click order. The ship flies on its own; "waitfor pt_near == 1" waits for the arrival.
        Autotest.Commands["pt_goto"] = arg =>
        {
            var ship = TechAutotest.PlayerShip();
            _target = arg switch
            {
                "station" => PickStation(ship),
                "mission" => () => (_mission ?? throw new InvalidOperationException("no accepted mission")).GetClosestObjective(out _)?.GetAreaLocation(Game()),
                "relay" => () => new Circle(Relay().DetWorldCenter, 600f),
                _ => throw new InvalidOperationException("pt_goto needs: station | mission | relay"),
            };
            var to = _target() ?? throw new InvalidOperationException($"no location for {arg}");
            // A point on the near edge of the target, so the ship stops next to a station instead of on it.
            var dir = (ship.DetWorldCenter - to.Center).Normalize();
            var at = to.Center + dir * (to.Radius + ship.WorldBoundingRadius + 100f);
            if (arg == "mission") at = to.Center;
            var move = new MoveCommand(ship, ship.Commands.AllocLocalCommandID(), at, null, CollisionAvoidanceFlags.All, isTentative: false);
            Game().NetManager.EnqueueInput(new MPEnqueueCommandsInput(move, append: false, playLocalSound: false));
            Log.Info($"AUTOTEST playthrough: {ship.Name} ordered to {arg} at {at}, {ship.DetWorldCenter.DistanceTo(at):0} away");
        };
        // 1 when the player ship is within 500 of the pt_goto target.
        Autotest.Fields["pt_near"] = () =>
            _target?.Invoke() is { } c && TechAutotest.PlayerShip().DetWorldBoundingCircle.DistanceTo(in c) < 500f ? 1 : 0;
        Autotest.Commands["pt_accept"] = _ => Accept();
        // 0 none, 1 in progress, 2 completed, 3 failed or expired.
        Autotest.Fields["pt_mission"] = () => _mission?.Completion switch
        {
            null => 0,
            CompletionState.InProgress => 1,
            CompletionState.Completed => 2,
            _ => 3,
        };
        // pt_unlock <tech id>: the unlock button of the station's techs tab.
        Autotest.Commands["pt_unlock"] = arg =>
            Game().NetManager.EnqueueInput(new CommTechsTab.MPBuyTechInput(new ID<TechRules>(arg), Station()));
        Autotest.Fields["pt_unlocked"] = () => Autotest.RequireRun().Mode.Techs.IsTechUnlocked(new ID<TechRules>("cosmoteer.hyperdrive_small")) ? 1 : 0;
        // pt_buy <resource id> <amount>: the trade window, station to ship.
        Autotest.Commands["pt_buy"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new InvalidOperationException("pt_buy needs: resource amount");
            Buy(new ID<ResourceRules>(parts[0]), int.Parse(parts[1], CultureInfo.InvariantCulture));
        };
        Autotest.Fields["pt_steel"] = () => Stored("steel");
        Autotest.Fields["pt_coil"] = () => Stored("coil");
        Autotest.Fields["pt_hyperium"] = () => Stored("hyperium");
        // pt_build <part id>: a blueprint part on a free spot next to the hull, confirmed like the build tool's button. Crew build it.
        Autotest.Commands["pt_build"] = arg =>
        {
            var ship = TechAutotest.PlayerShip();
            var rules = ship.Rules.PartsByID[new ID<PartRules>(arg)];
            var at = StartingVault.PlaceOutside(ship, rules, requireCrewPath: true, apply: sui =>
                Game().NetManager.EnqueueInput(new MPUpdateShipInput(sui,
                    UpdateShipFlags.Build | UpdateShipFlags.DestroyBlocking | UpdateShipFlags.ClearDestroyed | UpdateShipFlags.ApplyBlueprintMetadata)));
            Log.Info($"AUTOTEST playthrough: blueprint {arg} at {at}");
        };
        Autotest.Fields["pt_drives"] = () => TechAutotest.PlayerShip().Ftl.HasFtlDrives ? 1 : 0;
        Autotest.Fields["pt_fuel"] = () => TechAutotest.PlayerShip().Ftl.GetTotalFuel();
        // pt_relayjump: a right click on the relay toward the first unvisited neighbor. The drives spin up on the relay.
        Autotest.Commands["pt_relayjump"] = _ =>
        {
            var ship = TechAutotest.PlayerShip();
            var gate = Relay().PhysicalShip ?? throw new InvalidOperationException("the relay is in stasis; fly closer first");
            var jump = new FtlGateJumpCommand(ship, ship.Commands.AllocLocalCommandID(), gate, CollisionAvoidanceFlags.All, isTentative: false);
            Game().NetManager.EnqueueInput(new MPEnqueueCommandsInput(jump, append: false, playLocalSound: false));
        };
        Autotest.Fields["pt_ready"] = () => TechAutotest.PlayerShip().Ftl.IsReadyToJump ? 1 : 0;
        // pt_fight <timeout seconds>: right clicks the mission's nearest live target ship, again every 20 seconds as it moves,
        // until no fight step is left: the mission ended, or only a step such as ReceiveRewards is left (pt_collect).
        // A target in stasis or out of sight gets a move order to the objective area instead.
        Autotest.Commands["pt_fight"] = arg =>
        {
            double next = 0;
            Autotest.WaitFor(() =>
            {
                var m = _mission ?? throw new InvalidOperationException("no accepted mission");
                if (m.Completion != CompletionState.InProgress || !FightLeft(m)) return true;
                double now = Halfling.App.Clock.Time.Seconds;
                if (now >= next)
                {
                    next = now + 20;
                    Engage(m);
                }
                return false;
            }, double.Parse(arg, CultureInfo.InvariantCulture), "the mission to end");
        };
        // pt_collect: the "receive payment" button in the comms window of the picked station (CommMissionsTab.MPMissionPaymentInput).
        // A vanilla bounty ends with a ReceiveRewards step after its target dies.
        Autotest.Commands["pt_collect"] = _ =>
            Game().NetManager.EnqueueInput(new CommMissionsTab.MPMissionPaymentInput(Station(), _mission ?? throw new InvalidOperationException("no accepted mission")));
        Autotest.Commands["pt_status"] = arg => Status(arg);
        // pt_watch <real seconds>: a status line at that interval, so a failed wait shows what the ship did. It only reads.
        Autotest.Commands["pt_watch"] = arg =>
        {
            double every = double.Parse(arg, CultureInfo.InvariantCulture), next = 0;
            Halfling.App.Director.FrameEnded += (_, _) =>
            {
                double now = Halfling.App.Clock.Time.Seconds;
                if (now < next || Run.Current is null) return;
                next = now + every;
                Errors.Guard("playthrough watch", () => Status("watch"));
            };
        };
    }

    /// <summary>
    /// One right click: an attack order on the nearest live target ship of the mission. A target in stasis (it fled out of sight) shows
    /// where its landmark is discovered, and a player who focuses the objective line flies there (vanilla ObjectiveTargetShip.OnFocused).
    /// Else a move order to the mission area.
    /// </summary>
    static void Engage(Mission m)
    {
        var ship = TechAutotest.PlayerShip();
        var targets = new List<DefeatShipsObjective>();
        m.GetObjectives(targets, activeOnly: true);
        var targetShips = targets.SelectMany(o => o.TargetShips).ToList();
        var target = targetShips.Select(t => t.TargetShip)
            .Where(t => t is { IsJunk: false } && t.Sim == ship.Sim).MinBy(t => t!.DetWorldCenter.DistanceTo(ship.DetWorldCenter));
        var landmark = targetShips.Select(t => t.TargetSpawner).OfType<Cosmoteer.Simulation.Stasis.StasisSpawner>()
            .Where(sp => StasisLandmark.GetLandmark(sp) is { IsDiscovered: true }).Select(sp => (Vector2?)sp.Location).FirstOrDefault();
        Command order;
        if (target is not null)
            order = new AttackCommand(ship, ship.Commands.AllocLocalCommandID(), target, CollisionAvoidanceFlags.All, isTentative: false, isConfirmed: true);
        else if (landmark is { } seen)
            order = new MoveCommand(ship, ship.Commands.AllocLocalCommandID(), seen, null, CollisionAvoidanceFlags.All, isTentative: false);
        else if (m.GetClosestObjective(out _)?.GetAreaLocation(Game()) is { } area)
            order = new MoveCommand(ship, ship.Commands.AllocLocalCommandID(), area.Center, null, CollisionAvoidanceFlags.All, isTentative: false);
        else throw new InvalidOperationException($"mission {m.MetatypeID} has no target ship and no area");
        Game().NetManager.EnqueueInput(new MPEnqueueCommandsInput(order, append: false, playLocalSound: false));
        string where = target is not null ? $"on {target.Metadata.PlainShipName}" : landmark is not null ? "to the target's landmark" : "to the mission area";
        Log.Info($"AUTOTEST playthrough: {order.GetType().Name} {where}");
    }

    /// <summary>True while a fight step of the mission is not over, also one that a later step of a sequence holds.</summary>
    static bool FightLeft(Mission m)
    {
        var fights = new List<DefeatShipsObjective>();
        m.GetObjectives(fights);
        return fights.Any(o => o.Completion == CompletionState.InProgress);
    }

    static Cosmoteer.Game.GameRoot Game() => Autotest.RequireRun().Game;

    static ObjectID? _stationId;

    /// <summary>The station that pt_goto station picked. Far stations are in stasis, so the ship exists only near it.</summary>
    static MetaSpaceStation PickedStation()
    {
        var sim = Game().Sim;
        return SpaceStation.GetStations(sim, onlyOperating: false).Where(s => _stationId is { } id && s.StationID.Equals(id)).Cast<MetaSpaceStation?>().FirstOrDefault()
            ?? throw new InvalidOperationException("no picked station; run pt_goto station first");
    }

    static Ship Station() => PickedStation().PhysicalShip ?? throw new InvalidOperationException("the picked station is in stasis; fly closer first");

    /// <summary>Picks the nearest operating station that offers missions, the one a person flies to first.</summary>
    static Func<Circle?> PickStation(Ship ship)
    {
        var sim = Game().Sim;
        var station = SpaceStation.GetStations(sim, onlyOperating: true).Where(s => s.GetProvidedMissions() is not null)
            .OrderBy(s => s.DetWorldCenter.DistanceTo(ship.DetWorldCenter)).Cast<MetaSpaceStation?>().FirstOrDefault()
            ?? throw new InvalidOperationException("no operating station with missions in this system");
        _stationId = station.StationID;
        return () => PickedStation() is var s ? new Circle(s.DetWorldCenter, s.PhysicalShip?.WorldBoundingRadius ?? 400f) : null;
    }

    static int Stored(string resource) =>
        TechAutotest.PlayerShip().Resources.GetResourceTotal(new ID<ResourceRules>(resource), includeStored: true, includeCarried: false);

    static MetaFtlGate Relay()
    {
        var run = Autotest.RequireRun();
        var next = run.Game.CurrentMapNode.Connections.FirstOrDefault(n => !run.State.VisitedNodes.Contains(n.UniqueID))
            ?? throw new InvalidOperationException("no unvisited neighbor system");
        return FtlGate.GetGateForDestination(run.Game.Sim, next) ?? throw new InvalidOperationException($"no relay toward {next.Name}");
    }

    /// <summary>
    /// The accept button for one offer of the station: a local mission with enemies to fight that the player can afford,
    /// the lowest tier first, then the nearest. A person picks a fight that the starting ship can win.
    /// </summary>
    static void Accept()
    {
        var run = Autotest.RequireRun();
        var sim = run.Game.Sim;
        var station = Station();
        var list = SpaceStation.GetStations(sim, onlyOperating: false).Where(s => s.PhysicalShip == station).Select(s => s.GetProvidedMissions()).FirstOrDefault()
            ?? throw new InvalidOperationException($"{station.Name} offers no missions");
        var enemies = new HashSet<int>();
        bool Fight(Mission m)
        {
            enemies.Clear();
            m.GetEnemyAllegiances(sim, enemies);
            return enemies.Count > 0;
        }
        var pick = list
            .Where(m => m.IsLocal && m.CanAccept(sim) && Fight(m) && m.GetClosestObjective(out _) is not null
                && Contracts.ContractBook.CanPay(run, m, run.Game.LocalPlayerIndex))
            .OrderBy(m => m.GetTierRange(sim)?.Max ?? 0).ThenBy(m => m.GetDistanceToClosestObjective())
            .FirstOrDefault() ?? throw new InvalidOperationException(
                $"{station.Name} has no local fight to accept; offers: {string.Join(", ", list.Select(m => $"{m.MetatypeID} local={m.IsLocal} accept={m.CanAccept(sim)}"))}");
        _mission = pick;
        run.Game.NetManager.EnqueueInput(new CommMissionsTab.MPAcceptMissionInput(CommMissionsTab.MissionSourceType.Ship, station, pick));
        Log.Info($"AUTOTEST playthrough: accepted {pick.MetatypeID} {pick.MissionID} '{Events.EventView.Plain(pick.Name.Text)}' at {station.Name}, {pick.GetDistanceToClosestObjective():0} away");
    }

    /// <summary>The trade window's transfer from the station to the ship (CrewAndResourceTransferWindow.DoTransfer).</summary>
    static void Buy(ID<ResourceRules> resource, int units)
    {
        var game = Game();
        var ship = TechAutotest.PlayerShip();
        var station = Station();
        var sources = new List<(IResourceSource Source, int AnticipatedResources, float Distance)>();
        station.Resources.SearchAllAvailableResourceSources(ship, resource, MPValueType.Displayed, sources);
        var sinks = new List<(IResourceSink Sink, int AnticipatedCapacity, float Distance)>();
        ship.Resources.SearchAllAvailableResourceSinks(station, resource, MPValueType.Displayed, sinks);
        int left = units;
        foreach (var (source, available, _) in sources)
        {
            int fromSource = available;
            for (int i = 0; i < sinks.Count && left > 0 && fromSource > 0; i++)
            {
                var (sink, room, dist) = sinks[i];
                int n = Math.Min(left, Math.Min(fromSource, room));
                if (n <= 0) continue;
                int price = game.Sim.Mode.GetResourceBuyCost(resource, source.Ship?.Metadata.PlayerIndex);
                var id = MPMoveResourcesInput.GetJobOwner(source, sink, game.LocalPlayerIndex).Jobs.AllocLocalJobID();
                game.NetManager.EnqueueInput(new MPMoveResourcesInput(source, sink, n, id, sticky: false, TradeType.Buy, price));
                sinks[i] = (sink, room - n, dist);
                left -= n;
                fromSource -= n;
            }
        }
        Log.Info($"AUTOTEST playthrough: buy {resource}: queued {units - left} of {units} ({sources.Count} station sources, {sinks.Count} ship sinks)");
    }

    static IEnumerable<ObjectiveTargetShip> TargetShips()
    {
        if (_mission is null) return [];
        var objectives = new List<DefeatShipsObjective>();
        _mission.GetObjectives(objectives, activeOnly: false);
        return objectives.SelectMany(o => o.TargetShips);
    }

    /// <summary>Logs only. A killed target has id 0 and alive=False: vanilla clears the ship ID (ObjectiveTargetShip.OnTargetShipChanged).</summary>
    static string TargetState(SimRoot sim) => string.Join("; ", TargetShips().Select(t =>
        $"id={t.ShipID} node={(t.MapNodeID.Equals(sim.MapNode.UniqueID) ? "here" : t.MapNodeID.ToString())} alive={t.IsAlive} live={t.TargetShip is not null} " +
        $"spawner={t.TargetSpawner?.GetType().Name ?? "none"} stasis={sim.Stasis[t.ShipID]?.GetType().Name ?? "none"} at={t.Location?.ToString() ?? "none"}"));

    static void Status(string label)
    {
        var run = Autotest.RequireRun();
        var ship = TechAutotest.PlayerShip();
        int hurt = ship.Parts.Count(p => p.Health < p.Rules.MaxHealth);
        Log.Info($"AUTOTEST PLAYTHROUGH {label}: sector={run.State.SectorIndex} tech={run.State.FleetTech} heat={run.State.Heat:0.0} " +
                 $"money={run.Game.LocalPlayerState.Money} parts={ship.Parts.Count} hurt={hurt} drives={ship.Ftl.HasFtlDrives} fuel={ship.Ftl.GetTotalFuel()} " +
                 $"mission={_mission?.MetatypeID} {_mission?.Completion} target={(_mission?.GetClosestObjective(out float d) is null ? "none" : $"{d:0}")} " +
                 $"hostiles={run.Game.Sim.Ships.Count(s => s.IsEnemiesWith(ship) && !s.IsJunk)} " +
                 $"nearest hostile={run.Game.Sim.Ships.Where(s => s.IsEnemiesWith(ship) && !s.IsJunk).Select(s => s.DetWorldCenter.DistanceTo(ship.DetWorldCenter)).DefaultIfEmpty(-1).Min():0} " +
                 $"order={ship.Commands.ConfirmedCommands.FirstOrDefault()?.GetType().Name} targets=[{TargetState(run.Game.Sim)}]");
    }
}

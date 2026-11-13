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
        // pt_goto station|mission|relay <timeout seconds>: a right click order, again every 20 seconds until the ship is near (pt_near).
        // A far station is in stasis, so its size is a guess; a person clicks again when it is in view.
        Autotest.Commands["pt_goto"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new InvalidOperationException("pt_goto needs: station|mission|relay <timeout seconds>");
            string where = parts[0];
            _target = where switch
            {
                "station" => PickStation(TechAutotest.PlayerShip()),
                "mission" => () => (_mission ?? throw new InvalidOperationException("no accepted mission")).GetClosestObjective(out _)?.GetAreaLocation(Game()),
                "relay" => () => new Circle(Relay().DetWorldCenter, 600f),
                _ => throw new InvalidOperationException("pt_goto needs: station|mission|relay <timeout seconds>"),
            };
            double next = 0;
            Autotest.WaitFor(() =>
            {
                if (Near()) return true;
                double now = Halfling.App.Clock.Time.Seconds;
                if (now >= next)
                {
                    next = now + 20;
                    GoTo(where);
                }
                return false;
            }, double.Parse(parts[1], CultureInfo.InvariantCulture), $"the ship to reach the {where}");
        };
        Autotest.Fields["pt_near"] = () => Near() ? 1 : 0;
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
        // pt_buy <timeout seconds> <resource id> <total> [<resource id> <total> ...]: the trade window, station to ship, until the ship
        // stores each total. Vanilla cancels and refunds a transfer that the crew did not finish, for example when the ship chases an
        // attacker out of trade range. A person then flies back and buys the rest: every 20 seconds, each missing resource with no
        // transfer on its way is bought again, after a move order back to the picked station if the ship is not near it.
        Autotest.Commands["pt_buy"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || parts.Length % 2 == 0) throw new InvalidOperationException("pt_buy needs: timeout resource total [resource total ...]");
            var wants = parts.Skip(1).Chunk(2).Select(p => (Resource: new ID<ResourceRules>(p[0]), Total: int.Parse(p[1], CultureInfo.InvariantCulture))).ToList();
            _target = _station ?? throw new InvalidOperationException("no picked station; run pt_goto station first");
            double next = 0;
            Autotest.WaitFor(() =>
            {
                var missing = wants.Where(w => Stored(w.Resource) < w.Total).ToList();
                if (missing.Count == 0) return true;
                double now = Halfling.App.Clock.Time.Seconds;
                if (now < next) return false;
                next = now + 20;
                if (!Near()) GoTo("station");
                else foreach (var w in missing.Where(w => !BuyPending(w.Resource))) Buy(w.Resource, w.Total - Stored(w.Resource));
                return false;
            }, double.Parse(parts[0], CultureInfo.InvariantCulture), $"the ship to store {string.Join(", ", wants.Select(w => $"{w.Total} {w.Resource}"))}");
        };
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
                // No acting ship during a jump (between systems) or after the ship is lost: nothing to report.
                if (now < next || Run.Current is not { Game.Sim: { } sim } || Autotest.ActingShip(sim) is null) return;
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

    /// <summary>One move order to the pt_goto target: a point on its near edge, so the ship stops next to a station instead of on it.</summary>
    static void GoTo(string where)
    {
        var ship = TechAutotest.PlayerShip();
        var to = _target?.Invoke() ?? throw new InvalidOperationException($"no location for {where}");
        var dir = (ship.DetWorldCenter - to.Center).Normalize();
        var at = where == "mission" ? to.Center : to.Center + dir * (to.Radius + ship.WorldBoundingRadius + 100f);
        var move = new MoveCommand(ship, ship.Commands.AllocLocalCommandID(), at, null, CollisionAvoidanceFlags.All, isTentative: false);
        Game().NetManager.EnqueueInput(new MPEnqueueCommandsInput(move, append: false, playLocalSound: false));
        Log.Info($"AUTOTEST playthrough: {ship.Name} ordered to {where} at {at}, {ship.DetWorldCenter.DistanceTo(at):0} away");
    }

    /// <summary>
    /// The player ship is close enough to trade with the pt_goto target. Vanilla drops a trade transfer without a message when the ship
    /// edges are farther apart than LoseHomeShipWarningDistance (MPMoveResourcesInput.Execute). 100 less as a margin; GoTo stops 100 from the edge.
    /// </summary>
    static bool Near() => _target?.Invoke() is { } c
        && TechAutotest.PlayerShip().DetWorldBoundingCircle.DistanceTo(in c) < GameApp.Rules.Crew.LoseHomeShipWarningDistance - 100f;

    /// <summary>True while a fight step of the mission is not over, also one that a later step of a sequence holds.</summary>
    static bool FightLeft(Mission m)
    {
        var fights = new List<DefeatShipsObjective>();
        m.GetObjectives(fights);
        return fights.Any(o => o.Completion == CompletionState.InProgress);
    }

    static Cosmoteer.Game.GameRoot Game() => Autotest.RequireRun().Game;

    static ObjectID? _stationId;
    static Func<Circle?>? _station;

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
        // A station in stasis has no hull to measure: 400 is a guess until it is live.
        return _station = () => PickedStation() is var s ? s.PhysicalShip?.DetWorldBoundingCircle ?? new Circle(s.DetWorldCenter, 400f) : null;
    }

    static int Stored(string resource) => Stored(new ID<ResourceRules>(resource));

    static int Stored(ID<ResourceRules> resource) =>
        TechAutotest.PlayerShip().Resources.GetResourceTotal(resource, includeStored: true, includeCarried: false);

    /// <summary>A buy of this resource is still on its way: the player ship owns the transfer job (MPMoveResourcesInput.GetJobOwner).</summary>
    static bool BuyPending(ID<ResourceRules> resource) =>
        TechAutotest.PlayerShip().Resources.ManualTransferJobs.Any(j => j.TradeType == TradeType.Buy && j.ResourceType == resource);

    static MetaFtlGate Relay()
    {
        var run = Autotest.RequireRun();
        var next = run.Game.CurrentMapNode.Connections.FirstOrDefault(n => !run.State.VisitedNodes.Contains(n.UniqueID))
            ?? throw new InvalidOperationException("no unvisited neighbor system");
        return FtlGate.GetGateForDestination(run.Game.Sim, next) ?? throw new InvalidOperationException($"no relay toward {next.Name}");
    }

    /// <summary>
    /// The accept button for one offer of the station: a local mission with enemies to fight that the player can afford.
    /// A person picks the fight with the lowest threat: the lowest tier, then the fewest targets that use the Disable tactic,
    /// then the nearest. A Disable target shoots the thrusters and control parts of the starter ship, which carries no steel
    /// to rebuild them (spec 1004). A base has no personality, so a high-tier base never counts as safe.
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
        var fights = list
            .Where(m => m.IsLocal && m.CanAccept(sim) && Fight(m) && m.GetClosestObjective(out _) is not null
                && Contracts.ContractBook.CanPay(run, m, run.Game.LocalPlayerIndex))
            .Select(m => (Mission: m, Disablers: Disablers(run, sim, m)))
            .OrderBy(f => f.Mission.GetTierRange(sim)?.Max ?? 0).ThenBy(f => f.Disablers).ThenBy(f => f.Mission.GetDistanceToClosestObjective())
            .ToList();
        Log.Info($"AUTOTEST playthrough: fights offered: {string.Join(", ", fights.Select(f => $"{f.Mission.MetatypeID} {f.Mission.MissionID} tier={f.Mission.GetTierRange(sim)?.Max} disable targets={f.Disablers} [{Targets(sim, f.Mission)}]"))}");
        var pick = fights.Select(f => f.Mission).FirstOrDefault() ?? throw new InvalidOperationException(
                $"{station.Name} has no local fight to accept; offers: {string.Join(", ", list.Select(m => $"{m.MetatypeID} local={m.IsLocal} accept={m.CanAccept(sim)}"))}");
        _mission = pick;
        run.Game.NetManager.EnqueueInput(new CommMissionsTab.MPAcceptMissionInput(CommMissionsTab.MissionSourceType.Ship, station, pick));
        Log.Info($"AUTOTEST playthrough: accepted {pick.MetatypeID} {pick.MissionID} '{Events.EventView.Plain(pick.Name.Text)}' at {station.Name}, {pick.GetDistanceToClosestObjective():0} away");
    }

    /// <summary>
    /// The targets of a mission that will use the Disable tactic. Spec 363: the build fit of Disable is 1, so the personality roll and
    /// jitter decide it, and both come from the group and the ship ID. That works for a target in stasis too.
    /// </summary>
    /// <summary>Logs only: each target with its fame, personality and jittered trait weights.</summary>
    static string Targets(SimRoot sim, Mission m)
    {
        var objectives = new List<DefeatShipsObjective>();
        m.GetObjectives(objectives);
        return string.Join(" ", objectives.SelectMany(o => o.TargetShips).Select(t =>
        {
            t.GetTargetFameInfo(sim, out int fame, out _, applyDifficultyScaling: false);
            var tt = Autotest.RequireRun().T.Tactics;
            string group = t.GetTargetAllegiance(sim) is { } a ? Ai.PersonalityStrategy.GroupOf(a) : "?";
            var w = Ai.PersonalityStrategy.Weights(group, t.ShipID, tt);
            string weights = w is null ? "none" : string.Join("/", Enum.GetValues<Ai.Trait>().Select(tr => $"{tr}{w[(int)tr]:0.00}"));
            return $"{t.GetPlainTargetName(sim)}:{t.ShipID}:fame{fame}:tier{t.GetTargetTier(sim)}:{Ai.PersonalityStrategy.Pick(group, t.ShipID, tt)?.Name}:{weights}";
        }));
    }

    static int Disablers(Run run, SimRoot sim, Mission m)
    {
        var t = run.T.Tactics;
        if (!t.PersonalitiesEnabled || !t.DisableEnabled) return 0;
        var objectives = new List<DefeatShipsObjective>();
        m.GetObjectives(objectives);
        return objectives.SelectMany(o => o.TargetShips).Count(target =>
            target.GetTargetAllegiance(sim) is { } allegiance
            && Ai.PersonalityStrategy.Weights(Ai.PersonalityStrategy.GroupOf(allegiance), target.ShipID, t) is { } w
            && w[(int)Ai.Trait.Disable] >= t.PersonalityThreshold);
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

using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Events;

namespace HotCargo.Chains;

/// <summary>
/// Entry point of the Chains area: one story chain per trade or military station (CHAINS.md).
/// Content: mod/chains/chains.rules (chains, stages, result rows), missions.rules (stage missions), pages.rules (choice pages).
/// </summary>
public static class ChainsArea
{
    public static void Register()
    {
        EventLibrary.ExtraFiles.Add(ChainLibrary.PagesPath);
        Run.Hooks.Tick += ChainRunner.OnTick;
        Run.Hooks.SystemEntered += ChainRunner.OnSystemEntered;
        EventRunner.OutcomeChosen += ChainRunner.OnOutcomeChosen;
        AddAutotest();
    }

    static Run RequireRun() => Autotest.RequireRun();
    static SimRoot RequireSim() => RequireRun().Game.Sim ?? throw new InvalidOperationException("no sim");

    /// <summary>The chain that the test commands act on: the last one that chain_force started.</summary>
    static ChainInstance? _test;

    static ChainInstance Test() => _test is { } c && RequireRun().State.Chains.Instances.Contains(c)
        ? c
        // After a load the run state holds new objects. The forced chain is the last one, so tests find it again.
        : _test = RequireRun().State.Chains.Instances.LastOrDefault() ?? throw new InvalidOperationException("no chain; run chain_force first");

    static Mission? StageMission()
    {
        var c = Test();
        if (c.MissionId is not { } id) return null;
        return RequireRun().Mode.Missions[id] ?? (ChainRunner.FindStation(RequireSim(), c.StationId)?.GetProvidedMissions()?[id]);
    }

    static void AddAutotest()
    {
        // chain_force <chain id>: gives the station nearest to the fleet that chain, at its first stage. Test only (single player, from a frame).
        Autotest.Commands["chain_force"] = arg =>
        {
            var run = RequireRun();
            var sim = RequireSim();
            var s = run.State.Chains;
            s.AssignedNodes.Add(sim.NodeId());
            var at = SimQueries.FleetCenter(sim) ?? throw new InvalidOperationException("no player ships");
            var stations = ChainRunner.Stations(sim).OrderBy(st => st.DetWorldCenter.DistanceSquaredTo(at)).ThenBy(st => st.StationID).ToList();
            if (stations.Count == 0) throw new InvalidOperationException("no eligible station in this system");
            var station = stations[0];
            s.Instances.RemoveAll(i => i.StationId == station.StationID && i.NodeId == sim.NodeId());
            _test = ChainRunner.Start(run, sim, ChainLibrary.Get(arg), station);
            Autotest.Wait(2);
        };
        // chain_tostation: moves the player ships next to the test chain's station.
        Autotest.Commands["chain_tostation"] = _ =>
        {
            var sim = RequireSim();
            var st = ChainRunner.FindStation(sim, Test().StationId) ?? throw new InvalidOperationException("the chain station is not in this system");
            float offset = 600f;
            foreach (var ship in sim.Ships.HumanPlayerSignificantShips.ToList())
            {
                Autotest.Teleport(ship, st.DetWorldCenter + new Vector2(offset + ship.WorldBoundingRadius + (st.PhysicalShip?.WorldBoundingRadius ?? 300f), 0));
                offset += 2 * ship.WorldBoundingRadius + 100f;
            }
            Autotest.Wait(3);
        };
        // chain_away: moves the player ships far from the test chain's station, so a station page does not open.
        Autotest.Commands["chain_away"] = _ =>
        {
            var sim = RequireSim();
            var st = ChainRunner.FindStation(sim, Test().StationId) ?? throw new InvalidOperationException("the chain station is not in this system");
            foreach (var ship in sim.Ships.HumanPlayerSignificantShips.ToList()) Autotest.Teleport(ship, st.DetWorldCenter + new Vector2(0, 6000));
            Autotest.Wait(2);
        };
        // chain_accept: accepts the stage offer through the comms accept input, like the missions tab does.
        Autotest.Commands["chain_accept"] = _ =>
        {
            var c = Test();
            var st = ChainRunner.FindStation(RequireSim(), c.StationId) ?? throw new InvalidOperationException("the chain station is not in this system");
            var ship = st.PhysicalShip ?? throw new InvalidOperationException("the chain station is in stasis; run chain_tostation first");
            var m = st.GetProvidedMissions()?[c.MissionId ?? throw new InvalidOperationException("no stage offer")] ?? throw new InvalidOperationException("the offer is not in the station's list");
            RequireRun().Game.NetManager.EnqueueInput(new CommMissionsTab.MPAcceptMissionInput(CommMissionsTab.MissionSourceType.Ship, ship, m));
            Autotest.Wait(2);
        };
        // chain_abandon: abandons the accepted stage mission through the mission log input.
        Autotest.Commands["chain_abandon"] = _ =>
        {
            var m = RequireRun().Mode.Missions[Test().MissionId ?? throw new InvalidOperationException("no stage mission")] ?? throw new InvalidOperationException("the stage mission is not accepted");
            RequireRun().Game.NetManager.EnqueueInput(new MissionLogTab.MPAbandonMissionInput(m));
            Autotest.Wait(2);
        };
        // chain_goto [tag]: moves the player ships near the first ship with the tag (default hc_ch_enemy), so ships in stasis come alive.
        Autotest.Commands["chain_goto"] = arg =>
        {
            var sim = RequireSim();
            string tag = arg.Length > 0 ? arg : "hc_ch_enemy";
            var target = SimQueries.TaggedShips(sim, new(tag)).FirstOrDefault();
            if (target == default) throw new InvalidOperationException($"no ship tagged {tag}");
            float offset = 900f;
            foreach (var ship in sim.Ships.HumanPlayerSignificantShips.ToList())
            {
                Autotest.Teleport(ship, target.Location + new Vector2(offset, 0));
                offset += 2 * ship.WorldBoundingRadius + 100f;
            }
            Autotest.Wait(3);
        };
        // chain_kill [tag]: removes the live ships with the tag (default hc_ch_enemy, the enemies of stage missions).
        // Removing hc_ch_cargo destroys a protected transport, so its ProtectShips objective fails.
        Autotest.Commands["chain_kill"] = arg =>
        {
            var sim = RequireSim();
            int n = 0;
            var tag = new Cosmoteer.Data.ID<Cosmoteer.Generators.Simulation.SimObjectSpawner>(arg.Length > 0 ? arg : "hc_ch_enemy");
            foreach (var t in SimQueries.TaggedShips(sim, tag))
                if (t.Live is { } ship) { sim.Ships.Remove(ship, dispose: true, handleLostShip: false); n++; }
            // Ships in stasis too: removing the spawner counts as a kill for mission objectives.
            foreach (var spawner in sim.Stasis.Where(sp => sp.HasTag(tag)).ToList()) { sim.Stasis.RemoveStasisSpawner(spawner); n++; }
            Log.Info($"AUTOTEST removed {n} ships tagged {(arg.Length > 0 ? arg : "hc_ch_enemy")}");
            Autotest.Wait(2);
        };
        // chain_pay: collects the reward of a finished stage mission at its station, like the comms payment button.
        Autotest.Commands["chain_pay"] = _ =>
        {
            var c = Test();
            var ship = ChainRunner.FindStation(RequireSim(), c.StationId)?.PhysicalShip ?? throw new InvalidOperationException("the chain station is not live");
            var m = RequireRun().Mode.Missions[c.MissionId ?? throw new InvalidOperationException("no stage mission")] ?? throw new InvalidOperationException("the stage mission is not accepted");
            RequireRun().Game.NetManager.EnqueueInput(new CommMissionsTab.MPMissionPaymentInput(ship, m));
            Autotest.Wait(2);
        };
        // chain_result <result>: gives the current stage that result now, through the same path as a mission that leaves the log.
        Autotest.Commands["chain_result"] = arg =>
        {
            var c = Test();
            if (StageMission() is { } m)
            {
                if (RequireRun().Mode.Missions.ContainsMission(m)) RequireRun().Mode.Missions.RemoveMission(m);
                else ChainRunner.FindStation(RequireSim(), c.StationId)?.GetProvidedMissions()?.RemoveMission(m);
            }
            c.MissionId = null;
            c.PendingResult = arg;
            Autotest.Wait(2);
        };
        // chain_due: starts a waiting Return or Arrival stage now (headless games cannot finish a jump).
        Autotest.Commands["chain_due"] = _ =>
        {
            var c = Test();
            c.Away = true;
            c.DueSeconds = RequireRun().State.GameSeconds;
            Autotest.Wait(2);
        };
        // chain_revisit: runs the system-entered hook for the current system, like a later visit (headless games cannot finish a jump).
        Autotest.Commands["chain_revisit"] = _ =>
        {
            ChainRunner.OnSystemEntered(RequireRun(), firstVisit: false);
            Autotest.Wait(2);
        };
        // chain_mark: remembers the reputation with the test station's faction, for the field chain_rep_gain.
        Autotest.Commands["chain_mark"] = _ =>
        {
            double marked = Autotest.Fields["chain_reputation"]();
            Autotest.Fields["chain_rep_gain"] = () => Autotest.Fields["chain_reputation"]() - marked;
        };
        Autotest.Commands["expectstage"] = arg =>
        {
            string actual = Test().Ended ? "end" : Test().Stage;
            Autotest.Check(actual == arg, $"chain {Test().ChainId} stage is {arg} (actual {actual})");
        };
        Autotest.Commands["expectchainflag"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool has = Test().Flags.Contains(parts[0]);
            Autotest.Check(has == (parts[1] == "1"), $"chain flag {parts[0]} is {parts[1]} (actual {(has ? 1 : 0)})");
        };
        // chainview: logs the stage offer as comms shows it (name, description, reward lines), as plain text.
        Autotest.Commands["chainview"] = _ =>
        {
            var m = StageMission() ?? throw new InvalidOperationException("no stage mission");
            var lines = new List<(Halfling.Gui.Components.Text.ITextProvider Text, Halfling.Gui.Components.Text.ITextProvider? Tooltip)>();
            m.GetRewardDisplayLines(lines);
            string name = EventView.Plain(m.NameAndInfo.Text), desc = EventView.Plain(m.Description?.Text ?? "");
            var objectives = new List<Cosmoteer.Modes.Career.Missions.Objectives.Objective.DisplayLine>();
            m.GetObjectiveDisplayLines(objectives);
            Log.Info($"AUTOTEST CHAIN VIEW {name} ({m.Completion})\n{desc}\n  {string.Join("\n  ", lines.Select(l => EventView.Plain(l.Text.Text)))}" +
                     $"\n  {string.Join("\n  ", objectives.Select(o => $"[{o.State}] {EventView.Plain(o.Text.Text)}"))}");
            Autotest.Check(name.Contains("[Chain]") && desc.Length > 0 && lines.Any(l => l.Text.Text.Contains("Story chain")), "the stage offer shows the chain label, a description and the chain line");
        };
        // chain_spawnall: spawns the mission of every mission stage of every chain at the test station, to catch content errors.
        Autotest.Commands["chain_spawnall"] = _ =>
        {
            var run = RequireRun();
            var sim = RequireSim();
            var c = Test();
            var at = ChainRunner.FindStation(sim, c.StationId)?.DetWorldCenter ?? throw new InvalidOperationException("the chain station is not in this system");
            int ok = 0, none = 0;
            foreach (var chain in ChainLibrary.InOrder)
                foreach (var stage in chain.Stages.Where(s => s.Kind == StageKind.Mission))
                {
                    var probe = new ChainInstance { ChainId = chain.ID, NodeId = c.NodeId, StationId = c.StationId, StationPlayerIndex = c.StationPlayerIndex, Stage = stage.ID };
                    ChainRunner.SpawnMission(run, sim, probe, stage, at);
                    if (probe.MissionId is not { } id) { none++; Log.Info($"AUTOTEST CHAIN SPAWN {chain.ID}/{stage.ID}: no mission"); continue; }
                    var m = run.Mode.Missions[id] ?? ChainRunner.FindStation(sim, c.StationId)?.GetProvidedMissions()?[id];
                    Log.Info($"AUTOTEST CHAIN SPAWN {chain.ID}/{stage.ID}: {(m is null ? "queued for the log" : EventView.Plain(m.NameAndInfo.Text))}");
                    ok++;
                }
            Autotest.Check(none == 0, $"every chain mission stage spawns a mission ({ok} spawned, {none} without a mission)");
        };
        Autotest.Fields["chain_part"] = () => Test().Part;
        Autotest.Fields["chain_story"] = () => Test().Story.Count;
        Autotest.Fields["chain_mission"] = () => Test().MissionId is null ? 0 : 1;
        Autotest.Fields["chain_accepted"] = () => Test().MissionId is { } id && RequireRun().Mode.Missions[id] is not null ? 1 : 0;
        Autotest.Fields["chain_awaiting"] = () => Test().AwaitingSerial > 0 ? 1 : 0;
        Autotest.Fields["chain_count"] = () => RequireRun().State.Chains.Instances.Count;
        Autotest.Fields["chain_here"] = () => RequireRun().State.Chains.Instances.Count(i => i.NodeId == RequireSim().NodeId());
        Autotest.Fields["chain_stations"] = () => ChainRunner.Stations(RequireSim()).Count();
        Autotest.Fields["chain_kinds"] = () => RequireRun().State.Chains.Instances.Select(i => i.ChainId).Distinct().Count();
        Autotest.Fields["chain_offers"] = () => ChainRunner.Stations(RequireSim()).Sum(st => st.GetProvidedMissions()?.Count(m => m.MetatypeID == ChainLibrary.Metatype) ?? 0);
        Autotest.Fields["chain_reputation"] = () =>
        {
            var f = Cosmoteer.Factions.FactionRules.GetFaction(Test().StationPlayerIndex) ?? throw new InvalidOperationException("station has no faction");
            return RequireRun().Mode.GetFactionReputation(f.ID);
        };
        Autotest.Fields["chains_loaded"] = () => ChainLibrary.InOrder.Count;
    }
}

using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Random;
using Halfling.Serialization;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Hunters;
using HotCargo.Missions;
using HotCargo.Tech;

namespace HotCargo.Events;

/// <summary>
/// Entry point of the Events area: FTL-style choice events (EVENTS.md).
/// Content: mod/events/events.rules (events), spawns.rules (ships that outcomes spawn), sysgen_events.rules (signal POIs).
/// Money in events is in "event money units": 1 = the middle of the standard mission pay at the sector tier, with the reward factors.
/// </summary>
public static class EventsArea
{
    public static void Register()
    {
        Run.Hooks.NewRunStarted += EventRunner.OnNewRun;
        Run.Hooks.SystemEntered += EventRunner.OnSystemEntered;
        Run.Hooks.Tick += EventRunner.OnTick;
        AddAutotest();
    }

    static Run RequireRun() => Autotest.RequireRun();
    static SimRoot RequireSim() => RequireRun().Game.Sim ?? throw new InvalidOperationException("no sim");
    static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    static readonly Dictionary<string, double> Marks = new(StringComparer.OrdinalIgnoreCase);

    static void AddAutotest()
    {
        // event <id>: opens the event now, replacing any pending page. Test only (single player, from a frame).
        Autotest.Commands["event"] = arg =>
        {
            var run = RequireRun();
            EventRunner.Open(run, RequireSim(), EventLibrary.Get(arg), lead: "");
            Autotest.Wait(1);
        };
        // choose <choice number | last> [outcome number]: without an outcome it sends the MP input like the dialog does and rolls.
        // With an outcome it applies that outcome directly, for exact effect checks.
        Autotest.Commands["choose"] = arg =>
        {
            var run = RequireRun();
            var p = run.State.Events.Pending ?? throw new InvalidOperationException("no pending event");
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int choice = parts[0] == "last" ? EventLibrary.Get(p.EventId).Choices.Length - 1 : Int(parts[0]) - 1;
            if (parts.Length > 1) EventRunner.Choose(run, RequireSim(), run.Game.LocalPlayerIndex, p.Serial, choice, Int(parts[1]) - 1);
            else run.Game.NetManager.EnqueueInput(new EventChoiceInput(p.Serial, choice));
            Autotest.Wait(2);
        };
        Autotest.Commands["expectevent"] = arg =>
        {
            string actual = RequireRun().State.Events.Pending?.EventId ?? "none";
            Autotest.Check(actual == arg, $"pending event is {arg} (actual {actual})");
        };
        // expectchoice <choice number> enabled|disabled|hidden: checks the dialog view of the pending page for the local player.
        Autotest.Commands["expectchoice"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int index = Int(parts[0]) - 1;
            var page = CurrentView() ?? throw new InvalidOperationException("no event page");
            var c = page.Choices.FirstOrDefault(v => v.Index == index);
            string actual = c is null ? "hidden" : c.Enabled ? "enabled" : "disabled";
            Autotest.Check(actual == parts[1], $"choice {index + 1} is {parts[1]} (actual {actual}: {EventView.Plain(c?.Text ?? "")})");
        };
        Autotest.Commands["expectflag"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool has = RequireRun().State.Events.Flags.Contains(parts[0]);
            Autotest.Check(has == (parts[1] == "1"), $"flag {parts[0]} is {parts[1]} (actual {(has ? 1 : 0)})");
        };
        // expecttagged <spawn tag> <op> <count>: live and stasis ships with the tag, for spawn outcomes.
        Autotest.Commands["expecttagged"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Autotest.Fields[$"tagged_{parts[0]}"] = () => Tagged(RequireSim(), parts[0]);
            Autotest.Commands["expect"]($"tagged_{parts[0]} {parts[1]} {parts[2]}");
        };
        // eventview: logs the page that the dialog shows now, as plain text.
        Autotest.Commands["eventview"] = _ =>
        {
            var page = CurrentView();
            if (page is null) { Log.Info("AUTOTEST EVENT VIEW: none"); return; }
            var r = RequireRun();
            Log.Info($"AUTOTEST EVENT VIEW {page.Key} (money {r.Game.GetPlayerState(r.Game.LocalPlayerIndex, deterministic: true).Money:n0}): {page.Title}\n{EventView.Plain(page.Body)}");
            foreach (var c in page.Choices) Log.Info($"  [{(c.Enabled ? "x" : " ")}] {EventView.Plain(c.Text)}");
        };
        // eventviewall: builds the view of every event page with the current fleet, to catch content and text errors.
        Autotest.Commands["eventviewall"] = _ =>
        {
            var run = RequireRun();
            var sim = RequireSim();
            foreach (var e in EventLibrary.InOrder)
            {
                var p = new PendingEvent { EventId = e.ID, Serial = 0, Tier = run.CurrentTier, TechValue = EventRunner.CurrentTechValue(run) };
                var page = EventView.Pending(run, sim, p, run.Game.LocalPlayerIndex);
                Log.Info($"AUTOTEST EVENT {e.ID} ({e.Trigger}): {page.Title}\n{EventView.Plain(page.Body)}");
                foreach (var c in page.Choices) Log.Info($"  [{(c.Enabled ? "x" : " ")}] {EventView.Plain(c.Text)}");
            }
            Autotest.Check(EventLibrary.InOrder.Count >= 10, $"{EventLibrary.InOrder.Count} events load and render");
        };
        // ev_mark: remembers money, heat, vault tech, fame, crew and every resource, for the fields ev_money_gain, ev_heat_gain,
        // ev_tech_gain, ev_fame_gain, ev_crew_gain and ev_res_<resource id>_gain.
        Autotest.Commands["ev_mark"] = _ =>
        {
            foreach (var (name, f) in Gauges()) Marks[name] = f();
            foreach (var (name, f) in Gauges()) Autotest.Fields[$"ev_{name}_gain"] = () => f() - Marks[name];
        };
        // ev_set <EventsTunables field> <value>: changes a live tunable for this run. Test only.
        Autotest.Commands["ev_set"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var t = RequireRun().T.Events;
            var field = typeof(EventsTunables).GetField(parts[0]) ?? throw new InvalidOperationException($"no Events tunable {parts[0]}");
            field.SetValue(t, Convert.ChangeType(parts[1], field.FieldType, CultureInfo.InvariantCulture));
        };
        // ev_arrive: runs the first-arrival hook without a jump (headless games cannot finish a jump).
        Autotest.Commands["ev_arrive"] = _ => EventRunner.OnSystemEntered(RequireRun(), firstVisit: true);
        // ev_signal: spawns a signal POI from the system generator group, 3,000 from the first player ship. ev_signal_goto flies there.
        Autotest.Commands["ev_signal"] = _ => SpawnSignal();
        Autotest.Commands["ev_signal_goto"] = _ =>
        {
            var sim = RequireSim();
            var signal = sim.Doodads.Where(d => d.Tags?.Contains(EventRunner.SignalTag) == true).OrderBy(d => d.UniqueID).FirstOrDefault()
                ?? throw new InvalidOperationException("no signal POI in this system");
            foreach (var s in HunterSpawner.HumanShips(sim).ToList()) s.WorldCenter = signal.Location + new Halfling.Geometry.Vector2(300, 0);
            Autotest.Wait(3);
        };
        // ev_tostation: moves the player ships next to the nearest ship tagged trade_station (not any ship that trades).
        Autotest.Commands["ev_tostation"] = _ =>
        {
            var sim = RequireSim();
            var ships = HunterSpawner.HumanShips(sim).ToList();
            var tag = new ID<SimObjectSpawner>("trade_station");
            var station = sim.Ships.Where(s => s.Tags?.Contains(tag) == true && !s.IsJunk)
                .OrderBy(s => s.DetWorldCenter.DistanceSquaredTo(ships[0].DetWorldCenter)).FirstOrDefault()
                ?? throw new InvalidOperationException("no live trade station in this system");
            float offset = station.WorldBoundingRadius + 300f;
            foreach (var s in ships) { s.WorldCenter = station.WorldCenter + new Halfling.Geometry.Vector2(offset + s.WorldBoundingRadius, 0); offset += 2 * s.WorldBoundingRadius + 100f; }
            Log.Info($"AUTOTEST moved next to station {station.Metadata.PlainShipName}");
            Autotest.Wait(2);
        };
        Autotest.Fields["ev_pending"] = () => RequireRun().State.Events.Pending is null ? 0 : 1;
        Autotest.Fields["ev_seen"] = () => RequireRun().State.Events.Seen.Values.Sum();
        Autotest.Fields["ev_result"] = () => RequireRun().State.Events.LastResult?.Serial ?? 0;
        Autotest.Fields["ev_signals"] = () => RequireSim().Doodads.Count(d => d.Tags?.Contains(EventRunner.SignalTag) == true);
        Autotest.Fields["ev_stdmoney"] = () => EventRunner.Credits(RequireRun(), 1f, 0f, RequireRun().CurrentTier, 0);
    }

    static EventPage? CurrentView()
    {
        var run = RequireRun();
        return EventView.Current(run, RequireSim(), run.Game.LocalPlayerIndex, lastResultShown: int.MaxValue);
    }

    static IEnumerable<(string Name, Func<double> Value)> Gauges()
    {
        yield return ("money", () => { var r = RequireRun(); return r.Game.GetPlayerState(r.Game.LocalPlayerIndex, deterministic: true).Money; });
        yield return ("heat", () => RequireRun().State.Heat);
        yield return ("tech", () => Vaults.All(RequireSim()).Sum(g => g.Resources));
        yield return ("fame", () => RequireRun().Mode.PlayerFame);
        yield return ("crew", () => { var r = RequireRun(); return r.Game.HumanPlayersIndexes.Sum(RequireSim().Crew.CountCrewForPlayer); });
        foreach (var res in GameApp.Rules.Resources)
        {
            var id = res.ID;
            yield return ($"res_{id}", () => Vaults.HumanShips(RequireSim()).Sum(s => s.Resources.GetResourceTotal(id, includeStored: true, includeCarried: false)));
        }
    }

    static int Tagged(SimRoot sim, string tagName)
    {
        var tag = new ID<SimObjectSpawner>(tagName);
        int live = sim.Ships.Count(s => s.Tags?.Contains(tag) == true && !s.IsJunk);
        int stasis = sim.Stasis.Count(s => MissionArea.StasisTags(s)?.Contains(tag) == true);
        return live + stasis;
    }

    static void SpawnSignal()
    {
        var sim = RequireSim();
        var mode = (CareerSimModeManager)sim.Mode;
        var tag = new ID<SimObjectSpawner>("hc_group_signal");
        var group = All(mode.SectorType.SystemGenerator.Spawners).OfType<SimObjectSpawner>().FirstOrDefault(s => s.Tags?.Contains(tag) == true)
            ?? throw new InvalidOperationException("signal spawner group not found in the sector generator; are the event rules loaded?");
        var at = HunterSpawner.FleetCenter(sim) ?? throw new InvalidOperationException("no player ships");
        // Test values for chance, count and distance, restored after, like the Missions test helper does.
        var saved = (group.SpawnChance, group.Count, group.Distance, group.MinDistanceFromTags);
        try
        {
            (group.SpawnChance, group.Count, group.Distance, group.MinDistanceFromTags) = (1f, 1f, new Halfling.Range<float>(3000f, 3000f), null);
            new SimulationGenerator { Spawners = [group] }.Generate(sim, new FastRand(1234UL), loadInstantly: true, rootLoc: at);
        }
        finally
        {
            (group.SpawnChance, group.Count, group.Distance, group.MinDistanceFromTags) = saved;
        }
        Log.Info($"AUTOTEST signal spawned; signals in system: {sim.Doodads.Count(d => d.Tags?.Contains(EventRunner.SignalTag) == true)}");
        Autotest.Wait(1);
    }

    static IEnumerable<SimSpawner> All(IEnumerable<SimSpawner> roots)
    {
        foreach (SimSpawner s in roots)
        {
            yield return s;
            if (s is SimObjectSpawner { SubSpawners: { } subs })
                foreach (SimSpawner sub in All(subs)) yield return sub;
        }
    }
}

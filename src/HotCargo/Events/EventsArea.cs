using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Random;
using Halfling.Serialization;
using Halfling.Geometry;
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
    static void SendPick(string arg, bool select)
    {
        var run = RequireRun();
        var p = run.State.Events.Pending ?? throw new InvalidOperationException("no pending event");
        var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int index = Int(parts[0]) - 1;
        var options = EventLibrary.Get(p.EventId).Options;
        if (index < 0 || index >= options.Length) throw new InvalidOperationException($"event {p.EventId} has no option {index + 1}");
        int steps = parts.Length > 1 ? Int(parts[1]) : options[index].MinSteps;
        MultiChoice.DraftSender.Toggle(run, EventsMultiDomain.Name, p.Serial, index, select, steps);
        Autotest.Wait(1);
    }

    public static void Register()
    {
        Run.Hooks.NewRunStarted += EventRunner.OnNewRun;
        Run.Hooks.SystemEntered += EventRunner.OnSystemEntered;
        Run.Hooks.Tick += EventRunner.OnTick;
        Run.Hooks.Tick += MissionCargo.OnTick;
        Run.Hooks.Tick += TracerBeacon.OnTick;
        Run.Hooks.Tick += RescueBeacon.OnTick;
        ItemActions.Handlers.Add("rescue", new RescueBeacon.Handler());
        Run.Hooks.NewRunStarted += Abandoned.OnNewRun;
        Ui.CommsHail.Providers.Add(new EventHailProvider());
        Run.Hooks.SystemEntered += Abandoned.OnSystemEntered;
        SharedChoice.SharedChoices.Register(EventsChoiceDomain.Instance);
        MultiChoice.MultiChoices.Register(EventsMultiDomain.Instance);
        AddAutotest();
    }

    static Run RequireRun() => Autotest.RequireRun();
    static SimRoot RequireSim() => RequireRun().Game.Sim ?? throw new InvalidOperationException("no sim");
    static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    static readonly Dictionary<string, double> Marks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The serial of the page that was pending at the last ev_mark. 0: none.</summary>
    internal static int MarkedSerial;

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
            else run.Game.NetManager.EnqueueInput(EventsChoiceDomain.Pick(p.Serial, choice));
            Autotest.Wait(2);
        };
        // answer: answers a page that the test did not pick (a random trigger), as a player does. A plain last choice is taken.
        // A last choice that submits options (spec 811, Vault Coolant Leak) first gets the first option the player can select, at
        // its full amount. A page with no option to select fails: in play it could not be closed.
        Autotest.Commands["answer"] = _ =>
        {
            var page = CurrentView() ?? throw new InvalidOperationException("no event page");
            var last = page.Choices[^1];
            if (!last.Submit) { Autotest.Inject([$"choose {last.Index + 1}"]); return; }
            var option = page.Options.FirstOrDefault(o => o.Active)
                ?? throw new InvalidOperationException($"CHECK FAILED: {EventView.Plain(page.Title)} has no option to select, so its submit cannot close it");
            string steps = option.Slider is { } slider ? $" {slider.Max}" : "";
            Autotest.Inject([$"pick {option.Id + 1}{steps}", $"choose {last.Index + 1}"]);
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
        // pick <option number> [steps] / unpick <option number>: selects or clears a page option in the shared draft, as a row click
        // does (the multiselect MP input, spec 810). Steps default to the option's minimum. A real player action: no "mp" in co-op.
        Autotest.Commands["pick"] = arg => SendPick(arg, select: true);
        Autotest.Commands["unpick"] = arg => SendPick(arg, select: false);
        // expectoption <option number> enabled|disabled|hidden|selected: checks the option row of the pending page.
        Autotest.Commands["expectoption"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int index = Int(parts[0]) - 1;
            var page = CurrentView() ?? throw new InvalidOperationException("no event page");
            var o = page.Options.FirstOrDefault(v => v.Id == index);
            string actual = o is null ? "hidden" : o.Selected ? "selected" : o.Active ? "enabled" : "disabled";
            Autotest.Check(actual == parts[1], $"option {index + 1} is {parts[1]} (actual {actual}: {EventView.Plain(o?.Text ?? "")})");
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
        // ev_tech_gain, ev_fame_gain, ev_crew_gain and ev_res_<resource id>_gain. It also remembers the serial of the pending page,
        // for the field ev_answered and for "mpchoose marked".
        Autotest.Commands["ev_mark"] = _ =>
        {
            MarkedSerial = RequireRun().State.Events.Pending?.Serial ?? 0;
            foreach (var (name, f) in Gauges()) Marks[name] = f();
            foreach (var (name, f) in Gauges()) Autotest.Fields[$"ev_{name}_gain"] = () => f() - Marks[name];
        };
        // ev_pause on|off: autotests do not pause on an event window (PauseOnEvent), so a windowed test runs on. "on" asks for the pause.
        Autotest.Commands["ev_pause"] = arg => EventDialog.PauseInAutotest = arg.Trim() switch
        {
            "on" => true, "off" => false, _ => throw new InvalidOperationException("ev_pause needs: on|off"),
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
            foreach (var s in sim.Ships.HumanPlayerSignificantShips.ToList()) Autotest.Teleport(s, signal.Location + new Halfling.Geometry.Vector2(300, 0));
            Autotest.Wait(3);
        };
        // ev_tostation: moves the player ships next to the nearest ship tagged trade_station (not any ship that trades).
        Autotest.Commands["ev_tostation"] = arg =>
        {
            var sim = RequireSim();
            var ships = sim.Ships.HumanPlayerSignificantShips.ToList();
            var tag = new ID<SimObjectSpawner>("trade_station");
            // Live or in stasis: after a teleport far away (for example to a signal), every station is in stasis.
            var station = SimQueries.TaggedShips(sim, tag).OrderBy(t => t.Location.DistanceSquaredTo(ships[0].DetWorldCenter)).ThenBy(t => t.Id).ToList() switch
            {
                [var first, ..] => first,
                [] => throw new InvalidOperationException("no trade station in this system"),
            };
            // A station in stasis has no known size; 1,200 clears a trade station and stays inside StationTriggerDistance.
            // ev_tostation <distance>: this far from the station's hull (from its center while it is in stasis).
            float offset = arg.Trim().Length > 0 ? (station.Live?.WorldBoundingRadius ?? 0f) + float.Parse(arg, CultureInfo.InvariantCulture)
                : station.Live is { } live ? live.WorldBoundingRadius + 300f : 1200f;
            foreach (var s in ships) { Autotest.Teleport(s, station.Location + new Halfling.Geometry.Vector2(offset + s.WorldBoundingRadius, 0)); offset += 2 * s.WorldBoundingRadius + 100f; }
            Log.Info($"AUTOTEST moved next to station {station.Id} (live: {station.Live is not null})");
            Autotest.Wait(2);
        };
        // 1 while the sim is paused. Event dialogs must not pause headless test games.
        Autotest.Fields["ev_paused"] = () => RequireRun().Game.Sim.IsPaused.Confirmed ? 1 : 0;
        // expectneed <event id> met|unmet: checks the event-level need with the current fleet (the trigger condition).
        Autotest.Commands["expectneed"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var run = RequireRun();
            var e = EventLibrary.Get(parts[0]);
            var p = new PendingEvent { EventId = e.ID, Tier = run.CurrentTier, TechValue = EventRunner.CurrentTechValue(run), Heat = run.State.Heat };
            var unmet = EventModel.FirstUnmet(EventRunner.ToNeed(run, e.Need, p.Tier, p.TechValue),
                EventRunner.Snap(run, RequireSim(), run.Game.GetPlayerState(run.Game.LocalPlayerIndex, deterministic: true).Money, p.Tier, p.TechValue));
            string actual = unmet is null ? "met" : "unmet";
            Autotest.Check(actual == parts[1], $"need of {e.ID} is {parts[1]} (actual {actual}{(unmet is null ? "" : $": {unmet}")})");
        };
        // expecteligible <event id> 1|0: whether a trigger could pick the event now (run limit, category limit, need).
        Autotest.Commands["expecteligible"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var run = RequireRun();
            var snap = EventRunner.Snap(run, RequireSim(), run.Game.GetPlayerState(run.Game.LocalPlayerIndex, deterministic: true).Money,
                run.CurrentTier, EventRunner.CurrentTechValue(run));
            bool eligible = EventRunner.Eligible(run, EventLibrary.Get(parts[0]), snap);
            Autotest.Check(eligible == (parts[1] == "1"), $"{parts[0]} eligible is {parts[1]} (actual {(eligible ? 1 : 0)})");
        };
        // ev_sector <n>: sets the sector index without a jump (headless games cannot finish a jump). Test only.
        Autotest.Commands["ev_sector"] = arg => RequireRun().State.SectorIndex = Int(arg);
        // Courier Job (spec 85, 1100 to 1103): offers in station lists, accept the first, fly to the contact, and the hand-over state.
        Autotest.Fields["ev_courier_offers"] = () => CourierOffers(RequireSim()).Count();
        // ev_courier_offer: the nearest live trade station offers a Courier Job now, without the roll. Run it with "mp" in co-op tests.
        Autotest.Commands["ev_courier_offer"] = _ =>
        {
            var sim = RequireSim();
            var at = SimQueries.FleetCenter(sim) ?? throw new InvalidOperationException("no player ships");
            var station = sim.Ships.Where(s => s.HasTag(new ID<SimObjectSpawner>("trade_station"))).OrderBy(s => s.DetWorldCenter.DistanceSquaredTo(at)).ThenBy(s => s.UniqueID).FirstOrDefault()
                ?? throw new InvalidOperationException("no live trade station");
            EventRunner.OfferCourier(sim, station);
        };
        // Accepts through the game's own accept input, as the comms window does, so the accept gate and the cargo load run (spec 1101).
        Autotest.Commands["ev_courier_accept"] = _ =>
        {
            var mission = CourierOffers(RequireSim()).Select(o => o.Mission).FirstOrDefault()
                ?? throw new InvalidOperationException("no Courier Job offer in this system");
            var station = CourierStation();
            _acceptedCourier = mission;
            RequireRun().Game.NetManager.EnqueueInput(new Cosmoteer.Modes.Career.Comms.CommMissionsTab.MPAcceptMissionInput(
                Cosmoteer.Modes.Career.Comms.CommMissionsTab.MissionSourceType.Ship, station, mission));
            Autotest.Wait(2);
        };
        // Spec 1119, the real comms UI, one frame apart (the window builds its tabs on its first frame):
        // ev_courier_ui open | tab | select | close: open the offer station's comms, show the missions tab, select the offer, close.
        // ev_accept_active is 1 while the tab's accept button reacts.
        Autotest.Commands["ev_courier_ui"] = arg =>
        {
            var game = RequireRun().Game;
            var station = CourierStation();
            Cosmoteer.Modes.Career.Comms.CommWindow.s_openWindows.TryGetValue(station, out var open);
            switch (arg.Trim())
            {
                case "open":
                    // Next to the offer's station, in comms range, then its comms window as a hail opens it.
                    var caller = SimQueries.FirstHumanShip(RequireSim()) ?? throw new InvalidOperationException("no player ship");
                    Autotest.Teleport(caller, station.DetWorldCenter + new Halfling.Geometry.Vector2(station.WorldBoundingRadius + caller.WorldBoundingRadius + 200f, 0));
                    game.Gui.FloatingWindows.AddChild(new Cosmoteer.Modes.Career.Comms.CommWindow(game, caller, station));
                    break;
                case "tab":
                    var window = open ?? throw new InvalidOperationException("no comms window with the station; ev_courier_ui open first");
                    window._tabs.TabsListBox.SelectedWidget = window._missionsTab ?? throw new InvalidOperationException("the station has no missions tab");
                    break;
                case "select":
                    var mission = CourierOffers(RequireSim()).Select(o => o.Mission).FirstOrDefault() ?? throw new InvalidOperationException("no Courier Job offer");
                    MissionsTab()._missionsPanel._missionItems[mission].IsSelected = true;
                    break;
                case "close":
                    if (open is not null) Cosmoteer.Game.Gui.Extensions.Unparent(open);
                    break;
                default: throw new InvalidOperationException("ev_courier_ui needs: open | tab | select | close");
            }
        };
        Autotest.Fields["ev_courier_ui_open"] = () => Cosmoteer.Modes.Career.Comms.CommWindow.s_openWindows.TryGetValue(CourierStation(), out _) ? 1 : 0;
        Autotest.Fields["ev_accept_active"] = () => MissionsTab()._acceptBtn.SelfInputActive ? 1 : 0;
        // The Courier Job that ev_courier_accept accepted last, also after it left the log: 1 done, 0 open, -1 failed, -2 none.
        Autotest.Fields["ev_courier_job"] = () =>
        {
            if (_acceptedCourier is not { } m) return -2;
            var found = new List<HandOverObjective>();
            m.GetObjectives(found);
            return found.Single().Completion switch { CompletionState.Completed => 1, CompletionState.Failed => -1, _ => 0 };
        };
        // The money reward of the latest Courier Job in the log, or -1.
        Autotest.Fields["ev_courier_pay"] = () => RequireRun().Mode.Missions.LastOrDefault(m => m.MetatypeID.Equals(CourierType))?.Rewards
            .OfType<Cosmoteer.Modes.Career.Missions.Rewards.MoneyReward>().Sum(m => m.Money) ?? -1;
        Autotest.Commands["ev_courier_pay_mark"] = _ =>
        {
            double at = Autotest.Fields["ev_courier_pay"]();
            if (at <= 0) throw new InvalidOperationException("no Courier Job pay to mark");
            Autotest.Fields["ev_courier_pay_ratio"] = () => Autotest.Fields["ev_courier_pay"]() / at;
        };
        // 1: the Courier Job's hand-over is done, 0: open, -1: failed, -2: no Courier Job in the log.
        Autotest.Fields["ev_courier_handover"] = () =>
        {
            var mission = RequireRun().Mode.Missions.LastOrDefault(m => m.MetatypeID.Equals(CourierType));
            if (mission is null) return -2;
            var found = new List<HandOverObjective>();
            mission.GetObjectives(found);
            return found.Single().Completion switch { CompletionState.Completed => 1, CompletionState.Failed => -1, _ => 0 };
        };
        // 1: the latest hand-over objective in the log (any mission) is done, 0: open, -1: failed, -2: none.
        Autotest.Fields["ev_handover"] = () =>
        {
            var found = new List<HandOverObjective>();
            foreach (Mission m in RequireRun().Mode.Missions) m.GetObjectives(found);
            return found.LastOrDefault()?.Completion switch { null => -2, CompletionState.Completed => 1, CompletionState.Failed => -1, _ => 0 };
        };
        // ev_crew_remove <n>: takes n crew off the human ships (lowest ship ID first), so bunks are free. Test only.
        Autotest.Commands["ev_crew_remove"] = arg =>
        {
            var souls = Tech.Vaults.HumanShips(RequireSim()).OrderBy(sh => sh.UniqueID).SelectMany(sh => sh.Crew.Assigned)
                .Where(c => c.CarriedResource.Quantity == 0).Take(Int(arg)).ToList();
            foreach (var soul in souls) soul.Delete(killed: false);
            Autotest.Wait(1);
        };
        // ev_goto <tag>: moves the fleet next to the first ship with the tag, live or in stasis, junk included (a salvage capsule). Test only.
        Autotest.Commands["ev_goto"] = arg =>
        {
            var sim = RequireSim();
            var tag = new ID<SimObjectSpawner>(arg.Trim());
            // TaggedShips leaves live junk out, so a capsule that already left stasis is found here.
            var target = SimQueries.TaggedShips(sim, tag).FirstOrDefault();
            if (target == default && sim.Ships.Where(sh => sh.HasTag(tag)).OrderBy(sh => sh.UniqueID).FirstOrDefault() is { } junk)
                target = new(junk.UniqueID, junk.Metadata.PlayerIndex, junk.DetWorldCenter, junk);
            if (target == default) throw new InvalidOperationException($"no ship tagged {arg}");
            MoveNextTo(sim, target.Location, target.Live?.WorldBoundingRadius + 150f ?? 600f);
        };
        // ev_contact_goto: moves the fleet next to the contact of the first open hand-over (live or in stasis). Test only.
        Autotest.Commands["ev_contact_goto"] = _ =>
        {
            var h = OpenHandOvers().FirstOrDefault() ?? throw new InvalidOperationException("no open hand-over");
            var at = h.Contact.Location ?? throw new InvalidOperationException("the hand-over contact is not in this system");
            MoveNextTo(RequireSim(), at, h.Contact.TargetShip?.WorldBoundingRadius + 300f ?? 1200f);
        };
        // ev_deliver_ship: moves the delivered ship of the first open hand-over next to its contact, and the fleet beside it. Test only.
        Autotest.Commands["ev_deliver_ship"] = _ =>
        {
            var h = OpenHandOvers().FirstOrDefault(o => o.DeliveredShipId is not null) ?? throw new InvalidOperationException("no open hand-over with a ship to deliver");
            var ship = RequireSim().Ships[h.DeliveredShipId!.Value] ?? throw new InvalidOperationException("the ship to deliver is not live; run ev_goto on it first");
            var at = h.Contact.Location ?? throw new InvalidOperationException("the hand-over contact is not in this system");
            // The contact can be in stasis (no size known): 900 from its center clears a trade station and stays within DeliveryRadius.
            float offset = h.Contact.TargetShip?.WorldBoundingRadius + 300f ?? 900f;
            Autotest.Teleport(ship, at + new Vector2(offset + ship.WorldBoundingRadius, 0));
            MoveNextTo(RequireSim(), at, offset + 2 * ship.WorldBoundingRadius + 200f);
            // A contact that was in stasis loads now; place the ship again against its real hull.
            Autotest.WaitFor(() => h.Contact.TargetShip is not null, 15, "the hand-over contact is live", () =>
            {
                var contact = h.Contact.TargetShip!;
                Autotest.Teleport(ship, contact.WorldCenter + new Vector2(contact.WorldBoundingRadius + ship.WorldBoundingRadius + 300f, 0));
                Autotest.Wait(2);
            });
        };
        // ev_delivered_crew <n>: n > 0 puts n player crew on the delivered ship (it must be a player ship); 0 takes them all off. Test only.
        Autotest.Commands["ev_delivered_crew"] = arg =>
        {
            var h = OpenHandOvers().FirstOrDefault(o => o.DeliveredShipId is not null) ?? throw new InvalidOperationException("no open hand-over with a ship to deliver");
            var ship = RequireSim().Ships[h.DeliveredShipId!.Value] ?? throw new InvalidOperationException("the ship to deliver is not live");
            int n = Int(arg);
            if (n > 0) { if (ship.Crew.CreateCrewsAtRandomParts(n) != n) throw new InvalidOperationException($"{ship.Metadata.PlainShipName} has no room for {n} crew"); }
            else foreach (var soul in ship.Crew.Assigned.ToList()) soul.Delete(killed: false);
            Autotest.Wait(1);
        };
        // ev_take <tag> <resource>: moves the resource out of the live tagged ship's storage into the fleet, as crew carrying
        // salvage would. Fails when the ship holds none. Test only.
        Autotest.Commands["ev_take"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var tag = new ID<SimObjectSpawner>(parts[0]);
            // Junk too (a salvage capsule): TaggedShips leaves wrecks out.
            Ship? Live() => RequireSim().Ships.Where(sh => sh.HasTag(tag)).OrderBy(sh => sh.UniqueID).FirstOrDefault();
            // A ship that a teleport brought into range leaves stasis a few ticks later.
            Autotest.WaitFor(() => Live() is not null, 15, $"a live ship tagged {parts[0]}", () => Take(Live()!, new ID<Cosmoteer.Resources.ResourceRules>(parts[1])));
        };
        static void Take(Ship source, ID<Cosmoteer.Resources.ResourceRules> rid)
        {
            var sim = RequireSim();
            int taken = source.Resources.RemoveResources(rid, 100);
            if (taken == 0) throw new InvalidOperationException($"{source.Metadata.PlainShipName} holds no {rid}");
            int left = taken;
            foreach (var ship in Tech.Vaults.HumanShips(sim).OrderBy(sh => sh.UniqueID)) left -= ship.Resources.AddResources(rid, left, excludeConstructionSinks: true);
            if (left > 0) throw new InvalidOperationException($"no fleet storage for {left} {rid}");
            Autotest.Wait(1);
        }
        // ev_storage_fill <resource>: fills every human ship's storage with the resource. ev_storage_clear <resource>: takes all of it out. Test only.
        Autotest.Commands["ev_storage_fill"] = arg =>
        {
            var rid = new ID<Cosmoteer.Resources.ResourceRules>(arg.Trim());
            foreach (var ship in Tech.Vaults.HumanShips(RequireSim()))
                while (ship.Resources.AddResources(rid, 100, excludeConstructionSinks: true) > 0) { }
            Autotest.Wait(1);
        };
        Autotest.Commands["ev_storage_clear"] = arg =>
        {
            var rid = new ID<Cosmoteer.Resources.ResourceRules>(arg.Trim());
            foreach (var ship in Tech.Vaults.HumanShips(RequireSim()))
                while (ship.Resources.RemoveResources(rid, 100) > 0) { }
            Autotest.Wait(1);
        };
        // Item actions (spec 1111 to 1118). Test only.
        // item_give <resource>: one unit into the acting player's first ship with room.
        Autotest.Commands["item_give"] = arg =>
        {
            var rid = new ID<Cosmoteer.Resources.ResourceRules>(arg.Trim());
            var sim = RequireSim();
            var ship = PlayerShips(sim).FirstOrDefault(s => s.Resources.AddResources(rid, 1, excludeConstructionSinks: true) == 1)
                ?? throw new InvalidOperationException($"no ship of player {ActingPlayer()} has room for {rid}");
            Log.Info($"AUTOTEST {rid} into {ship.Metadata.PlainShipName}");
        };
        // item_action <resource> <action>: the acting player uses the action on its first storage part that holds the resource (MP input).
        Autotest.Commands["item_action"] = arg =>
        {
            var p = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (p.Length != 2) throw new InvalidOperationException("usage: item_action <resource> <action>");
            var rid = new ID<Cosmoteer.Resources.ResourceRules>(p[0]);
            var part = ItemPart(RequireSim(), rid) ?? throw new InvalidOperationException($"no storage part of player {ActingPlayer()} holds {rid}");
            RequireRun().Game.NetManager.EnqueueInput(new ItemActionInput(part, rid, p[1]));
            Autotest.Wait(1);
        };
        // item_select <resource>: selects the local player's storage part that holds the resource, or clears the selection ("none").
        // The field item_button is 1 while the item's action button shows for that selection.
        Autotest.Commands["item_select"] = arg =>
        {
            var sim = RequireSim();
            if (arg.Trim() == "none") { sim.PlayerInput.ClearSelectedParts(); return; }
            var rid = new ID<Cosmoteer.Resources.ResourceRules>(arg.Trim());
            _selectedItem = rid;
            sim.PlayerInput.SelectSinglePart(ItemPart(sim, rid) ?? PlayerShips(sim).SelectMany(s => s.Parts).OrderBy(x => x.Location.X).ThenBy(x => x.Location.Y)
                .First(x => Cosmoteer.Ships.Parts.Resources.FlexResourceGrid.GetAllOnPart(x).Any()));
        };
        Autotest.Fields["item_button"] = () =>
            _selectedItem is { } r && ItemActionButton.Target(RequireRun().Game, r.GetResourceRules()) is not null ? 1 : 0;
        // Rescue Beacon (spec 1127). Test only.
        // ev_money_zero: the acting player's money goes to 0. ev_rescue_tip: 1 when the fire action's tooltip lines show the fee.
        Autotest.Commands["ev_money_zero"] = _ =>
        {
            var state = RequireRun().Game.GetPlayerState(ActingPlayer(), deterministic: true);
            state.AddMoney(-state.Money);
        };
        Autotest.Fields["ev_rescue_tip"] = () =>
        {
            var run = RequireRun();
            var part = ItemPart(RequireSim(), new ID<Cosmoteer.Resources.ResourceRules>("yarden_zamir.rescue_beacon")) ?? throw new InvalidOperationException("no rescue beacon aboard");
            string tip = ItemActions.Handlers["rescue"].ToolTip(run, part) ?? "";
            Log.Info($"AUTOTEST rescue tip: {tip}");
            return tip.Contains(RescueBeacon.Fee(run).ToString("N0"), StringComparison.Ordinal) ? 1 : 0;
        };
        Autotest.Fields["ev_rescues"] = () => RequireRun().State.Events.Rescues.Count(r => r.LeaveAt < 0);
        Autotest.Fields["ev_rescue_pulling"] = () => RequireRun().State.Events.Rescues.Any(r => r.Pulling && r.LeaveAt < 0) ? 1 : 0;
        Autotest.Fields["ev_rescue_arrivals"] = () => RescueBeacon.Arrivals;
        // The first running rescue: the towed ship's distance to its station, and the tug's speed.
        Autotest.Fields["ev_rescue_dist"] = () => RequireRun().State.Events.Rescues.FirstOrDefault(x => x.LeaveAt < 0) is { } r && RequireSim().Ships[r.Towed] is { } t
            ? t.DetWorldCenter.DistanceTo(r.Destination) : -1;
        Autotest.Fields["ev_tug_dist"] = () => RequireRun().State.Events.Rescues.FirstOrDefault(x => x.LeaveAt < 0) is { } r && RequireSim().Ships[r.Tug] is { } tug
            ? tug.DetWorldCenter.DistanceTo(r.Destination) : -1;
        Autotest.Fields["ev_tug_speed"] = () => RequireRun().State.Events.Rescues.FirstOrDefault(x => x.LeaveAt < 0) is { } r && RequireSim().Ships[r.Tug] is { } tug
            ? tug.Physics.Body.LinearVelocity.Length() : -1;
        // ev_rescue_camera: test only. The camera stops following ships and frames the tug and the towed ship (screenshots).
        Autotest.Commands["ev_rescue_camera"] = _ =>
        {
            var sim = RequireSim();
            var r = RequireRun().State.Events.Rescues.FirstOrDefault(x => x.LeaveAt < 0) ?? throw new InvalidOperationException("no running rescue");
            var tug = sim.Ships[r.Tug] ?? throw new InvalidOperationException("the tug is not live");
            var towed = sim.Ships[r.Towed] ?? throw new InvalidOperationException("the towed ship is not live");
            var view = sim.Camera.ViewController;
            view.ClearFocus(clearFramesOfReferenceAndRotation: true);
            view.FRLocation = (tug.WorldCenter + towed.WorldCenter) / 2f;
            float size = tug.WorldCenter.DistanceTo(towed.WorldCenter) + tug.WorldBoundingRadius + towed.WorldBoundingRadius + 100f;
            view.ZoomToFit(new Vector2(size, size), instant: true);
        };
        // ev_tug_kill: removes the tug of the first running rescue, as if it was destroyed.
        Autotest.Commands["ev_tug_kill"] = _ =>
        {
            var sim = RequireSim();
            var r = RequireRun().State.Events.Rescues.FirstOrDefault(x => x.LeaveAt < 0) ?? throw new InvalidOperationException("no running rescue");
            sim.Ships.Remove(sim.Ships[r.Tug] ?? throw new InvalidOperationException("the tug is not live"), dispose: true, handleLostShip: false);
            Autotest.Wait(1);
        };
        // item_force <n>: the next item action picks content n (1-based). ev_force_outcome <n>: the next event roll picks outcome n.
        Autotest.Commands["item_force"] = arg => ItemActions.ForcedContent = int.Parse(arg.Trim()) - 1;
        Autotest.Commands["ev_force_outcome"] = arg => EventRunner.ForcedOutcome = int.Parse(arg.Trim()) - 1;
        // ev_contact_mark remembers the courier contact. ev_contact_hostile: 1 while it is live and an enemy of the players.
        // ev_contact_gone: 1 once it left the system.
        Autotest.Commands["ev_contact_mark"] = _ => _markedContact = CourierContact(RequireSim()).Id;
        Autotest.Fields["ev_contact_hostile"] = () =>
        {
            var sim = RequireSim();
            var id = _markedContact ?? throw new InvalidOperationException("ev_contact_mark first");
            return sim.Ships[id] is { } c && SimQueries.FirstHumanShip(sim) is { } h && c.IsEnemiesWith(h) ? 1 : 0;
        };
        Autotest.Fields["ev_contact_gone"] = () =>
        {
            var sim = RequireSim();
            var id = _markedContact ?? throw new InvalidOperationException("ev_contact_mark first");
            return sim.Ships[id] is null && !sim.Stasis.Any(st => st.ReservedObjectID == id) ? 1 : 0;
        };
        Autotest.Commands["ev_courier_goto"] = _ =>
        {
            var sim = RequireSim();
            var contact = CourierContact(sim);
            Log.Info($"AUTOTEST courier contact {contact.Id} live={contact.Live is not null} at {contact.Location}");
            // Ship sight is short (a small control room sees 400), and a cloudy nebula cuts it to 30 % (about 120). The contact can spawn
            // in one, so the ships stop 40 outside its hull: then it is in sight in every nebula.
            float offset = (contact.Live?.WorldBoundingRadius ?? 300f) + 40f;
            foreach (var s in sim.Ships.HumanPlayerSignificantShips.ToList())
            {
                Autotest.Teleport(s, contact.Location + new Halfling.Geometry.Vector2(offset + s.WorldBoundingRadius, 0));
                offset += 2 * s.WorldBoundingRadius + 100f;
            }
            Autotest.Wait(3);
        };
        Autotest.Fields["ev_courier_seen"] = () =>
        {
            var run = RequireRun();
            if (CourierContact(RequireSim()).Live is not { } ship) return -1;
            int team = run.Game.GetPlayerState(run.Game.LocalPlayerIndex, deterministic: true).Team;
            return ship.IsWithinSightOfTeam(team) ? 1 : 0;
        };
        // Reputation with the faction that controls the system, for the Reputation outcome effect.
        Autotest.Fields["ev_rep"] = () =>
        {
            var faction = RequireSim().MapNode?.ControllingFaction ?? throw new InvalidOperationException("no faction controls this system");
            return RequireRun().Mode.GetFactionReputation(faction);
        };
        Autotest.Commands["ev_rep_mark"] = _ => { double at = Autotest.Fields["ev_rep"](); Autotest.Fields["ev_rep_gain"] = () => Autotest.Fields["ev_rep"]() - at; };
        // Tow Job (spec 88): state of the latest tow (0 in progress, 1 done, 2 failed), and moves that skip the actual towing.
        Autotest.Fields["ev_tow_state"] = () => RequireTow().Completion switch
        {
            CompletionState.InProgress => 0,
            CompletionState.Completed => 1,
            CompletionState.Failed => 2,
            var c => throw new InvalidOperationException($"unexpected tow state {c}"),
        };
        Autotest.Fields["ev_tow_dist"] = () => RequireTow() is var o && o.Towed.Location is { } at ? at.DistanceTo(o.Destination) : -1;
        // ev_spawn <generator>: runs a generator from spawns.rules around the fleet, like a Spawn outcome. Test only.
        Autotest.Commands["ev_spawn"] = arg =>
        {
            var sim = RequireSim();
            var at = SimQueries.FleetCenter(sim) ?? throw new InvalidOperationException("no player ships");
            EventLibrary.Spawn(arg).Generate(sim, new Halfling.Random.FastRand(4321UL), loadInstantly: true, rootLoc: at, addExistingSpawnTags: true);
            Autotest.Wait(2);
        };
        // expectviewtext <text>: the page that the dialog shows now contains the text (tags removed).
        Autotest.Commands["expectviewtext"] = arg =>
        {
            var page = CurrentView() ?? throw new InvalidOperationException("no event page");
            string plain = EventView.Plain(page.Body) + "\n" + string.Join("\n", page.Choices.Select(c => EventView.Plain(c.Text)));
            Autotest.Check(plain.Contains(arg, StringComparison.Ordinal), $"event page shows '{arg}'");
        };
        // ev_tow_arrive: puts the towed ship and the player ships next to the destination station.
        Autotest.Commands["ev_tow_arrive"] = _ =>
        {
            var o = RequireTow();
            var towed = o.Towed.TargetShip ?? throw new InvalidOperationException("the towed ship is not live");
            var sim = RequireSim();
            Vector2 at = o.Destination + new Vector2(o.ArrivalRadius * 0.5f, 0);
            Autotest.Teleport(towed, at);
            foreach (var s in sim.Ships.HumanPlayerSignificantShips.ToList()) Autotest.Teleport(s, at + new Vector2(towed.WorldBoundingRadius + s.WorldBoundingRadius + 100f, 0));
            Autotest.Wait(2);
        };
        Autotest.Commands["ev_tow_kill"] = _ =>
        {
            var towed = RequireTow().Towed.TargetShip ?? throw new InvalidOperationException("the towed ship is not live");
            RequireSim().Ships.Remove(towed, dispose: true, handleLostShip: false);
            Autotest.Wait(2);
        };
        // Wanted (spec 470-472): vanilla wanted status in this system, and the saved warning state.
        Autotest.Fields["ev_wanted"] = () => ((CareerSimModeManager)RequireSim().Mode).Wanted.IsLocalPlayerWanted ? 1 : 0;
        Autotest.Fields["ev_wanted_warned"] = () => RequireRun().State.Events.WantedWarned ? 1 : 0;
        // Research offers (spec 760-763): the pending page's offer, and the researched tech count.
        Autotest.Fields["ev_offer_units"] = () => RequirePending().OfferUnits;
        Autotest.Fields["ev_offer_techs"] = () => RequirePending().OfferTechs.Length;
        Autotest.Fields["ev_offer_value"] = () => RequirePending().OfferValue;
        Autotest.Fields["ev_offer_price"] = () => RequirePending().OfferPrice;
        // Research value per tech value of the units asked: must be at least Events.ResearchMinRate.
        Autotest.Fields["ev_offer_rate"] = () => RequirePending() is var p && p.OfferUnits > 0 ? p.OfferValue / (double)(p.OfferUnits * p.TechValue) : 0;
        Autotest.Commands["ev_researched_mark"] = _ => { double at = Autotest.Fields["ev_researched"](); Autotest.Fields["ev_researched_gain"] = () => Autotest.Fields["ev_researched"]() - at; };
        // Sale price per research worth: must be above 1 (Events.ResearchSalePremium).
        Autotest.Fields["ev_offer_premium"] = () => RequirePending() is var p && p.OfferValue > 0 ? p.OfferPrice / (double)p.OfferValue : 0;
        Autotest.Fields["ev_researched"] = () => GameApp.Rules.CareerMode.Techs.Count(t => RequireRun().Mode.Techs.IsTechUnlocked(t.ID));
        // Relic ship (spec 765).
        Autotest.Fields["ev_relic_sector"] = () => RequireRun().State.Events.RelicSector;
        Autotest.Fields["ev_relic_dist"] = () =>
        {
            var sim = RequireSim();
            var found = Abandoned.RelicLocations(sim);
            if (found.Count == 0) return -2;
            return sim.Ships.HumanPlayerSignificantShips.Min(h => h.DetWorldCenter.DistanceTo(found[0].Location) - h.WorldBoundingRadius) - found[0].Radius;
        };
        Autotest.Fields["ev_relic_messaged"] = () => RequireRun().State.Events.RelicMessaged ? 1 : 0;
        Autotest.Commands["ev_relic_spawn"] = _ => { Abandoned.SpawnRelic(RequireRun(), RequireSim()); Autotest.Wait(2); };
        // ev_relic_goto <distance>: puts the player ships this far from the relic's hull (two calls: the first loads it out of stasis).
        Autotest.Commands["ev_relic_goto"] = arg =>
        {
            var sim = RequireSim();
            var found = Abandoned.RelicLocations(sim);
            if (found.Count == 0) throw new InvalidOperationException("no relic ship in this system");
            MoveNextTo(sim, found[0].Location, found[0].Radius + float.Parse(arg, CultureInfo.InvariantCulture));
        };
        // ev_abandoned: spawns a vanilla abandoned ship (the sector generator's own spawner) 1,500 from the fleet; ev_abandoned_goto flies next to it.
        Autotest.Commands["ev_abandoned"] = _ => SpawnVanillaAbandoned();
        Autotest.Commands["ev_abandoned_goto"] = _ =>
        {
            var sim = RequireSim();
            var wreck = sim.Ships.Where(s => s.HasLabel(Abandoned.Label) && !s.HasTag(Abandoned.RelicTag)).OrderBy(s => s.UniqueID).FirstOrDefault()
                ?? throw new InvalidOperationException("no live abandoned ship in this system");
            MoveNextTo(sim, wreck.WorldCenter, wreck.WorldBoundingRadius + 150f);
        };
        // Comms (spec 767-771). ev_comms <target> <button text>: clicks the comms button whose plain text contains the text.
        Autotest.Commands["ev_comms"] = arg =>
        {
            int space = arg.IndexOf(' ');
            if (space < 0) throw new InvalidOperationException("ev_comms needs: target text");
            string which = arg[..space], text = arg[(space + 1)..];
            var page = CommsPage(which) ?? throw new InvalidOperationException($"the {which} ship shows no Hot Cargo comms page");
            var button = page.Buttons.FirstOrDefault(b => EventView.Plain(b.Text).Contains(text, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"no comms button with '{text}' on the {which} ship; buttons: {string.Join(" | ", page.Buttons.Select(b => EventView.Plain(b.Text)))}");
            if (!button.SelfInputActive) throw new InvalidOperationException($"comms button '{text}' is disabled");
            // A shared choice gets its click from the comms tab's shared choice panel; headless, send what the panel sends (first pick).
            if (button is Ui.HailChoice choice && page.Shared is { } shared)
                RequireRun().Game.NetManager.EnqueueInput(new SharedChoice.SharedChoiceInput(shared.Domain, shared.Serial, SharedChoice.SharedChoiceAction.Choose, choice.Option));
            else button.OnClick?.Invoke(null, EventArgs.Empty);
            Autotest.Wait(2);
        };
        // ev_open <target>: opens the real comms window to the target. ev_click <target> <text>: runs one frame of its dialogue tab,
        // then clicks the tab button whose plain text contains the text, like a player (spec 769: the tab wires shared choices).
        Autotest.Commands["ev_open"] = arg =>
        {
            var game = RequireRun().Game;
            var caller = SimQueries.FirstHumanShip(RequireSim()) ?? throw new InvalidOperationException("no player ship");
            game.Gui.FloatingWindows.AddChild(new Cosmoteer.Modes.Career.Comms.CommWindow(game, caller, CommsTarget(arg.Trim())));
            Autotest.Wait(1);
        };
        Autotest.Commands["ev_click"] = arg =>
        {
            int space = arg.IndexOf(' ');
            if (space < 0) throw new InvalidOperationException("ev_click needs: target text");
            string which = arg[..space], text = arg[(space + 1)..];
            var tab = Ui.CommsHail.OpenTab(CommsTarget(which)) ?? throw new InvalidOperationException($"no open comms tab with the {which} ship (ev_open first)");
            tab.OnUpdatingUIState(null, EventArgs.Empty);
            var buttons = tab._currentDialogue?.Buttons ?? throw new InvalidOperationException($"the {which} comms tab shows no page");
            int index = buttons.FindIndex(b => EventView.Plain(b.Text).Contains(text, StringComparison.Ordinal));
            if (index < 0) throw new InvalidOperationException($"no comms button with '{text}' on the {which} tab; buttons: {string.Join(" | ", buttons.Select(b => EventView.Plain(b.Text)))}");
            if (!tab._dialogueButtonsBox.Children[index].SelfInputActive) throw new InvalidOperationException($"comms button '{text}' is disabled");
            tab.ButtonPressed(index);
            Autotest.Wait(2);
        };
        // expectcomms <target> <text> 1|0: the target's Hot Cargo comms page has (1) or lacks (0) a button or line with the text.
        Autotest.Commands["expectcomms"] = arg =>
        {
            var parts = arg.Split(' ');
            string which = parts[0], want = parts[^1], text = string.Join(' ', parts[1..^1]);
            var page = CommsPage(which);
            string all = page is null ? "" : EventView.Plain(page.PrimaryDialogue) + "\n" + string.Join("\n", page.Buttons.Select(b => EventView.Plain(b.Text)));
            bool has = all.Contains(text, StringComparison.Ordinal);
            Autotest.Check(has == (want == "1"), $"{which} comms {(want == "1" ? "show" : "do not show")} '{text}'");
        };
        Autotest.Fields["ev_carrier"] = () => RequireRun().State.Events.Pending?.Carrier is null ? 0 : 1;
        // 1 when the pending page's carrier is hailing the local player (vanilla hailer list).
        Autotest.Fields["ev_hailing"] = () =>
        {
            var run = RequireRun();
            if (run.State.Events.Pending?.Carrier is not { } id) return 0;
            var mode = (CareerSimModeManager)RequireSim().Mode;
            return mode.ShipsHailingPlayer.TryGetValue(run.Game.LocalPlayerIndex, out var list) && list.Any(h => h.Item1.UniqueID == id) ? 1 : 0;
        };
        Autotest.Commands["ev_carrier_goto"] = _ =>
        {
            var ship = CommsTarget("carrier");
            MoveNextTo(RequireSim(), ship.WorldCenter, ship.WorldBoundingRadius + 100f);
        };
        Autotest.Commands["ev_kill_carrier"] = _ =>
        {
            var ship = CommsTarget("carrier");
            RequireSim().Ships.Remove(ship, dispose: true, handleLostShip: false);
            Autotest.Wait(2);
        };
        Autotest.Fields["ev_buyer_mult"] = () => RequireRun().State.BuyerPriceMultiplier;
        Autotest.Fields["ev_pending"] = () => RequireRun().State.Events.Pending is null ? 0 : 1;
        Autotest.Fields["ev_seen"] = () => RequireRun().State.Events.Seen.Values.Sum();
        Autotest.Fields["ev_result"] = () => RequireRun().State.Events.LastResult?.Serial ?? 0;
        // 1 when the page that was pending at the last ev_mark has a result.
        Autotest.Fields["ev_answered"] = () => MarkedSerial > 0 && RequireRun().State.Events.LastResult?.Serial >= MarkedSerial ? 1 : 0;
        Autotest.Fields["ev_signals"] = () => RequireSim().Doodads.Count(d => d.Tags?.Contains(EventRunner.SignalTag) == true);
        Autotest.Fields["ev_stdmoney"] = () => EventRunner.Credits(RequireRun(), 1f, 0f, RequireRun().CurrentTier, 0);
    }

    static PendingEvent RequirePending() => RequireRun().State.Events.Pending ?? throw new InvalidOperationException("no pending event");

    static void MoveNextTo(SimRoot sim, Vector2 center, float offset)
    {
        foreach (var s in sim.Ships.HumanPlayerSignificantShips.ToList())
        {
            Autotest.Teleport(s, center + new Vector2(offset + s.WorldBoundingRadius, 0));
            offset += 2 * s.WorldBoundingRadius + 100f;
        }
        Autotest.Wait(2);
    }

    static void SpawnVanillaAbandoned()
    {
        var sim = RequireSim();
        var mode = (CareerSimModeManager)sim.Mode;
        var spawner = All(mode.SectorType.SystemGenerator.Spawners).OfType<ShipSpawner>().FirstOrDefault(s => s.Labels?.Contains(Abandoned.Label) == true)
            ?? throw new InvalidOperationException("no vanilla abandoned-ship spawner in the sector generator");
        var at = SimQueries.FleetCenter(sim) ?? throw new InvalidOperationException("no player ships");
        // Test distance, restored after, like SpawnSignal.
        var saved = spawner.Distance;
        try
        {
            spawner.Distance = new Halfling.Range<float>(1500f, 1500f);
            new SimulationGenerator { Spawners = [spawner] }.Generate(sim, new Halfling.Random.FastRand(99UL), loadInstantly: true, rootLoc: at);
        }
        finally { spawner.Distance = saved; }
        Log.Info($"AUTOTEST vanilla abandoned ship spawned; abandoned ships: {sim.Ships.Count(s => s.HasLabel(Abandoned.Label))}");
        Autotest.Wait(2);
    }

    static readonly ID<SimObjectSpawner> CourierContactTag = new("hc_courier_contact");
    static readonly ID<MissionMetatype> CourierType = new("HotCargoCourier");

    static IEnumerable<(MissionList List, Mission Mission)> CourierOffers(SimRoot sim)
    {
        foreach (var station in Cosmoteer.Ships.Special.SpaceStation.GetStations(sim, onlyOperating: false))
            if (station.GetProvidedMissions() is { } list)
                foreach (Mission m in list)
                    if (m.MetatypeID.Equals(CourierType)) yield return (list, m);
    }

    /// <summary>The contact of the accepted Courier Job, else the first contact in the system. A system can hold two (a rolled and a forced offer).</summary>
    /// <summary>The live station whose mission list has the Courier Job offer.</summary>
    static Ship CourierStation()
    {
        var mission = CourierOffers(RequireSim()).Select(o => o.Mission).FirstOrDefault()
            ?? throw new InvalidOperationException("no Courier Job offer in this system");
        return Cosmoteer.Ships.Special.SpaceStation.GetStations(RequireSim(), onlyOperating: false)
            .FirstOrDefault(s => s.GetProvidedMissions()?.ContainsMission(mission) == true).PhysicalShip
            ?? throw new InvalidOperationException("the Courier Job's station is not live");
    }

    static Cosmoteer.Modes.Career.Comms.CommMissionsTab MissionsTab() =>
        Contracts.ContractsUi.AcceptButtonPatch.LastTab?.TryGetTarget(out var tab) == true ? tab : throw new InvalidOperationException("no missions tab; ev_courier_ui first");

    static Cosmoteer.Game.ObjectID? _markedContact;
    static Mission? _acceptedCourier;
    static ID<Cosmoteer.Resources.ResourceRules>? _selectedItem;

    /// <summary>The player that a test command acts for: the "as" player inside the sim, else the local player.</summary>
    static int ActingPlayer() => Autotest.SimPlayer ?? RequireRun().Game.LocalPlayerIndex;

    static IEnumerable<Ship> PlayerShips(SimRoot sim) =>
        Vaults.HumanShips(sim).Where(s => s.Metadata.PlayerIndex == ActingPlayer()).OrderBy(s => s.UniqueID);

    /// <summary>The acting player's first storage part, by ship ID and part location, that holds a unit of the item.</summary>
    static Cosmoteer.Ships.Parts.Part? ItemPart(SimRoot sim, ID<Cosmoteer.Resources.ResourceRules> item) =>
        PlayerShips(sim).SelectMany(s => s.Parts.OrderBy(x => x.Location.X).ThenBy(x => x.Location.Y)).FirstOrDefault(x => ItemActions.Holds(x, item));

    static SimQueries.TaggedShip CourierContact(SimRoot sim)
    {
        var contacts = SimQueries.TaggedShips(sim, CourierContactTag);
        if (contacts.Count == 0) throw new InvalidOperationException("no courier contact in this system");
        var accepted = HandOverObjective.Open(RequireRun()).Select(o => (Cosmoteer.Game.ObjectID?)o.Contact.ShipID).FirstOrDefault();
        if (accepted is not { } id) return contacts[0];
        int i = contacts.FindIndex(c => c.Id == id);
        return i >= 0 ? contacts[i] : throw new InvalidOperationException($"the accepted job's contact {id} is not in this system");
    }

    static IEnumerable<HandOverObjective> OpenHandOvers()
    {
        var found = new List<HandOverObjective>();
        foreach (Mission m in RequireRun().Mode.Missions) m.GetObjectives(found);
        return found.Where(o => o.Completion == CompletionState.InProgress);
    }

    static TowObjective? _lastTow;

    /// <summary>The latest tow objective in the mission log, or the last one seen: a completed mission leaves the log when it pays.</summary>
    static TowObjective RequireTow()
    {
        var found = new List<TowObjective>();
        foreach (Mission m in RequireRun().Mode.Missions)
            foreach (var o in m.Objectives) o.GetObjectives(found);
        _lastTow = found.LastOrDefault() ?? _lastTow;
        return _lastTow ?? throw new InvalidOperationException("no accepted mission has a HotCargoTow objective");
    }

    /// <summary>The pending page as either surface shows it (the event window, or a carrier's comms), or the last result.</summary>
    static EventPage? CurrentView()
    {
        var run = RequireRun();
        if (run.State.Events.Pending is { } p) return EventView.Pending(run, RequireSim(), p, run.Game.LocalPlayerIndex);
        return EventView.Current(run, RequireSim(), run.Game.LocalPlayerIndex, lastResultShown: int.MaxValue);
    }

    /// <summary>The ship a comms test talks to: carrier (the pending page's ship), contact (the Courier Job's contact), station (nearest trade station), faction (nearest faction ship).</summary>
    static Ship CommsTarget(string which)
    {
        var sim = RequireSim();
        var caller = SimQueries.FirstHumanShip(sim) ?? throw new InvalidOperationException("no player ship");
        Ship? found = which switch
        {
            "carrier" => RequireRun().State.Events.Pending?.Carrier is { } id ? sim.Ships[id] : null,
            // The contact of an open hand-over in the log (any mission, its turn or not), else the Courier Job's contact.
            "contact" => OpenHandOvers().FirstOrDefault() is { } h ? h.Contact.TargetShip : CourierContact(sim).Live,
            "station" => sim.Ships.Where(s => EventHail.TargetOf(s, caller) == HailTarget.Station).MinBy(s => s.DetWorldCenter.DistanceSquaredTo(caller.DetWorldCenter)),
            "faction" => sim.Ships.Where(s => EventHail.TargetOf(s, caller) == HailTarget.FactionShip).MinBy(s => s.DetWorldCenter.DistanceSquaredTo(caller.DetWorldCenter)),
            _ => throw new InvalidOperationException($"unknown comms target '{which}' (carrier, contact, station, faction)"),
        };
        return found ?? throw new InvalidOperationException($"no live {which} ship for comms");
    }

    /// <summary>The Hot Cargo comms page of a target, as the dialogue tab builds it (vanilla part left out).</summary>
    static Ui.HailPage? CommsPage(string which)
    {
        var caller = SimQueries.FirstHumanShip(RequireSim()) ?? throw new InvalidOperationException("no player ship");
        return Ui.CommsHail.Page(caller, CommsTarget(which), vanilla: null);
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
            yield return ($"loose_{id}", () => RequireSim().Nuggets.Where(n => n.ResourceType == id).Sum(n => n.Quantity));
        }
    }

    static int Tagged(SimRoot sim, string tagName)
    {
        var tag = new ID<SimObjectSpawner>(tagName);
        return SimQueries.TaggedShips(sim, tag).Count;
    }

    static void SpawnSignal()
    {
        var sim = RequireSim();
        var mode = (CareerSimModeManager)sim.Mode;
        var tag = new ID<SimObjectSpawner>("hc_group_signal");
        var group = All(mode.SectorType.SystemGenerator.Spawners).OfType<SimObjectSpawner>().FirstOrDefault(s => s.Tags?.Contains(tag) == true)
            ?? throw new InvalidOperationException("signal spawner group not found in the sector generator; are the event rules loaded?");
        var at = SimQueries.FleetCenter(sim) ?? throw new InvalidOperationException("no player ships");
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

using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.Random;
using HotCargo.Core;
using HotCargo.Events;
using HotCargo.Missions;

namespace HotCargo.Chains;

/// <summary>
/// Chain simulation logic: station assignment, stage missions and pages, results and their consequences.
/// Everything here runs from deterministic sources only: Run.Hooks (sim tick, system switch), the mission log's MissionRemoved event
/// (sim logic and MP inputs) and the event choice MP input (EventRunner.OutcomeChosen).
/// </summary>
public static class ChainRunner
{
    static readonly ID<SimObjectSpawner> TradeTag = new("trade_station");
    static readonly ID<SimObjectSpawner> MilitaryTag = new("military_station");
    static readonly ID<SimObjectSpawner> BuyerTag = new("hc_buyer");
    const float CheckSeconds = 0.5f;

    static MissionManager? _watched;

    static ChainsState State(Run run) => run.State.Chains;

    static bool Off(Run run) => !run.T.Chains.Enabled || ChainLibrary.LoadError is not null || EventLibrary.LoadError is not null;

    internal static void OnSystemEntered(Run run, bool firstVisit) => Errors.Guard("chain system switch", () =>
    {
        if (Off(run)) return;
        ObjectID node = run.Game.CurrentMapNode.UniqueID;
        foreach (var c in State(run).Instances.Where(i => !i.Ended))
        {
            c.HoldUntilNextVisit = false;
            bool here = c.NodeId == node;
            if (!here) c.Away = true;
            var stage = ChainLibrary.Get(c.ChainId).Stage(c.Stage);
            bool due = stage.Where switch
            {
                StageWhere.Return => here && c.Away,
                StageWhere.Arrival => firstVisit && run.State.SectorIndex >= c.StageSector + stage.AfterSectors,
                _ => false,
            };
            if (due && c.DueSeconds < 0) c.DueSeconds = run.State.GameSeconds + run.T.Chains.LaterDelaySeconds;
        }
    });

    internal static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("chain tick", () =>
    {
        if (Off(run) || sim != run.Game.Sim) return;
        Watch(run);
        double now = run.State.GameSeconds;
        // Checks run twice a game second. The step comes from game time, so every client checks on the same tick.
        if (Math.Floor(now / CheckSeconds) == Math.Floor((now - dt) / CheckSeconds)) return;
        _ = ChainLibrary.InOrder;
        AssignStations(run, sim);
        foreach (var c in State(run).Instances.ToList())
        {
            if (c.Ended) continue;
            Step(run, sim, c);
        }
    });

    /// <summary>One check of one chain. At most one page opens per check, because the events area holds one pending page.</summary>
    static void Step(Run run, SimRoot sim, ChainInstance c)
    {
        var events = run.State.Events;
        if (c.PendingResult.Length > 0)
        {
            // A result shows in the event window, so it waits until no page is open.
            if (events.Pending is null) Resolve(run, sim, c, c.PendingResult, resultSerial: 0, MissionPayer(run));
            return;
        }
        if (c.AwaitingSerial > 0)
        {
            // The page went away without a chain result (a content error dropped it). It opens again later.
            if (events.Pending?.Serial != c.AwaitingSerial) c.AwaitingSerial = 0;
            return;
        }
        var stage = ChainLibrary.Get(c.ChainId).Stage(c.Stage);
        bool here = sim.NodeId() == c.NodeId;
        if (c.MissionId is { } id)
        {
            if (here || stage.Where != StageWhere.Station) CheckMissionGone(run, sim, c, id);
            return;
        }
        bool due = c.DueSeconds >= 0 && run.State.GameSeconds >= c.DueSeconds;
        switch (stage.Kind, stage.Where)
        {
            case (StageKind.Mission, StageWhere.Station):
                if (here && !c.HoldUntilNextVisit && FindStation(sim, c.StationId) is { } station) SpawnMission(run, sim, c, stage, station.DetWorldCenter);
                else if (here && FindStation(sim, c.StationId) is null) EndStationGone(c);
                break;
            case (StageKind.Mission, _):
                if (!due) break;
                c.DueSeconds = -1;
                Vector2 at = (here ? FindStation(sim, c.StationId)?.DetWorldCenter : null) ?? SimQueries.FleetCenter(sim) ?? throw new InvalidOperationException("no human ships");
                SpawnMission(run, sim, c, stage, at);
                break;
            case (StageKind.Page, StageWhere.Station):
                if (here && !c.HoldUntilNextVisit && events.Pending is null && PlayerNear(run, sim, c)) OpenPage(run, sim, c, stage);
                break;
            case (StageKind.Page, _):
                if (!due || events.Pending is not null) break;
                c.DueSeconds = -1;
                OpenPage(run, sim, c, stage);
                break;
        }
    }

    // ---- Stations

    /// <summary>Gives every eligible station of the current system a chain, once per system.</summary>
    static void AssignStations(Run run, SimRoot sim)
    {
        var s = State(run);
        ObjectID node = sim.NodeId();
        if (s.AssignedNodes.Contains(node)) return;
        s.AssignedNodes.Add(node);
        int started = 0;
        var stations = Stations(sim).OrderBy(st => st.StationID).ToList();
        // Spec 160: in sector 1 the station nearest to the fleet (the first one the players meet) always offers a chain.
        // If no chain fits it, the next nearest takes its place.
        if (run.State.SectorIndex == 1 && SimQueries.FleetCenter(sim) is { } at && run.T.Chains.MaxNewChainsPerSystem > 0)
            foreach (var station in stations.OrderBy(st => st.DetWorldCenter.DistanceSquaredTo(at)).ThenBy(st => st.StationID))
                if (PickChain(run, sim, station) is { } first)
                {
                    Start(run, sim, first, station);
                    stations.Remove(station);
                    started++;
                    break;
                }
        foreach (var station in stations)
        {
            if (started >= run.T.Chains.MaxNewChainsPerSystem) break;
            if (sim.Rand.Single() >= run.T.Chains.StationChance) continue;
            if (PickChain(run, sim, station) is { } chain) { Start(run, sim, chain, station); started++; }
        }
    }

    /// <summary>Trade and military stations of a faction that give missions and are not enemies of the players. The Buyer is left out.</summary>
    internal static IEnumerable<MetaSpaceStation> Stations(SimRoot sim) =>
        SpaceStation.GetStations(sim, onlyOperating: false).Where(st => Kind(st) is not null && Eligible(sim, st));

    static bool Eligible(SimRoot sim, MetaSpaceStation st)
    {
        var tags = Tags(st);
        if (tags?.Contains(BuyerTag) == true || st.GetProvidedMissions() is null) return false;
        int player = PlayerIndex(st);
        return FactionRules.GetFaction(player) is not null && !sim.RequireGame().HumanPlayersIndexes.Any(h => sim.Mode.AreEnemies(player, h));
    }

    static HashSet<ID<SimObjectSpawner>>? Tags(MetaSpaceStation st) => st.PhysicalShip?.Tags ?? (st.Spawner is { } sp ? SimQueries.StasisTags(sp) : null);

    static int PlayerIndex(MetaSpaceStation st) => st.PhysicalShip?.Metadata.PlayerIndex ?? st.Spawner!.ObjectAllegiance;

    static StationKind? Kind(MetaSpaceStation st) => Tags(st) switch
    {
        { } t when t.Contains(TradeTag) => StationKind.Trade,
        { } t when t.Contains(MilitaryTag) => StationKind.Military,
        _ => null,
    };

    internal static MetaSpaceStation? FindStation(SimRoot sim, ObjectID id)
    {
        foreach (var st in SpaceStation.GetStations(sim, onlyOperating: false))
            if (st.StationID == id) return st;
        return null;
    }

    static bool PlayerNear(Run run, SimRoot sim, ChainInstance c)
    {
        if (FindStation(sim, c.StationId)?.PhysicalShip is not { IsJunk: false } ship) return false;
        float r = run.T.Chains.PageTriggerDistance;
        return sim.Ships.HumanPlayerSignificantShips.Any(h => h.DetWorldBoundingCircle.DistanceTo(ship.DetWorldBoundingCircle) <= r);
    }

    static ChainRules? PickChain(Run run, SimRoot sim, MetaSpaceStation station)
    {
        var s = State(run);
        int sector = run.State.SectorIndex;
        var kind = Kind(station);
        var fits = ChainLibrary.InOrder
            .Where(c => sector >= c.MinSector && (c.MaxSector <= 0 || sector <= c.MaxSector))
            .Where(c => c.Stations.Length == 0 || (kind is { } k && c.Stations.Contains(k)))
            .ToList();
        int pick = ChainModel.Pick(fits.Select(c => new ChainChoice(c.ID, c.Weight, s.Used.GetValueOrDefault(c.ID))).ToList(), sim.Rand.Single());
        return pick < 0 ? null : fits[pick];
    }

    /// <summary>Gives a station a chain at its first stage. Public for the autotest command that forces a chain.</summary>
    internal static ChainInstance Start(Run run, SimRoot sim, ChainRules chain, MetaSpaceStation station)
    {
        var s = State(run);
        s.Used[chain.ID] = s.Used.GetValueOrDefault(chain.ID) + 1;
        var c = new ChainInstance
        {
            ChainId = chain.ID,
            NodeId = sim.NodeId(),
            StationId = station.StationID,
            StationPlayerIndex = PlayerIndex(station),
            StageSector = run.State.SectorIndex,
        };
        s.Instances.Add(c);
        Advance(run, chain, c, chain.Stages[0].ID);
        Log.Info($"chain {chain.ID} starts at station {station.StationID} in {run.Game.CurrentMapNode.Name}");
        return c;
    }

    static void EndStationGone(ChainInstance c)
    {
        Log.Info($"chain {c.ChainId}: station {c.StationId} is gone; the chain ends");
        c.Story.Add("The station is gone.");
        c.Stage = "";
    }

    // ---- Missions

    /// <summary>
    /// Runs the stage's mission generator at a location, like a system generator would, and marks the mission as a chain stage.
    /// Objects that already exist (with the IDs of ships in stasis) are visible to its tag searches, so it finds the station.
    /// Objects of earlier chain missions (hc_ch_ tags) are left out, so this mission's objectives count only its own ships.
    /// </summary>
    internal static void SpawnMission(Run run, SimRoot sim, ChainInstance c, StageRules stage, Vector2 at)
    {
        var chain = ChainLibrary.Get(c.ChainId);
        var mode = (CareerSimModeManager)sim.Mode;
        var flags = mode.GetSpawnFlags();
        var objects = MissionArea.ExistingSpawnTags(sim);
        foreach (var tag in objects.Keys.Where(k => k.ToString().StartsWith("hc_ch_", StringComparison.Ordinal)).ToList()) objects.Remove(tag);
        var rand = new FastRand(sim.Rand.UInt64());
        var parentTree = new List<SimSpawner.SpawnedObject> { new(at) };
        uint batch = 0, fleet = 0;
        foreach (var spawner in ChainLibrary.Generator(stage.Mission).Spawners)
            if (spawner.Conditions?.CheckConditions(sim, 0, at, flags) ?? true)
                spawner.Spawn(sim, rand, parentTree, 0, ref batch, ref fleet, loadInstantly: true, [], objects, flags, 0f);
        if (objects.PrevMission is not { } m)
        {
            // A placement or a tag search failed in this system, for example no room for a spawn. Try again on the next visit.
            Log.Info($"chain {c.ChainId} stage {c.Stage}: mission {stage.Mission} spawned no mission here; trying again next visit");
            c.HoldUntilNextVisit = true;
            return;
        }
        if (m.MetatypeID != ChainLibrary.Metatype) throw new InvalidDataException($"mission '{stage.Mission}' made a {m.MetatypeID} mission, not {ChainLibrary.Metatype}");
        m.AddReward(new ChainStageReward(chain.Title, stage.Title, stage.Text, ChainModel.StorySoFar(c.Story, run.T.Chains.StoryLines), c.Part + 1));
        // Contract terms (economy area). Return and Arrival missions carry zero terms in missions.rules, like event missions (spec 86).
        if (stage.Terms is { } terms) m.AddReward(terms.CreateReward(m, rand, objects, at, null, null) ?? throw new InvalidOperationException("contract terms made no carrier"));
        // The mission name and description cache their text on first read. A read before the chain mark existed left the raw
        // "{chain_tag} {chain_name}" in the comms list, so they format again now (the event that the game raises for new objectives).
        m.OnMissionDisplayLinesChanged(null, EventArgs.Empty);
        c.MissionId = m.MissionID;
        c.MissionSince = run.State.GameSeconds;
        Log.Info($"chain {c.ChainId} stage {c.Stage}: mission {stage.Mission} spawned ({m.MissionID})");
        // A Return or Arrival mission skips the comms offer, so the event window tells the players why ships come for them.
        if (stage.Where != StageWhere.Station && run.State.Events.Pending is null)
        {
            var events = run.State.Events;
            events.LastResult = new EventResult { Serial = ++events.LastSerial, Title = $"{chain.Title}: {stage.Title}", Text = stage.Text };
        }
    }

    /// <summary>
    /// An offer that left the station's list without being accepted (declined, or expired because a target died) comes back on the next visit.
    /// A mission that left the log is handled by OnMissionRemoved. A mission that spawns into the log arrives a moment after it spawns.
    /// </summary>
    static void CheckMissionGone(Run run, SimRoot sim, ChainInstance c, ObjectID id)
    {
        if (run.Mode.Missions[id] is not null || run.State.GameSeconds - c.MissionSince < 2) return;
        if (FindStation(sim, c.StationId)?.GetProvidedMissions()?[id] is not null) return;
        Log.Info($"chain {c.ChainId} stage {c.Stage}: the offer is gone; it comes back on the next visit");
        c.MissionId = null;
        c.HoldUntilNextVisit = true;
    }

    /// <summary>Subscribes to the mission log of the current run. A new or loaded game has a new log.</summary>
    static void Watch(Run run)
    {
        if (_watched == run.Mode.Missions) return;
        if (_watched is not null) _watched.MissionRemoved -= OnMissionRemoved;
        _watched = run.Mode.Missions;
        _watched.MissionRemoved += OnMissionRemoved;
    }

    /// <summary>A mission left the log: completed, failed or abandoned. Records the result; the next check applies it.</summary>
    static void OnMissionRemoved(object? sender, Mission m) => Errors.Guard("chain mission removed", () =>
    {
        if (Run.Current is not { } run || !ReferenceEquals(sender, run.Mode.Missions)) return;
        var c = State(run).Instances.FirstOrDefault(i => i.MissionId == m.MissionID);
        if (c is null) return;
        string result = ChainModel.Classify(End(m));
        Log.Info($"chain {c.ChainId} stage {c.Stage}: mission {m.MissionID} left the log: {result}");
        c.MissionId = null;
        c.PendingResult = result;
    });

    static MissionEnd End(Mission m)
    {
        var timeLimits = new List<TimeLimitObjective>();
        m.GetObjectives(timeLimits);
        var guarded = new List<Objective>();
        // A failed hand-over lost its contact or its item: the person or the cargo is gone (spec 1105).
        m.GetObjectives(guarded, o => o is ProtectShipsObjective or EscortObjective or Events.HandOverObjective);
        return new MissionEnd(
            Completed: m.Completion == CompletionState.Completed,
            Failed: m.Completion == CompletionState.Failed,
            TimedOut: timeLimits.Any(o => o.Completion == CompletionState.Failed && o.GetTimeRemaining(m.Game) is { } left && left.Seconds <= 0),
            TargetLost: guarded.Any(o => o.Completion == CompletionState.Failed));
    }

    // ---- Pages

    static void OpenPage(Run run, SimRoot sim, ChainInstance c, StageRules stage)
    {
        string story = ChainModel.StorySoFar(c.Story, run.T.Chains.StoryLines);
        string lead = string.Join("\n\n", new[] { c.Lead, story.Length > 0 ? $"<gray>So far: {story}</gray>" : "" }.Where(t => t.Length > 0));
        EventRunner.Open(run, sim, EventLibrary.Get(stage.Page), lead);
        c.Lead = "";
        c.AwaitingSerial = run.State.Events.Pending?.Serial ?? throw new InvalidOperationException("the chain page did not open");
        Log.Info($"chain {c.ChainId} stage {c.Stage}: page {stage.Page} opened (serial {c.AwaitingSerial})");
    }

    /// <summary>
    /// A choice on a page applied. A ChainResult outcome resolves the stage now, with the choosing player paying any cost of the result row,
    /// like the page's own costs. A follow-up page keeps the stage waiting.
    /// </summary>
    internal static void OnOutcomeChosen(Run run, SimRoot sim, int serial, int playerIndex, OutcomeRules outcome)
    {
        var c = State(run).Instances.FirstOrDefault(i => i.AwaitingSerial == serial);
        if (c is null) return;
        if (outcome.ChainResult.Length > 0)
        {
            c.AwaitingSerial = 0;
            Resolve(run, sim, c, outcome.ChainResult, serial, playerIndex);
        }
        else c.AwaitingSerial = run.State.Events.Pending?.Serial ?? throw new InvalidDataException($"page outcome without ChainResult or Next in chain {c.ChainId}");
    }

    // ---- Results

    /// <summary>
    /// Applies the result row of the current stage: effects, reputation, chain flags and the story line, then moves to the next stage.
    /// resultSerial: the page that gave the result, whose result window gets the extra lines; 0 for a mission result.
    /// player: the human player who pays money losses of the row (gains split between all humans, like events).
    /// </summary>
    static void Resolve(Run run, SimRoot sim, ChainInstance c, string result, int resultSerial, int player)
    {
        var chain = ChainLibrary.Get(c.ChainId);
        var stage = chain.Stage(c.Stage);
        int row = ChainModel.Match(chain.Rows(stage), result, c.Flags);
        if (row < 0) throw new InvalidDataException($"chain {chain.ID} stage {stage.ID} has no result row for '{result}'");
        var r = stage.Results[row];
        c.PendingResult = "";
        var lines = new List<string>();
        var p = new PendingEvent { Tier = run.CurrentTier, TechValue = EventRunner.CurrentTechValue(run), Seed = sim.Rand.UInt64() };
        if (r.Effects is { } fx) EventRunner.ApplyOutcome(run, sim, player, fx, p, lines);
        if (r.Reputation != 0) ChangeReputation(run, c, r.Reputation, lines);
        foreach (string f in r.SetFlags) c.Flags.Add(f);
        foreach (string f in r.ClearFlags) c.Flags.Remove(f);
        if (r.Story.Length > 0) c.Story.Add(r.Story);
        c.Part++;
        Log.Info($"chain {chain.ID} stage {stage.ID}: result {result} -> row {row + 1}, next '{r.Next}'");
        Advance(run, chain, c, r.Next);
        Onboarding.RunLog.ChainResult(run, chain.Title, r.Story, lines, c.Ended);
        Show(run, sim, c, chain, r.Effects?.Text ?? "", lines, resultSerial);
    }

    /// <summary>
    /// The player who pays money losses of a mission result. Nobody chose a mission result, and the mission log is shared,
    /// so the richest human player pays, like the events area checks costs against the richest player.
    /// </summary>
    static int MissionPayer(Run run) =>
        ChainModel.Richest(run.Game.HumanPlayersIndexes.Select(i => (i, run.Game.GetPlayerState(i, deterministic: true).Money)).ToList());

    /// <summary>True when the current stage is a page at the station and a player ship is near it, so it can open now.</summary>
    static bool PageOpensNow(Run run, SimRoot sim, ChainInstance c, ChainRules chain) =>
        !c.Ended && chain.Stage(c.Stage) is { Kind: StageKind.Page, Where: StageWhere.Station } && !c.HoldUntilNextVisit
        && run.State.Events.Pending is null && sim.NodeId() == c.NodeId && PlayerNear(run, sim, c);

    static void Advance(Run run, ChainRules chain, ChainInstance c, string next)
    {
        bool loop = next == c.Stage;
        c.Stage = next;
        c.MissionId = null;
        c.AwaitingSerial = 0;
        c.DueSeconds = -1;
        c.Away = false;
        c.StageSector = run.State.SectorIndex;
        // A stage that leads back to itself (a standing offer) waits for the next visit, so it does not open again at once.
        c.HoldUntilNextVisit = loop;
        // An Arrival stage with AfterSectors 0 starts in the current system.
        if (next.Length > 0 && chain.Stage(next) is { Where: StageWhere.Arrival, AfterSectors: <= 0 }) c.DueSeconds = run.State.GameSeconds + run.T.Chains.LaterDelaySeconds;
    }

    /// <summary>
    /// Shows the result in the event window. A page result joins that page's result window. When the next stage is a page that can
    /// open now, the result leads that page instead, so the window goes straight on. Anything else gets its own result window.
    /// </summary>
    static void Show(Run run, SimRoot sim, ChainInstance c, ChainRules chain, string text, List<string> lines, int resultSerial)
    {
        var events = run.State.Events;
        var result = new EventResult { Serial = 0, Title = chain.Title, Text = text, Lines = [.. lines] };
        if (resultSerial > 0 && events.LastResult is { } last && last.Serial == resultSerial)
            result = new EventResult { Serial = last.Serial, Title = last.Title, Text = text.Length > 0 ? $"{last.Text}\n\n{text}" : last.Text, Lines = [.. last.Lines, .. lines] };
        if (PageOpensNow(run, sim, c, chain))
        {
            c.Lead = string.Join("\n", new[] { result.Text }.Concat(result.Lines).Where(l => l.Length > 0));
            OpenPage(run, sim, c, chain.Stage(c.Stage));
            return;
        }
        if (result.Serial > 0) { events.LastResult = result; return; }
        if (text.Length == 0 && lines.Count == 0) return;
        result.Serial = ++events.LastSerial;
        events.LastResult = result;
    }

    /// <summary>Reputation with the station's faction, in standard mission reputation at the sector tier (the unit of event reputation, spec 81).</summary>
    static void ChangeReputation(Run run, ChainInstance c, float units, List<string> lines)
    {
        if (FactionRules.GetFaction(c.StationPlayerIndex) is not { } faction) return;
        int amount = (int)MathF.Round(units * GameData.StandardMissionReputation(run.CurrentTier));
        if (amount == 0) return;
        run.Mode.AddFactionReputation(faction.ID, amount);
        lines.Add(EventText.Reputation(faction.ID, amount));
    }
}

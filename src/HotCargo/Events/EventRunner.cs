using Cosmoteer;
using Cosmoteer.Crew;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using Halfling.Geometry;
using Halfling.Random;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Tech;
using HotCargo.StartJobs;

namespace HotCargo.Events;

/// <summary>
/// Event simulation logic: triggers, conditions, outcome rolls and effects.
/// Everything here runs from deterministic sources only: Run.Hooks (sim tick, system switch, new run) and the choice MP input.
/// </summary>
public static class EventRunner
{
    public static readonly ID<SimObjectSpawner> SignalTag = new("hc_event_signal");
    static readonly ID<SimObjectSpawner> TradeStationTag = new("trade_station");
    static readonly ID<SimObjectSpawner> BuyerTag = new("hc_buyer");
    const float ProximityCheckSeconds = 0.5f;

    static EventsState State(Run run) => run.State.Events;

    /// <summary>Spec 70: a new run starts the cooldown, so no Arrival or Station event opens right at the start.</summary>
    internal static void OnNewRun(Run run) => State(run).LastEventSeconds = run.State.GameSeconds;

    internal static void OnSystemEntered(Run run, bool firstVisit)
    {
        var s = State(run);
        s.StationRolled.Clear();
        s.ArrivalDueSeconds = firstVisit ? run.State.GameSeconds + run.T.Events.ArrivalDelaySeconds : -1;
    }

    internal static void OnTick(Run run, SimRoot sim, double dt) => Guard(run, "event trigger", () =>
    {
        var t = run.T.Events;
        var s = State(run);
        if (sim != run.Game.Sim) return;
        CheckWanted(run, sim);
        CheckCarrier(run, sim);
        double at = run.State.GameSeconds;
        // Hulk loot is world content, not an event page: it runs while events are off or a page waits.
        if (Math.Floor(at / ProximityCheckSeconds) != Math.Floor((at - dt) / ProximityCheckSeconds)) Errors.Guard("hulk loot", () => Hulks.Check(run, sim));
        if (!t.Enabled || EventLibrary.LoadError is not null || s.Pending is not null) return;
        double now = run.State.GameSeconds;
        if (s.ArrivalDueSeconds >= 0 && now >= s.ArrivalDueSeconds)
        {
            s.ArrivalDueSeconds = -1;
            TryTrigger(run, sim, EventTrigger.Arrival, t.ArrivalEventChance, limited: true);
            return;
        }
        // Proximity checks run twice a game second. The step comes from game time, so every client checks on the same tick.
        if (Math.Floor(now / ProximityCheckSeconds) == Math.Floor((now - dt) / ProximityCheckSeconds)) return;
        CheckSignals(run, sim);
        Abandoned.Check(run, sim);
        if (s.Pending is null) CheckStations(run, sim);
    });

    /// <summary>
    /// Spec 470, 471: when the players are wanted (vanilla) and heat reached WantedWarningMinStage, a warning names the ways out,
    /// and a Wanted event (a way to clear wanted) may open. Runs in the sim tick; the warning itself is UI only.
    /// </summary>
    static void CheckWanted(Run run, SimRoot sim)
    {
        var s = State(run);
        var wanted = ((Cosmoteer.Modes.Career.CareerSimModeManager)sim.Mode).Wanted;
        var (fire, warnedNow) = EventModel.WantedWarning(wanted.IsLocalPlayerWanted, run.State.StagesStarted, run.T.Events.WantedWarningMinStage, s.WantedWarned);
        s.WantedWarned = warnedNow;
        if (!fire) return;
        var factions = wanted._wantedShips.Keys.Select(f => GameApp.Rules.GetFactionRules(f).NameKey.Text).ToList();
        Log.Info($"events: wanted by {string.Join(", ", factions)} at heat stage {run.State.StagesStarted}");
        EventText.ShowWantedWarning(run.Game, string.Join(", ", factions));
        var t = run.T.Events;
        if (t.Enabled && EventLibrary.LoadError is null && s.Pending is null) TryTrigger(run, sim, EventTrigger.Wanted, t.WantedEventChance, limited: false);
    }

    /// <summary>Spec 770: a carried page closes when its ship is gone (destroyed, or left behind in another system).</summary>
    static void CheckCarrier(Run run, SimRoot sim)
    {
        var s = State(run);
        if (s.Pending is not { Carrier: { } carrier } p) return;
        if (p.HailDue && EventHail.TryHail(sim, carrier)) p.HailDue = false;
        bool alive = EventHail.CarrierAlive(sim, carrier);
        // A station that starts to refuse calls (factions, spec 657) closes its page too, so the page never waits forever.
        bool refuses = alive && sim.Ships[carrier] is { } ship && Factions.FactionsArea.Refuses(run, null, ship);
        if (alive && !refuses) return;
        s.Pending = null;
        // Left behind in another system (no ship here): the players chose to leave, so the page closes with no result page.
        // A result page on arrival named an offer the players no longer had in mind (playtest 2026-11-13).
        if (p.CarrierNode is { } node && !node.Equals(sim.NodeId()))
        {
            Log.Info($"event {p.EventId}: its carrier stayed in another system, the page closes");
            return;
        }
        string text = refuses ? Cosmoteer.Localization.Strings.GetText("HotCargoEvents/CarrierRefuses")
            : sim.Ships[carrier] is { } wreck ? Cosmoteer.Localization.Strings.FormatText("HotCargoEvents/CarrierDestroyedFmt", wreck.Metadata.PlainShipName)
            : Cosmoteer.Localization.Strings.GetText("HotCargoEvents/CarrierGone");
        s.LastResult = new EventResult { Serial = p.Serial, Title = EventLibrary.Get(p.EventId).Title, Text = text };
        Log.Info($"event {p.EventId}: its carrier is {(refuses ? "refusing calls" : "destroyed")}, the page closes");
    }

    /// <summary>A human ship that reaches a signal POI removes it and opens a Signal event.</summary>
    static void CheckSignals(Run run, SimRoot sim)
    {
        float r2 = run.T.Events.SignalTriggerDistance * run.T.Events.SignalTriggerDistance;
        var ships = sim.Ships.HumanPlayerSignificantShips.ToList();
        foreach (var doodad in sim.Doodads.Where(d => d.Tags?.Contains(SignalTag) == true).OrderBy(d => d.UniqueID).ToList())
        {
            if (!ships.Any(sh => sh.DetWorldCenter.DistanceSquaredTo(doodad.Location) <= r2)) continue;
            sim.Doodads.Remove(doodad, dispose: true);
            Log.Info($"signal reached at {doodad.Location}");
            TryTrigger(run, sim, EventTrigger.Signal, chance: 1f, limited: false);
            return;
        }
    }

    /// <summary>
    /// The first trade station that a human ship comes near in a system visit rolls one Station event.
    /// The first one in a system for the whole run also rolls a Courier Job offer (spec 85).
    /// </summary>
    static void CheckStations(Run run, SimRoot sim)
    {
        var s = State(run);
        ObjectID node = sim.MapNode?.UniqueID ?? throw new InvalidOperationException("sim has no map node");
        if (s.StationRolled.Contains(node) && s.CourierNodes.Contains(node)) return;
        float r = run.T.Events.StationTriggerDistance;
        var ships = sim.Ships.HumanPlayerSignificantShips.ToList();
        // A station that refuses calls (factions, spec 657) cannot carry a page, so it does not count.
        Ship? station = sim.Ships.FirstOrDefault(st => st.HasTag(TradeStationTag) && !st.HasTag(BuyerTag) && !st.IsJunk
            && ships.Any(sh => sh.DetWorldBoundingCircle.DistanceTo(st.DetWorldBoundingCircle) <= r)
            && !Factions.FactionsArea.Refuses(run, null, st));
        if (station is null) return;
        if (s.CourierNodes.Add(node))
        {
            float roll = sim.Rand.Single();
            if (roll < run.T.Events.CourierChance) OfferCourier(sim, station);
            else Log.Info($"events: courier roll {roll:0.00} >= chance {run.T.Events.CourierChance:0.00}, no offer");
        }
        // Spec 768: a Station event is carried by that station. It hails the players, and its comms show the page.
        if (s.StationRolled.Add(node)) TryTrigger(run, sim, EventTrigger.Station, run.T.Events.StationEventChance, limited: true, carrier: station.UniqueID);
    }

    /// <summary>
    /// Puts a Courier Job into the station's mission list: the Courier generator in spawns.rules spawns the contact far away
    /// and a mission offer at the nearest trade station (the one the players reached).
    /// </summary>
    internal static void OfferCourier(SimRoot sim, Ship station)
    {
        EventLibrary.Spawn(CourierSpawn).Generate(sim, sim.Rand, loadInstantly: true, rootLoc: station.DetWorldCenter, addExistingSpawnTags: true);
        Log.Info($"courier job offered at {station.Metadata.PlainShipName}");
    }

    public const string CourierSpawn = "Courier";

    /// <summary>
    /// Rolls the chance, then picks an eligible event of the trigger by weight and opens it.
    /// limited: the cooldown and the per-system cap apply (Arrival and Station).
    /// </summary>
    static void TryTrigger(Run run, SimRoot sim, EventTrigger trigger, float chance, bool limited, ObjectID? carrier = null)
    {
        var t = run.T.Events;
        var s = State(run);
        ObjectID node = sim.MapNode?.UniqueID ?? throw new InvalidOperationException("sim has no map node");
        float roll = sim.Rand.Single();
        if (roll >= chance) { Log.Info($"events: {trigger} roll {roll:0.00} >= chance {chance:0.00}, no event"); return; }
        if (limited && !EventModel.CooldownOver(run.State.GameSeconds, s.LastEventSeconds, t.CooldownSeconds))
        {
            Log.Info($"events: {trigger} skipped, cooldown");
            return;
        }
        if (limited && s.NodeEvents.GetValueOrDefault(node) >= t.MaxEventsPerSystem)
        {
            Log.Info($"events: {trigger} skipped, {t.MaxEventsPerSystem} events in this system already");
            return;
        }
        var snapshot = Snap(run, sim, RichestHumanMoney(run), run.CurrentTier, CurrentTechValue(run));
        var candidates = EventLibrary.InOrder.Where(e => e.Trigger == trigger && Eligible(run, e, snapshot)).ToList();
        var weights = candidates.Select(e => EventModel.EffectiveWeight(e.Weight, e.WeightPerHeat, e.WeightPerTech, run.State.Heat, run.State.FleetTech)
            * (e.Offer == OfferKind.None ? 1f : Math.Max(0f, t.ResearchWeightScale))).ToList();
        int pick = EventModel.PickWeighted(weights, sim.Rand.Single());
        if (pick < 0) { Log.Info($"events: no eligible {trigger} event"); return; }
        // Every triggered event starts the cooldown, so an Arrival or Station event does not follow right after another dialog.
        s.LastEventSeconds = run.State.GameSeconds;
        if (limited) s.NodeEvents[node] = s.NodeEvents.GetValueOrDefault(node) + 1;
        Open(run, sim, candidates[pick], lead: "", carrier, carrierHails: carrier is not null);
    }

    /// <summary>A trigger without the cooldown and the per-system cap: Relic (the player chose to fly there).</summary>
    internal static void TriggerUnlimited(Run run, SimRoot sim, EventTrigger trigger, float chance) =>
        TryTrigger(run, sim, trigger, chance, limited: false);

    /// <summary>
    /// True when the event may open now: under its run limit, under its category limit, and its need is met.
    /// The Unavoidable count comes from the seen counts, so it needs no saved state of its own.
    /// </summary>
    public static bool Eligible(Run run, EventRules e, Snapshot snapshot)
    {
        var s = State(run);
        if (!EventModel.UnderRunLimit(s.Seen.GetValueOrDefault(e.ID), e.MaxPerRun)) return false;
        if (e.Category == EventCategory.Unavoidable)
        {
            int seen = EventLibrary.InOrder.Where(x => x.Category == EventCategory.Unavoidable).Sum(x => s.Seen.GetValueOrDefault(x.ID));
            if (seen >= run.T.Events.MaxUnavoidablePerRun) return false;
        }
        if (!ResearchOffers.Possible(run, e.Offer, run.CurrentTier, CurrentTechValue(run))) return false;
        return EventModel.FirstUnmet(ToNeed(run, e.Need, run.CurrentTier, CurrentTechValue(run)), snapshot) is null;
    }

    /// <summary>Opens an event page. Public for the autotest command that forces an event.</summary>
    /// <summary>
    /// carrier: the ship whose comms show the page (spec 767, 768). Without one, an event with a Carrier spawn gets its own ship, which
    /// hails when CarrierHails is on. carrierHails: the given carrier hails the players (a station event).
    /// </summary>
    public static void Open(Run run, SimRoot sim, EventRules e, string lead, ObjectID? carrier = null, bool carrierHails = false)
    {
        var s = State(run);
        s.Seen[e.ID] = s.Seen.GetValueOrDefault(e.ID) + 1;
        s.Pending = new PendingEvent
        {
            EventId = e.ID,
            Serial = ++s.LastSerial,
            Seed = sim.Rand.UInt64(),
            Tier = run.CurrentTier,
            TechValue = CurrentTechValue(run),
            Heat = run.State.Heat,
            Lead = lead,
        };
        ResearchOffers.Fill(run, s.Pending, e.Offer);
        if (carrier is null && e.Carrier.Length > 0)
        {
            carrier = EventHail.SpawnCarrier(sim, e, s.Pending.Seed);
            carrierHails = e.CarrierHails;
        }
        s.Pending.Carrier = carrier;
        s.Pending.CarrierNode = carrier is null ? null : sim.NodeId();
        s.Pending.HailDue = carrier is not null && carrierHails;
        if (carrier is { } c && carrierHails) EventText.ShowCalling(run.Game, sim.Ships[c]?.Metadata.PlainShipName ?? e.Title);
        Log.Info($"event opened: {e.ID} serial {s.Pending.Serial} tier {s.Pending.Tier} heat {run.State.Heat:0.0} tech {run.State.FleetTech}");
    }

    /// <summary>
    /// Applies a choice of the pending page. Called from the choice MP input on every client, and from a test command.
    /// Ignores the input when the page changed (another player chose first) or the chooser cannot pay.
    /// forcedOutcome: test only, picks that outcome instead of rolling.
    /// </summary>
    public static void Choose(Run run, SimRoot sim, int playerIndex, int serial, int choiceIndex, int forcedOutcome = -1) => Guard(run, "event choice", () =>
    {
        var s = State(run);
        if (run.State.Result != RunResult.Running) return;
        if (s.Pending is not { } p || p.Serial != serial)
        {
            Log.Info($"event choice ignored: serial {serial} is not the pending page");
            return;
        }
        var e = EventLibrary.Get(p.EventId);
        if (choiceIndex < 0 || choiceIndex >= e.Choices.Length) throw new InvalidOperationException($"event {e.ID} has no choice {choiceIndex}");
        var choice = e.Choices[choiceIndex];
        int money = run.Game.GetPlayerState(playerIndex, deterministic: true).Money;
        var snapshot = PageSnap(run, sim, money, p);
        var picks = choice.IsSubmit ? Picks(run, e, serial) : [];
        if (ChoiceUnmet(run, choice, p, picks, snapshot) is { } unmet)
        {
            Log.Info($"event choice ignored: {e.ID} choice {choiceIndex + 1} needs {unmet}");
            // UI only, for the player who clicked: the choice did nothing, so say why.
            if (playerIndex == run.Game.LocalPlayerIndex) Ux.UxKit.ChatLine(run.Game, EventText.Reason(unmet));
            return;
        }
        var lines = new List<string>();
        PayCost(run, sim, playerIndex, choice.Cost, p, lines);
        foreach (var pick in picks) PayPick(run, sim, playerIndex, pick, p, lines);
        // Spec 811: a selected Sure option takes the first outcome without a roll.
        int outcomeIndex = forcedOutcome >= 0 ? forcedOutcome : picks.Any(k => k.Rules.Sure) ? 0 : RollOutcome(run, choice, p.Seed, choiceIndex);
        if (outcomeIndex < 0 || outcomeIndex >= choice.Outcomes.Length) throw new InvalidOperationException($"event {e.ID} choice {choiceIndex + 1} has no outcome {outcomeIndex}");
        var outcome = choice.Outcomes[outcomeIndex];
        if (choice.ScaleByPoints) outcome = outcome.Scaled(PointsOf(picks), p.Tier, all: false);
        Log.Info($"event choice: {e.ID} choice {choiceIndex + 1} outcome {outcomeIndex + 1} by player {playerIndex}"
            + (picks.Count > 0 ? $", options {string.Join(" ", picks.Select(k => $"{k.Index + 1}x{k.Units:0.##}"))}" : ""));
        s.Pending = null;
        foreach (var pick in picks) ApplyPick(run, sim, playerIndex, pick, p, lines);
        if (picks.Count > 0) MultiChoice.MultiChoices.Clear(run, EventsMultiDomain.Name, serial);
        ApplyOutcome(run, sim, playerIndex, outcome, p, lines);
        // Tokens fill after the effects, so a text sees the counts that the choice left.
        string outcomeText = EventText.Fill(outcome.Text, Vaults.CountFleetTech(sim), p);
        if (outcome.Next.Length > 0)
        {
            string lead = string.Join("\n", new[] { outcomeText }.Concat(lines).Where(l => l.Length > 0));
            // A follow-up page stays on the ship that carried this one.
            Open(run, sim, EventLibrary.Get(outcome.Next), lead, p.Carrier);
        }
        else
        {
            s.LastResult = new EventResult { Serial = serial, Title = e.Title, Text = outcomeText, Lines = [.. lines] };
        }
        OutcomeChosen?.Invoke(run, sim, serial, playerIndex, outcome);
    });

    /// <summary>
    /// Runs inside Choose after an outcome applied. Arguments: run, sim, serial of the page that was chosen on, the choosing player, the outcome.
    /// A follow-up page (Next) is already pending. The Chains area reads ChainResult here.
    /// </summary>
    public static event Action<Run, SimRoot, int, int, OutcomeRules>? OutcomeChosen;

    /// <summary>Test only (ev_force_outcome): the next outcome roll picks this outcome index. Set on every machine in co-op.</summary>
    internal static int? ForcedOutcome;

    static int RollOutcome(Run run, ChoiceRules choice, ulong seed, int choiceIndex)
    {
        if (ForcedOutcome is { } forced)
        {
            ForcedOutcome = null;
            if (forced >= choice.Outcomes.Length) throw new InvalidOperationException($"forced outcome {forced + 1} of {choice.Outcomes.Length}");
            Log.Info($"event roll forced -> outcome {forced + 1}");
            return forced;
        }
        var weights = choice.Outcomes.Select(o => EventModel.EffectiveWeight(o.Weight, o.WeightPerHeat, o.WeightPerTech, run.State.Heat, run.State.FleetTech)).ToList();
        float roll = new FastRand(seed, (ulong)choiceIndex + 1).Single();
        int pick = EventModel.PickWeighted(weights, roll);
        if (pick < 0) throw new InvalidDataException("all outcome weights are 0 at this heat and tech; fix the weights in events.rules");
        Log.Info($"event roll {roll:0.000} over weights [{string.Join(", ", weights.Select(w => w.ToString("0.##")))}] -> outcome {pick + 1}");
        return pick;
    }

    // ---- Conditions

    public static long CurrentTechValue(Run run) => run.TechValue();

    /// <summary>The most money one human player has. In co-op each player has own money and any player can pick a choice.</summary>
    static int RichestHumanMoney(Run run) =>
        run.Game.HumanPlayersIndexes.Max(i => run.Game.GetPlayerState(i, deterministic: true).Money);

    /// <summary>Credits of an amount in event money units at a tier, with the economy and Hot Cargo reward factors.</summary>
    public static int Credits(Run run, float units, float techUnits, int tier, long techValue)
    {
        var t = run.T.Events;
        float factor = run.Mode.EconDifficulty.MoneyRewardFactor * run.T.Journey.RewardScale;
        return EventModel.Credits(units * t.MoneyScale, GameData.StandardMissionMoney(tier), factor, techUnits * t.MoneyScale, techValue, Math.Max(1, t.MoneyRoundTo));
    }

    public static int FameAmount(Run run, float units, int tier) =>
        (int)MathF.Round(units * run.T.Events.FameScale * run.StandardMissionFamePaid(tier));

    public static Need ToNeed(Run run, NeedRules? n, int tier, long techValue) => n is null ? new Need() : new Need
    {
        Money = Credits(run, n.Money, 0f, tier, techValue),
        Tech = n.Tech,
        VaultSpace = n.VaultSpace,
        Crew = n.Crew,
        Bunks = n.Bunks,
        HandOverReady = n.HandOverReady,
        MinHeat = n.MinHeat,
        MaxHeat = n.MaxHeat,
        MinSector = n.MinSector,
        MaxSector = n.MaxSector,
        Resources = n.Resources.Select(r => (r.ID, r.At(tier))).ToList(),
        Parts = n.Parts,
        Flags = n.Flags,
        NotFlags = n.NotFlags,
        FlagAges = n.FlagAges.Select(f => (f.Flag, f.MinSectors)).ToList(),
        MinSectorsLeft = n.MinSectorsLeft,
        MaxSectorsLeft = n.MaxSectorsLeft,
        Wanted = n.Wanted,
    };

    /// <summary>The need of a choice plus its sure cost.</summary>
    public static Need ChoiceNeed(Run run, ChoiceRules c, PendingEvent p)
    {
        var need = ToNeed(run, c.Need, p.Tier, p.TechValue);
        if (c.Cost is not { } cost) return need;
        return EventModel.Plus(need, CostCredits(run, cost, p), cost.Tech + (cost.OfferUnits ? p.OfferUnits : 0), cost.Crew, cost.Resources.Select(r => (r.ID, r.At(p.Tier))));
    }

    /// <summary>Credits of a sure money cost, with the heat part from when the page opened.</summary>
    public static int CostCredits(Run run, CostRules cost, PendingEvent p) =>
        Credits(run, EventModel.HeatScaledUnits(cost.Money, cost.MoneyPerHeat, p.Heat), 0f, p.Tier, p.TechValue);

    /// <summary>The fleet as a pending page's choices see it: Snap, plus the state of the hand-over on the page's ship (spec 1110).</summary>
    public static Snapshot PageSnap(Run run, SimRoot sim, int money, PendingEvent p) =>
        Snap(run, sim, money, p.Tier, p.TechValue) with
        {
            HandOverBlock = p.Carrier is { } c && HandOverObjective.ForContact(run, c) is { } h ? h.Blocker(sim) : null,
        };

    /// <summary>The fleet as conditions see it. Money is the given player's; everything else is the whole human fleet in this system.</summary>
    public static Snapshot Snap(Run run, SimRoot sim, int money, int tier, long techValue)
    {
        var ships = Vaults.HumanShips(sim).ToList();
        var resources = new Dictionary<string, int>();
        foreach (ResourceRules r in GameApp.Rules.Resources)
            resources[r.ID.ToString()] = ships.Sum(sh => sh.Resources.GetResourceTotal(r.ID, includeStored: true, includeCarried: false));
        var vaults = Vaults.All(sim);
        return new Snapshot
        {
            Money = money,
            Tech = vaults.Sum(g => g.Resources),
            VaultSpace = vaults.Sum(g => g.MaxResources - g.Resources),
            Crew = run.Game.HumanPlayersIndexes.Sum(sim.Crew.CountCrewForPlayer),
            Bunks = ships.Sum(sh => sh.Crew.RemainingCrewCapacity),
            Heat = run.State.Heat,
            Sector = run.State.SectorIndex,
            Resources = resources,
            Parts = ships.SelectMany(sh => sh.Parts).Select(pt => pt.Rules.ID.ToString()).ToHashSet(),
            Flags = State(run).Flags,
            FlagSectors = State(run).FlagSectors,
            SectorsLeft = run.SectorCount() - run.State.SectorIndex,
            Wanted = ((Cosmoteer.Modes.Career.CareerSimModeManager)sim.Mode).Wanted.IsLocalPlayerWanted,
        };
    }

    // ---- Page options (spec 810 to 812)

    /// <summary>A selected option of a page's shared draft: its index (the multiselect option ID), its rules, slider steps and units.</summary>
    public sealed record OptionPick(int Index, OptionRules Rules, int Steps, float Units);

    /// <summary>What one pick pays: credits from the chooser, crates, crew who leave, crew on hand who stay, and a resource amount.</summary>
    public readonly record struct Pay(int Money, int Tech, int Crew, int CrewWork, int Resource);

    /// <summary>The selected options of the page with this serial, from the shared draft (values kept in each option's range).</summary>
    public static List<OptionPick> Picks(Run run, EventRules e, int serial) =>
        MultiChoice.MultiChoices.Draft(run, EventsMultiDomain.Name, serial)
            .Where(d => d.Option >= 0 && d.Option < e.Options.Length)
            .Select(d => new OptionPick(d.Option, e.Options[d.Option], d.Value, d.Value * e.Options[d.Option].StepUnits))
            .ToList();

    public static float UnitSize(OptionRules o, PendingEvent p) => EventModel.UnitSize(o.Unit, o.UnitPerTier, o.UnitPerHeat, p.Tier, p.Heat);

    public static Pay PayOf(Run run, OptionRules o, float units, PendingEvent p)
    {
        float size = UnitSize(o, p);
        int whole = EventModel.Whole(units, size);
        return o.Pay switch
        {
            PayKind.None => default,
            PayKind.Money => new Pay(Credits(run, units * size, 0f, p.Tier, p.TechValue), 0, 0, 0, 0),
            PayKind.Tech => new Pay(0, whole, 0, 0, 0),
            PayKind.Crew => new Pay(0, 0, whole, 0, 0),
            PayKind.CrewWork => new Pay(0, 0, 0, whole, 0),
            PayKind.Resource => new Pay(0, 0, 0, 0, whole),
            _ => throw new ArgumentOutOfRangeException(nameof(o), o.Pay, null),
        };
    }

    public static float PointsOf(IEnumerable<OptionPick> picks) => picks.Sum(k => k.Units * k.Rules.Points);

    /// <summary>
    /// The first thing a choice lacks, or null. Any choice: its need and sure cost. A submit also: at least one selected option,
    /// enough points, each selected option's need, and the needs and pays of all selected options on top of the choice's own.
    /// </summary>
    public static Unmet? ChoiceUnmet(Run run, ChoiceRules c, PendingEvent p, IReadOnlyList<OptionPick> picks, Snapshot snapshot)
    {
        var need = ChoiceNeed(run, c, p);
        if (!c.IsSubmit) return EventModel.FirstUnmet(need, snapshot);
        if (picks.Count == 0) return new Unmet(UnmetKind.Points);
        float needPoints = EventModel.NeedPoints(c.NeedPoints, c.NeedPointsPerHeat, p.Heat);
        if (!EventModel.Enough(PointsOf(picks), needPoints)) return new Unmet(UnmetKind.Points, Amount: needPoints);
        foreach (var pick in picks)
        {
            // An option's own need adds to its pay, like a choice's need adds to its cost: Need Tech 1 with a crate keeps one crate.
            var own = ToNeed(run, pick.Rules.Need, p.Tier, p.TechValue);
            if (EventModel.FirstUnmet(own, snapshot) is { } u) return u;
            var pay = PayOf(run, pick.Rules, pick.Units, p);
            var resources = own.Resources.ToList();
            if (pay.Resource > 0) resources.Add((pick.Rules.Resource, pay.Resource));
            need = EventModel.Plus(need, own.Money + pay.Money, own.Tech + pay.Tech, own.Crew + pay.Crew + pay.CrewWork, resources);
        }
        return EventModel.FirstUnmet(need, snapshot);
    }

    static void PayPick(Run run, SimRoot sim, int playerIndex, OptionPick pick, PendingEvent p, List<string> lines)
    {
        var pay = PayOf(run, pick.Rules, pick.Units, p);
        if (pay.Money > 0) ChangeMoney(run, playerIndex, -pay.Money, lines);
        if (pay.Tech > 0) ChangeTech(sim, -pay.Tech, lines);
        if (pay.Crew > 0) ChangeCrew(sim, -pay.Crew, p.Seed, lines);
        if (pay.Resource > 0) ChangeResource(sim, pick.Rules.Resource, -pay.Resource, lines);
    }

    /// <summary>Salt of an option's risk roll, plus the option index, so each option rolls on its own.</summary>
    const ulong RiskSalt = 0x5249_534B_0000UL;

    /// <summary>An option's effects: per unit (scaled), once, and its risk, rolled from the page seed.</summary>
    static void ApplyPick(Run run, SimRoot sim, int playerIndex, OptionPick pick, PendingEvent p, List<string> lines)
    {
        var o = pick.Rules;
        if (o.Effect is { } fx) ApplyOptionEffect(run, sim, playerIndex, fx.Scaled(pick.Units, p.Tier, all: true), p, lines);
        if (o.Once is { } once) ApplyOptionEffect(run, sim, playerIndex, once, p, lines);
        if (o.Risk is not { } risk) return;
        float chance = EventModel.RiskChance(o.RiskChance, pick.Units);
        float roll = new FastRand(p.Seed, RiskSalt + (ulong)pick.Index).Single();
        Log.Info($"event option {pick.Index + 1} risk roll {roll:0.000} vs {chance:0.000}");
        if (roll < chance) ApplyOptionEffect(run, sim, playerIndex, risk, p, lines);
    }

    static void ApplyOptionEffect(Run run, SimRoot sim, int playerIndex, OutcomeRules fx, PendingEvent p, List<string> lines)
    {
        if (fx.Text.Length > 0) lines.Add(EventText.Fill(fx.Text, run.State.FleetTech, p));
        ApplyOutcome(run, sim, playerIndex, fx, p, lines);
    }

    // ---- Effects. Each effect adds one XML result line.

    static void PayCost(Run run, SimRoot sim, int playerIndex, CostRules? cost, PendingEvent p, List<string> lines)
    {
        if (cost is null) return;
        int money = CostCredits(run, cost, p);
        if (money > 0) ChangeMoney(run, playerIndex, -money, lines);
        int tech = cost.Tech + (cost.OfferUnits ? p.OfferUnits : 0);
        if (tech > 0) ChangeTech(sim, -tech, lines);
        if (cost.Crew > 0) ChangeCrew(sim, -cost.Crew, p.Seed, lines);
        foreach (var r in cost.Resources) ChangeResource(sim, r.ID, -r.At(p.Tier), lines);
    }

    /// <summary>Applies the effects of an outcome and adds one result line per effect. Also used by the Chains area for stage results.</summary>
    internal static void ApplyOutcome(Run run, SimRoot sim, int playerIndex, OutcomeRules o, PendingEvent p, List<string> lines)
    {
        int credits = Credits(run, o.Money, o.MoneyTech, p.Tier, p.TechValue);
        if (credits != 0) ChangeMoney(run, playerIndex, credits, lines);
        if (o.Fame != 0) ChangeFame(run, FameAmount(run, o.Fame, p.Tier), lines);
        if (o.Heat != 0) ChangeHeat(run, sim, o.Heat * run.T.Events.HeatScale, lines);
        if (o.Tech != 0) ChangeTech(sim, o.Tech, lines);
        if (o.Crew != 0) ChangeCrew(sim, o.Crew, p.Seed, lines);
        if (o.CrewLeaves > 0) ChangeCrew(sim, -o.CrewLeaves, p.Seed, lines, killed: false);
        foreach (var r in o.Resources) ChangeResource(sim, r.ID, r.At(p.Tier), lines);
        if (o.HullDamage > 0) DamageHull(sim, o.HullDamage, p.Seed, lines);
        if (o.Reputation != 0) ChangeReputation(run, sim, o.Reputation, p.Tier, lines);
        if (o.BuyerPrice > 0) ChangeBuyerPrice(run, o.BuyerPrice, lines);
        if (o.ClearWanted) ClearWanted(sim, lines);
        if (o.UnlockOffer && ResearchOffers.Unlock(run, p) is { Count: > 0 } techs) lines.Add(EventText.Researched(techs));
        if (o.OfferPrice && p.OfferPrice > 0) ChangeMoney(run, playerIndex, p.OfferPrice, lines);
        foreach (string f in o.SetFlags)
        {
            State(run).Flags.Add(f);
            State(run).FlagSectors[f] = run.State.SectorIndex;
        }
        foreach (string f in o.ClearFlags) State(run).Flags.Remove(f);
        if (o.Spawn.Length > 0) Spawn(run, sim, o.Spawn, p.Seed);
        if (o.HandOver) HandOverObjective.Deliver(run, p.Carrier, o.HandOverPay);
        if (o.ContactTurns != ContactReaction.None) HandOverObjective.React(run, sim, p.Carrier, o.ContactTurns);
        // UI only: the chat tells every player the result.
        if (o.Chat && o.Text.Length > 0) Ux.UxKit.ChatLine(run.Game, EventText.Fill(o.Text, run.State.FleetTech, p));
    }

    /// <summary>Gains split between the human players like a mission reward. A loss comes from the chooser and stops at 0.</summary>
    static void ChangeMoney(Run run, int playerIndex, int amount, List<string> lines)
    {
        var game = run.Game;
        if (amount > 0)
        {
            int each = amount / game.LogicalHumanPlayerCount;
            foreach (int i in game.HumanPlayersIndexes) game.GetPlayerState(i, deterministic: true).AddMoney(each);
        }
        else
        {
            var state = game.GetPlayerState(playerIndex, deterministic: true);
            amount = -Math.Min(-amount, Math.Max(0, state.Money));
            state.AddMoney(amount);
        }
        if (amount != 0) lines.Add(EventText.Money(amount));
    }

    static void ChangeFame(Run run, int amount, List<string> lines)
    {
        int before = run.Mode.PlayerFame;
        run.Mode.PlayerFame = Math.Max(0, before + amount);
        int delta = run.Mode.PlayerFame - before;
        if (delta != 0) lines.Add(EventText.Fame(delta));
    }

    /// <summary>Reputation with the faction that controls the system. A system without one gets nothing.</summary>
    static void ChangeReputation(Run run, SimRoot sim, float units, int tier, List<string> lines)
    {
        if (sim.MapNode?.ControllingFaction is not { } faction) { lines.Add(EventText.NoFaction()); return; }
        int amount = (int)MathF.Round(units * GameData.StandardMissionReputation(tier));
        if (amount == 0) return;
        run.Mode.AddFactionReputation(faction, amount);
        lines.Add(EventText.Reputation(faction, amount));
    }

    /// <summary>Spec 472: like a paid bounty, every faction stops wanting the players (WantedManager.RemoveFactionFromWanting).</summary>
    static void ClearWanted(SimRoot sim, List<string> lines)
    {
        var wanted = ((Cosmoteer.Modes.Career.CareerSimModeManager)sim.Mode).Wanted;
        var factions = wanted._wantedShips.Keys.ToList();
        foreach (var f in factions) wanted.RemoveFactionFromWanting(f);
        if (factions.Count > 0) lines.Add(EventText.WantedCleared());
        Log.Info($"events: wanted cleared ({factions.Count} factions)");
    }

    static void ChangeBuyerPrice(Run run, float factor, List<string> lines)
    {
        float before = run.State.BuyerPriceMultiplier;
        run.State.BuyerPriceMultiplier = EventModel.BuyerPriceAfter(before, factor);
        lines.Add(EventText.BuyerPrice(run.State.BuyerPriceMultiplier));
        Log.Info($"buyer price multiplier {before:0.00} -> {run.State.BuyerPriceMultiplier:0.00}");
    }

    static void ChangeHeat(Run run, SimRoot sim, float amount, List<string> lines)
    {
        float before = run.State.Heat;
        run.AddHeat(amount, sim);
        float delta = run.State.Heat - before;
        if (Math.Abs(delta) >= 0.5f) lines.Add(EventText.Heat(delta));
    }

    static void ChangeTech(SimRoot sim, int amount, List<string> lines)
    {
        int done = amount > 0 ? Vaults.Add(sim, amount) : -Vaults.Remove(sim, -amount);
        if (done != 0) lines.Add(EventText.Resource(Vaults.TechId, done));
        if (done < amount) lines.Add(EventText.NoVaultRoom(amount - done));
    }

    /// <summary>killed: false removes crew without the death path (no death stats, no casualty), for crew that leave alive.</summary>
    static void ChangeCrew(SimRoot sim, int amount, ulong seed, List<string> lines, bool killed = true)
    {
        var ships = Vaults.HumanShips(sim).OrderBy(sh => sh.Metadata.PlayerIndex).ThenBy(sh => sh.UniqueID).ToList();
        int done = 0;
        if (amount > 0)
        {
            foreach (var ship in ships)
            {
                if (done == amount) break;
                done += ship.Crew.CreateCrewsAtRandomParts(Math.Min(amount - done, ship.Crew.RemainingCrewCapacity));
            }
        }
        else
        {
            // Crew that carry nothing, so no carried tech or cargo vanishes with them.
            var rand = new FastRand(seed, 0x4352_4557UL);
            var pool = ships.SelectMany(sh => sh.Crew.Assigned).Where(c => c.CarriedResource.Quantity == 0).OrderBy(c => c.UniqueID).ToList();
            while (done > amount && pool.Count > 0)
            {
                CrewSoul soul = pool[rand.Int32(0, pool.Count - 1)];
                pool.Remove(soul);
                soul.Delete(killed);
                done--;
            }
        }
        if (done != 0) lines.Add(killed ? EventText.Crew(done) : EventText.CrewLeft(-done));
        if (amount > 0 && done < amount) lines.Add(EventText.NoBunk(amount - done));
    }

    static void ChangeResource(SimRoot sim, string id, int amount, List<string> lines)
    {
        var rid = new ID<ResourceRules>(id);
        int left = Math.Abs(amount);
        foreach (var ship in Vaults.HumanShips(sim).OrderBy(sh => sh.Metadata.PlayerIndex).ThenBy(sh => sh.UniqueID))
        {
            if (left == 0) break;
            left -= amount > 0 ? ship.Resources.AddResources(rid, left, excludeConstructionSinks: true) : ship.Resources.RemoveResources(rid, left);
        }
        int done = Math.Sign(amount) * (Math.Abs(amount) - left);
        if (done != 0) lines.Add(EventText.Resource(rid, done));
        if (amount > 0 && left > 0) lines.Add(EventText.NoStorage(rid, left));
    }

    /// <summary>Takes fraction × max health from up to 6 random parts of the biggest human ship. Parts keep 1 health; vaults are skipped.</summary>
    static void DamageHull(SimRoot sim, float fraction, ulong seed, List<string> lines)
    {
        var ship = Vaults.HumanShips(sim).OrderByDescending(sh => sh.Parts.Count).ThenBy(sh => sh.UniqueID).FirstOrDefault();
        if (ship is null) return;
        var pool = ship.Parts.Where(pt => pt.Rules.ID != Vaults.VaultId && pt.Health > 1).OrderBy(pt => pt.Location.Y).ThenBy(pt => pt.Location.X).ToList();
        var rand = new FastRand(seed, 0x4855_4C4CUL);
        int hits = Math.Min(6, pool.Count);
        for (int i = 0; i < hits; i++)
        {
            Part part = pool[rand.Int32(0, pool.Count - 1)];
            pool.Remove(part);
            part.Health = Math.Max(1, part.Health - (int)(fraction * part.Rules.MaxHealth));
        }
        if (hits > 0) lines.Add(EventText.HullDamage(hits));
    }

    /// <summary>Runs a generator from spawns.rules around the human fleet. Existing tags (player_ship and others) are visible to its spawners.</summary>
    static void Spawn(Run run, SimRoot sim, string name, ulong seed)
    {
        Vector2 at = SimQueries.FleetCenter(sim) ?? throw new InvalidOperationException("no human ships to spawn near");
        EventLibrary.Spawn(name).Generate(sim, new FastRand(seed, 0x5350_4157UL), loadInstantly: true, rootLoc: at, addExistingSpawnTags: true);
        Log.Info($"event spawn {name} at {at}");
    }

    /// <summary>
    /// A content or engine error in event logic must not crash the sim tick or an MP input.
    /// It drops the pending page and shows the error. Every client fails the same way, so the sim stays in sync.
    /// </summary>
    static void Guard(Run run, string what, Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            Errors.Report(what, ex.Message, ex);
            State(run).Pending = null;
        }
    }
}

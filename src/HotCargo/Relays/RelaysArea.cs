using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Halfling.ObjectText;
using Halfling.Random;
using HotCargo.Core;
using HotCargo.Tech;
using HotCargo.Ux;

namespace HotCargo.Relays;

/// <summary>
/// Entry point of the Relays area (spec 950 to 969): rare relay lockdowns. A lockdown is marked on a system next to the players'
/// system, so the galaxy map shows it before they jump in. In that system the relays refuse relay jumps until the lockdown opens:
/// the faction closes them for a time, the faction charges a fee (pay at its station), or pirates hold them (bribe or fight).
/// A blind jump always works. Every rule reads sim state only and changes it only in the sim tick and in MP inputs.
/// </summary>
public static class RelaysArea
{
    public static readonly ID<SimObjectSpawner> GuardTag = new("hc_relay_guard");

    public static void Register()
    {
        Run.Hooks.Tick += (run, sim, dt) => Errors.Guard("relays tick", () => OnTick(run, sim));
        // The switch hook starts a lockdown before the first tick in the new system; the tick covers the first system and loads.
        Run.Hooks.SystemEntered += (run, _) => Errors.Guard("relays arrival", () => Enter(run));
        Ui.CommsHail.Providers.Add(RelayHail.Instance);
        SharedChoice.SharedChoices.Register(RelayChoiceDomain.Instance);
        MultiChoice.MultiChoices.Register(RelayMultiDomain.Instance);
        RelaysAutotest.Register();
    }

    internal static string Text(string key) => Strings.GetText("HotCargoRelays/" + key);

    internal static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoRelays/" + key, args);

    /// <summary>The lockdown of the system that the players are in, or null.</summary>
    public static Lockdown? ActiveHere(Run run) =>
        run.PressureActive && run.State.Relays.Active is { } a && a.Node.Equals(run.Game.CurrentMapNode.UniqueID) ? a : null;

    /// <summary>Spec 954: relay jumps are blocked now. A blind jump passes.</summary>
    public static bool Blocks(Run run) => ActiveHere(run) is { Open: false } && !run.State.Factions.BlindJump;

    /// <summary>The marked kind of a node, or None.</summary>
    public static LockKind MarkedKind(Run run, MapNode node) =>
        run.PressureActive && run.State.Relays.MarkedNode is { } m && m.Equals(node.UniqueID) ? run.State.Relays.MarkedKind : LockKind.None;

    /// <summary>Spec 956: the station collects the relay fee. It never refuses to talk (spec 657).</summary>
    public static bool CollectsFee(Run run, Ship station) =>
        ActiveHere(run) is { Kind: LockKind.Fee, Open: false } && IsFeeStation(run, station);

    /// <summary>
    /// Spec 956: the stations of the controlling faction collect the toll. A system without a station of that faction
    /// (small systems) lets every station but the Buyer collect it, so the toll can always be paid.
    /// </summary>
    internal static bool IsFeeStation(Run run, Ship ship)
    {
        if (!IsTollStation(ship) || run.Game.CurrentMapNode.ControllingFaction is not { } owner) return false;
        if (FactionRules.GetShipFaction(ship)?.ID == owner) return true;
        var sim = ship.Sim ?? throw new InvalidOperationException("station has no sim");
        return !sim.Ships.AllShips.Any(s => IsTollStation(s) && FactionRules.GetShipFaction(s)?.ID == owner);
    }

    static bool IsTollStation(Ship ship) =>
        !ship.IsHumanPlayerShip && !Journey.Buyer.IsBuyer(ship) && SpaceStation.GetStation(ship, onlyOperating: false) is not null;

    public static int FeeCredits(Run run) =>
        Events.EventRunner.Credits(run, RelaysModel.FeeUnits(run.T.Relays.FeeUnits, run.T.Relays.FeePerHeat, run.State.Heat), 0f, run.CurrentTier, run.TechValue());

    public static int BribePrice(Run run) => Events.EventRunner.Credits(run, run.T.Relays.BribeUnits, 0f, run.CurrentTier, run.TechValue());

    static void OnTick(Run run, SimRoot sim)
    {
        if (!run.PressureActive || run.Game.Sim != sim) return;
        Enter(run);
        if (ActiveHere(run) is not { } a) return;
        if (!a.Open) Update(run, sim, a);
        else if (a.Bribed) TruceGuards(sim, a);
    }

    /// <summary>Once for each node that the players are in: start or end lockdowns, then roll the neighbours.</summary>
    static void Enter(Run run)
    {
        var s = run.State.Relays;
        var node = run.Game.CurrentMapNode;
        if (!run.PressureActive || Equals(s.RolledFor, node.UniqueID)) return;
        s.RolledFor = node.UniqueID;
        Arrive(run, node);
        if (run.T.Relays.Enabled) Roll(run, node);
    }

    /// <summary>Spec 954: entering the marked node starts its lockdown. Leaving a node ends its lockdown; a missed mark goes away.</summary>
    static void Arrive(Run run, MapNode node)
    {
        var s = run.State.Relays;
        if (s.Active is { } old && !old.Node.Equals(node.UniqueID)) s.Active = null;
        if (s.MarkedNode is not { } marked) return;
        if (marked.Equals(node.UniqueID))
        {
            s.Active = new Lockdown { Node = marked, Kind = s.MarkedKind, Since = run.State.GameSeconds };
            s.MarkedNode = null;
            Pursuit.Pacer.Started(run, Pursuit.PacedKinds.RelayLockdown);
            string line = Format("Arrive" + s.Active.Kind + "Fmt", Cost(run, s.Active.Kind == LockKind.Fee ? FeeCredits(run) : BribePrice(run)));
            Log.Info($"relays: lockdown starts in {node.Name}: {s.Active.Kind}");
            Run.OnNextFrame(() => UxKit.Guard("relay lockdown line", () => { run.Game.Gui.ShowAreaName(Text("ArriveToast")); UxKit.ChatLine(run.Game, line); }));
        }
        else if (!node.Connections.Any(c => c.UniqueID.Equals(marked)))
        {
            Log.Info("relays: the marked system is out of reach; the mark goes away");
            s.MarkedNode = null;
        }
    }

    /// <summary>
    /// Spec 951, 952: each unvisited neighbour rolls once per run, in ID order, while no mark waits and the pacer allows it.
    /// The Buyer's sector never gets a lockdown. The roll is seeded by the galaxy seed and the node.
    /// </summary>
    static void Roll(Run run, MapNode node)
    {
        var s = run.State.Relays;
        var t = run.T.Relays;
        if (s.MarkedNode is not null || run.State.SectorIndex + 1 >= run.T.Journey.RunSectorCount) return;
        foreach (var next in node.Connections.OrderBy(c => c.UniqueID))
        {
            if (run.State.VisitedNodes.Contains(next.UniqueID) || !s.Rolled.Add(next.UniqueID)) continue;
            if (run.State.SectorIndex < t.MinSector || !Pursuit.Pacer.MayStart(run, Pursuit.PacedKinds.RelayLockdown)) return;
            var rand = new FastRand((ulong)run.Mode.Init.RandSeed ^ 0x5245_4C41_5953UL, (ulong)next.UniqueID.GetHashCode());
            if (rand.Single() >= t.ChancePerSystem) continue;
            var kind = RelaysModel.PickKind(rand.Single(), t.ClosedWeight, t.FeeWeight, t.PiratesWeight, next.ControllingFaction is not null);
            if (kind == LockKind.None) continue;
            Mark(run, next, kind);
            return;
        }
    }

    internal static void Mark(Run run, MapNode node, LockKind kind)
    {
        run.State.Relays.MarkedNode = node.UniqueID;
        run.State.Relays.MarkedKind = kind;
        Log.Info($"relays: {node.Name} marked: {kind}");
        string line = Format("MarkedFmt", node.Name, Text("Kind" + kind));
        Run.OnNextFrame(() => UxKit.Guard("relay mark line", () => UxKit.ChatLine(run.Game, line)));
    }

    /// <summary>Spec 955, 957: a closed relay opens after its time; pirates spawn once and the relays open when every guard is gone.</summary>
    static void Update(Run run, SimRoot sim, Lockdown a)
    {
        switch (a.Kind)
        {
            case LockKind.Closed when RelaysModel.ClosedOpens(run.State.GameSeconds - a.Since, run.T.Relays.ClosedSeconds):
                Open(run, a, Text("ClosedOpens"));
                break;
            case LockKind.Pirates when !a.GuardsSpawned:
                SpawnGuards(run, sim, a);
                break;
            case LockKind.Pirates when a.Guards.Count > 0 && !a.Guards.Any(id => Holds(sim, a, id)):
                Open(run, a, Text("GuardsGone"));
                break;
        }
    }

    internal static void Open(Run run, Lockdown a, string line)
    {
        a.Open = true;
        Log.Info($"relays: lockdown opens ({a.Kind})");
        Run.OnNextFrame(() => UxKit.Guard("relay open line", () => { run.Game.Gui.ShowAreaName(line); UxKit.ChatLine(run.Game, line); }));
    }

    /// <summary>A live guard that still blocks: in this sim, not junk, no truce with the players.</summary>
    internal static Ship? Guard(SimRoot sim, ObjectID id)
    {
        var ship = sim.Ships[id];
        if (ship is null || !ship.ExistsInSim(sim) || ship.IsJunk) return null;
        return ((CareerSimModeManager)sim.Mode).GetTruce(ship) is null ? ship : null;
    }

    /// <summary>
    /// Spec 957: a guard still holds the relay: live with no truce, or in stasis. The guards spawn at the relay, often far from the fleet,
    /// so they start in stasis. After a bribe a guard in stasis does not hold: it gets its truce when it comes out.
    /// </summary>
    internal static bool Holds(SimRoot sim, Lockdown a, ObjectID id) =>
        Guard(sim, id) is not null || (!a.Bribed && sim.Stasis.IsObjectIDInStasis(id));

    /// <summary>Spec 958: every live guard without a truce gets one. Runs at the bribe and on each tick after it.</summary>
    internal static void TruceGuards(SimRoot sim, Lockdown a)
    {
        if (a.Guards.Select(id => Guard(sim, id)).OfType<Ship>().ToList() is not { Count: > 0 } open) return;
        var career = (CareerSimModeManager)sim.Mode;
        var anyShip = Vaults.HumanShips(sim).OrderBy(s => s.UniqueID).FirstOrDefault();
        if (anyShip is null) return;
        foreach (var g in open) career.AddTruce(g, anyShip, Halfling.Timing.Time.FromSeconds(3600));
    }

    /// <summary>Spec 957: the guards spawn from mod/relays/guards.rules next to the relay nearest to the fleet. The group count goes through the co-op scaling.</summary>
    static void SpawnGuards(Run run, SimRoot sim, Lockdown a)
    {
        a.GuardsSpawned = true;
        var fleet = SimQueries.FleetCenter(sim);
        var gates = FtlGate.GetGates(sim).Select(g => (g.DetWorldCenter, g.GateID)).ToList();
        if (fleet is not { } f || gates.Count == 0) { Log.Info("relays: no relay or fleet for the guards; the relays open"); a.Open = true; return; }
        var at = gates.OrderBy(g => g.DetWorldCenter.DistanceSquaredTo(f)).ThenBy(g => g.GateID).First().DetWorldCenter;
        var before = SimQueries.TaggedShips(sim, GuardTag).Select(t => t.Id).ToHashSet();
        Guards.Generator.Generate(sim, new FastRand((ulong)a.Node.GetHashCode(), 0x4755_4152_44UL), loadInstantly: true, rootLoc: at);
        a.Guards = SimQueries.TaggedShips(sim, GuardTag).Select(t => t.Id).Where(id => !before.Contains(id)).Order().ToList();
        if (a.Guards.Count == 0) throw new InvalidOperationException("the relay guard spawner made no ships");
        Log.Info($"relays: {a.Guards.Count} pirate guards at the relay");
    }

    internal static string Cost(Run run, int credits) => Cosmoteer.Extensions.ToXmlCostString(credits, run.Game);

    /// <summary>Spec 958: the resource kinds that a bribe can hold: the fleet holds some, a station buy price, never tech. The most value first.</summary>
    internal static List<(int Option, ResourceRules Rules, int Held)> BribeResources(Run run, SimRoot sim)
    {
        var all = GameApp.Rules.Resources;
        var ships = Vaults.HumanShips(sim).ToList();
        var list = new List<(int, ResourceRules, int)>();
        for (int i = 0; i < all.Length; i++)
        {
            var r = all[i];
            if (r.ID == Vaults.TechId || r.BuyPrice <= 0) continue;
            int held = ships.Sum(sh => sh.Resources.GetResourceTotal(r.ID, includeCarried: false));
            if (held > 0) list.Add((i + 1, r, held));
        }
        return list.OrderByDescending(x => (long)x.Item3 * x.Item2.BuyPrice).ThenBy(x => x.Item1).Take(run.T.Relays.BribeResourceKinds).ToList();
    }
}

/// <summary>Loads the pirate guard spawner from relays/guards.rules in the mod folder. It is not part of the game rules tree.</summary>
internal static class Guards
{
    static SimulationGenerator? _generator;

    public static string FilePath => Path.Combine(Mod.ModRoot, "relays", "guards.rules");

    public static SimulationGenerator Generator => _generator ??= Load();

    static SimulationGenerator Load()
    {
        var generator = Halfling.App.ObjectTextSerializer.ReadFromPath<SimulationGenerator>(new OTFile(FilePath), "Guards")
            ?? throw new InvalidDataException($"Guards missing in {FilePath}");
        if (generator.Spawners is not { Length: > 0 }) throw new InvalidDataException($"Guards/Spawners is empty in {FilePath}");
        return generator;
    }
}

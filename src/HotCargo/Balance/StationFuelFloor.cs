using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships.Special;
using Cosmoteer.Modes.Career.TradeRoutes;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Balance;

/// <summary>
/// Spec 304: every station keeps a floor of fuel for sale, so a run never gets stuck on fuel (relay jumps cost fuel, spec 300).
/// A station fills its stock when it first loads (ShipSpawner.SimulateTrading, then the NPC fuel fraction), and trade ships move it toward the rules target later
/// (TradeRoutesManager.GetDesiredTradeDeltas, the one source of trade ship deltas). The first patch tops the stock up to the floor;
/// the second keeps trade ships from taking it below. Players can buy all of it. Both run in the sim.
/// Limit: a station that refuses the players (factions, spec 657) does not sell its floor. Revisit if refusals start below Strike heat.
/// </summary>
static class StationFuelFloor
{
    static ID<ResourceRules> Fuel => GameApp.Rules.FtlFuelResource;

    /// <summary>
    /// The floor in the system of this sim, or null with no active run in its game. A system switch builds the new sim before the game
    /// switches to it, so this uses the sim's own map node, not the current one.
    /// </summary>
    static int? Floor(SimRoot sim)
    {
        if (Run.Current is not { } run || sim.Game != run.Game || sim.MapNode is not { } node) return null;
        int tier = run.Mode.GetMapNodeData(node).TierRange?.Min ?? 1;
        var t = run.T.Balance;
        return BalanceModel.StationFuelFloor(GameData.TierValueMaximum(tier + 1), t.StationFuelPerMillion, t.StationFuelMinimum);
    }

    /// <summary>
    /// Stations that load before the run exists (the start) or while the next system builds get the floor here: a new run and each
    /// system switch top up the stations that are live.
    /// </summary>
    internal static void OnSystemEntered(Run run, bool firstVisit) => OnNewRun(run);

    internal static void OnNewRun(Run run) => Errors.Guard("station fuel floor", () =>
    {
        var sim = run.Game.Sim;
        if (Floor(sim) is not { } floor) return;
        foreach (var station in SpaceStation.GetStations(sim, onlyOperating: true).OrderBy(s => s.StationID))
            if (station.PhysicalShip is { } ship && ship.Resources.GetResourceTotal(Fuel) is var have && have < floor)
                ship.Resources.AddResources(Fuel, floor - have, excludeConstructionSinks: true);
    });

    static readonly ID<SimObjectSpawner> TradeStationTag = new("trade_station");

    /// <summary>Test only: the floor of the current system, and the least fuel of a trade station that has its stock (loaded or remembered).
    /// Military stations get the floor too, but most have almost no storage (measured: 0 to 6 untyped tiles), so they sell little.</summary>
    internal static int CurrentFloor(SimRoot sim) => Floor(sim) ?? throw new InvalidOperationException("no active run");

    internal static int LeastStationFuel(Run run)
    {
        var sim = run.Game.Sim;
        var rules = run.Mode.CurrentMapNodeData.SectorType.TradeRoutes;
        var least = int.MaxValue;
        foreach (var station in SpaceStation.GetStations(sim, onlyOperating: true))
        {
            if (station.PhysicalShip is null && station.MetaShip.Spawner is Cosmoteer.Simulation.Stasis.StasisShipSpawner { ResourcesMemory: null }) continue;
            var tags = station.PhysicalShip?.Tags ?? (station.Spawner is { } sp ? SimQueries.StasisTags(sp) : null);
            if (tags?.Contains(TradeStationTag) != true) continue;
            var stock = new Dictionary<ID<ResourceRules>, int>();
            station.MetaShip.GetTotalTradeResources(rules, stock, new Dictionary<ID<ResourceRules>, int>());
            least = Math.Min(least, stock.GetValueOrDefault(Fuel));
        }
        return least == int.MaxValue ? throw new InvalidOperationException("no trade station with a stock in this system") : least;
    }

    /// <summary>
    /// After the first spawn of a station: ShipSpawner.SimulateTrading sets the stock, then CareerSimModeManager.OnShipSpawned scales
    /// every NPC fuel storage by NpcSpawnedResourceFractions (25 % to 75 %), so the floor goes after both.
    /// </summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.OnShipSpawned))]
    static class OnLoad
    {
        static void Postfix(CareerSimModeManager __instance, Ship ship, bool isInitSpawn) => Errors.Guard("station fuel floor", () =>
        {
            if (!isInitSpawn || ship.IsHumanPlayerShip || !ship.TryGetSpecialComponent<SpaceStation>(out _) || Floor(__instance.Sim) is not { } floor) return;
            int have = ship.Resources.GetResourceTotal(Fuel);
            if (have < floor) ship.Resources.AddResources(Fuel, floor - have, excludeConstructionSinks: true);
        });
    }

    [HarmonyPatch(typeof(TradeRoutesManager), nameof(TradeRoutesManager.GetDesiredTradeDeltas))]
    static class OnTrade
    {
        static void Postfix(TradeRoutesManager __instance, MetaShip sinkShip, Dictionary<ID<ResourceRules>, int> tradeDeltas) => Errors.Guard("station fuel floor", () =>
        {
            if (!tradeDeltas.TryGetValue(Fuel, out int delta) || delta >= 0 || Floor(__instance.Sim) is not { } floor) return;
            var stock = new Dictionary<ID<ResourceRules>, int>();
            sinkShip.GetTotalTradeResources(__instance.Rules, stock, new Dictionary<ID<ResourceRules>, int>());
            int clamped = BalanceModel.ClampStationFuelDelta(delta, stock.GetValueOrDefault(Fuel), floor);
            if (clamped == 0) tradeDeltas.Remove(Fuel);
            else tradeDeltas[Fuel] = clamped;
        });
    }
}

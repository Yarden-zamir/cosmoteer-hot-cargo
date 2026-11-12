using Cosmoteer;
using Cosmoteer.Factions;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Cosmoteer.Modes.Career.TechTree;
using Cosmoteer.Ships.Special;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Balance;

/// <summary>Entry point of the Balance area: the economy patches (BALANCE.md) and the balance census for tests.</summary>
public static class BalanceArea
{
    public static void Register()
    {
        RegisterContractTests();
        Autotest.Commands["balance_census"] = arg => BalanceCensus.Log(arg.Length > 0 ? arg : "census");
        Autotest.Commands["balance_visit"] = arg => BalanceCensus.Visit(int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture));
        // balance_probe <tier> <count>: spawns hunters of this tier and logs their mass, value, fuel and kill fame.
        Autotest.Commands["balance_probe"] = arg =>
        {
            var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => int.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            BalanceCensus.Probe(a[0], a.Length > 1 ? a[1] : 1);
        };
        // balance_salvage <label>: destroys every hunter part and logs the value of the nuggets in the system after the last mark.
        Autotest.Commands["balance_salvage"] = arg => BalanceCensus.Salvage(arg);
        // Single player only: jumps the human ships to the unvisited neighbor with the highest low tier that is not a faction home system (the main route), without fuel.
        Autotest.Commands["balance_route"] = _ =>
        {
            var run = Autotest.RequireRun();
            int maxTier = Cosmoteer.GameApp.Rules.CareerMode.MapSizes[Journey.JourneyArea.RunMapSizeIndex()].MapGenerator.Spawners
                .OfType<Journey.TierCapSpawner>().Single().MaxChangeableLowTier;
            var next = run.Game.CurrentMapNode.Connections
                .Where(n => !run.State.VisitedNodes.Contains(n.UniqueID) && (run.Mode.GetMapNodeData(n).TierRange?.Min ?? 0) <= maxTier)
                .OrderByDescending(n => run.Mode.GetMapNodeData(n).TierRange?.Min ?? 0).ThenBy(n => n.UniqueID)
                .FirstOrDefault()
                ?? run.Game.CurrentMapNode.Connections.Where(n => !run.State.VisitedNodes.Contains(n.UniqueID)).OrderBy(n => n.UniqueID).FirstOrDefault()
                ?? throw new InvalidOperationException("no unvisited neighbor system");
            run.Game.JumpToNode(next, doFtlAnimation: false, run.Game.Sim.Ships.HumanPlayerShips.ToList())
                .ContinueWith(t => Log.Error("AUTOTEST balance_route failed", t.Exception), TaskContinuationOptions.OnlyOnFaulted);
        };
    }

    static float _fameHeatExpected;

    /// <summary>Fields and commands of tests/autotest/balance-rules.txt: the contracts of spec 300 to 303.</summary>
    static void RegisterContractTests()
    {
        // Paid blueprint price ÷ (rules cost × the station faction's reputation factor). Expect Journey.TechCostScale. Needs tostation first.
        Autotest.Fields["balance_techcost_scale"] = () =>
        {
            var run = Autotest.RequireRun();
            var station = SpaceStation.GetStations(run.Game.Sim, onlyOperating: true).Select(s => s.PhysicalShip).OfType<Cosmoteer.Ships.Ship>().FirstOrDefault()
                ?? throw new InvalidOperationException("no loaded station; run tostation first");
            var tech = GameApp.Rules.CareerMode.TechsByID[new Cosmoteer.Data.ID<TechRules>("cosmoteer.cannon_med")];
            float rep = FactionRules.GetFaction(station.Metadata.PlayerIndex) is { } f ? run.Mode.GetCurrentReputationTitle(f.ID).TechBuyCostFactor : 1f;
            return CommTechsTab.GetTechCost(run.Game, tech, station) / (tech.Cost * (double)rep);
        };
        // balance_fame_over_cap <fame>: gives a mission fame reward while the players are above its fame cap (spec 302), and remembers
        // the heat that spec 25 predicts for the uncapped fame. balance_fame_heat_gap = heat gained − that prediction.
        Autotest.Commands["balance_fame_over_cap"] = arg =>
        {
            var run = Autotest.RequireRun();
            run.Mode.PlayerFame = Math.Max(run.Mode.PlayerFame, 100);
            var reward = new FameReward(int.Parse(arg, System.Globalization.CultureInfo.InvariantCulture), maxFame: 10);
            int uncapped = (int)(reward.Fame * Journey.RewardScalePatch.FameFactor(run.Mode.EconDifficulty));
            float mult = HeatModel.Multiplier(run.T.Heat.HeatMultiplierByUnits, run.State.FleetTech);
            _fameHeatExpected = run.State.Heat + HeatModel.FameHeat(run.T.Heat.HeatPerStandardMission, mult, uncapped, (int)Math.Round(run.StandardMissionFamePaid(run.CurrentTier)));
            reward.GiveReward(run.Game);
        };
        Autotest.Fields["balance_fame_heat_gap"] = () => Autotest.RequireRun().State.Heat - _fameHeatExpected;
        Autotest.Fields["balance_fuel_stack"] = () => GameApp.Rules.FtlFuelResource.GetResourceRules().MaxStackSize;
        Autotest.Fields["balance_fuel_price"] = () => Autotest.RequireRun().Game.Sim.Mode.GetResourceBuyCost(GameApp.Rules.FtlFuelResource, null);
        Autotest.Fields["balance_fuel_station_typed_max"] = () =>
            Autotest.RequireRun().Mode.CurrentMapNodeData.SectorType.TradeRoutes.StationResourceTradeDeltas
                .Single(d => d.ResourceType == GameApp.Rules.FtlFuelResource).PercentOfTypedTiles.Max;
        Autotest.Fields["balance_jump_cost_per_mass_ly"] = () =>
            (SimQueries.FirstHumanShip(Autotest.RequireRun().Game.Sim) ?? throw new InvalidOperationException("no human ship")).Rules.MapJumpFuelCostPerMassDistance;
        Autotest.Fields["balance_peak_tier_cap"] = () =>
        {
            var run = Autotest.RequireRun();
            return Journey.TierCapSpawner.PeakTierCap(run.Mode, run.State.SectorIndex) ?? -1;
        };
    }
}

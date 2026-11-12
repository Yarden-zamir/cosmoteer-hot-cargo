using Cosmoteer;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Cosmoteer.Data;
using HotCargo.Contracts;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Balance;

/// <summary>
/// Test only: measures one system for BALANCE.md. It logs the mission board in standard mission units, the contract terms,
/// the station fuel stock and price, and the fuel cost of each jump. scripts/balance-model.py reads the AUTOTEST BALANCE lines.
/// </summary>
static class BalanceCensus
{
    internal static void Log(string label)
    {
        var run = Autotest.RequireRun();
        var sim = run.Game.Sim ?? throw new InvalidOperationException("no sim");
        int tier = run.CurrentTier;
        float stdMoney = GameData.StandardMissionMoney(tier) * Journey.RewardScalePatch.MoneyFactor(run.Mode.EconDifficulty);
        float stdFame = run.StandardMissionFamePaid(tier);
        var offers = ContractBook.Offers(sim).Select(o => o.Mission).ToList();
        HotCargo.Log.Info($"AUTOTEST BALANCE {label} system sector={run.State.SectorIndex} tier={tier} type={run.Mode.CurrentMapNodeData.SectorType.ID} " +
                          $"offers={offers.Count} stations={SpaceStation.GetStations(sim, onlyOperating: false).Count()} stdMoney={stdMoney:0} stdFame={stdFame:0.0}");
        foreach (var m in offers)
        {
            var terms = ContractBook.Preview(run, m);
            HotCargo.Log.Info($"AUTOTEST BALANCE {label} offer type={m.MetatypeID} cat={m.Metatype.CategoryID} " +
                              $"money={m.GetRewardSortValue(Reward.SortValueType.Money) / stdMoney:0.00} fame={UncappedFame(m) / stdFame:0.00} " +
                              $"fee={(terms?.Fee ?? 0) / stdMoney:0.00} flatHeat={terms?.FlatHeat ?? 0:0} trail={terms?.TrailWeight ?? 0:0.0} informant={terms?.InformantChance ?? 0:0.00}");
        }
        LogFuel(run, sim, label);
    }

    /// <summary>Fame of a mission before the vanilla fame cap: reward fame and the kill fame of its target ships, with the payout factors.</summary>
    static float UncappedFame(Mission m)
    {
        var implicitFame = new Dictionary<int, int>();
        m.GetImplicitFameReward(implicitFame);
        float factor = Journey.RewardScalePatch.FameFactor(((CareerGameModeManager)m.Game.Mode).EconDifficulty);
        return implicitFame.Values.Sum() + m.Rewards.OfType<FameReward>().Sum(f => (int)(f.Fame * factor));
    }

    /// <summary>Moves the human ships next to the station with this index (stations by ID), so that it loads and fills its stock.</summary>
    internal static void Visit(int index)
    {
        var sim = Autotest.RequireRun().Game.Sim;
        var stations = SpaceStation.GetStations(sim, onlyOperating: true).OrderBy(s => s.StationID).ToList();
        if (index >= stations.Count) return;
        var at = stations[index].DetWorldCenter;
        int i = 0;
        foreach (var ship in sim.Ships.HumanPlayerSignificantShips.ToList())
            Autotest.Teleport(ship, Autotest.ClearSpotNear(sim, at, 1500f + 300f * i++, ship.DetWorldBoundingCircle.Radius));
    }

    internal static void Probe(int tier, int count)
    {
        var run = Autotest.RequireRun();
        var sim = run.Game.Sim;
        var before = Hunters.HunterSpawner.Hunters(sim).Select(h => h.UniqueID).ToHashSet();
        Hunters.HunterSpawner.SpawnWave(run, sim, count, tier, "Probe", firstOfStage: false);
        var career = (CareerSimModeManager)sim.Mode;
        foreach (var h in Hunters.HunterSpawner.Hunters(sim).Where(h => !before.Contains(h.UniqueID)))
        {
            career.GetShipFameInfo(h, out int fame, out _, applyDifficultyScaling: true);
            HotCargo.Log.Info($"AUTOTEST BALANCE probe tier={tier} ship={h.Metadata.PlainShipName} spawnTier={career.GetShipTier(h)} mass={h.Parts.Mass:0} " +
                              $"value={h.BaseBlueprintCost} fuel={Hunters.HunterSpawner.Fuel(run, h)} killFame={fame} drives={(h.Ftl.HasFtlDrives ? h.Ftl.GetTotalFuel() : -1)} " +
                              $"jump8LY={(h.Ftl.HasFtlDrives ? h.Ftl.GetFtlMapJumpFuelCost(8f) : -1)}");
        }
    }

    static double _nuggetMark;

    static double NuggetValue(SimRoot sim) => sim.Nuggets.Sum(n => (double)n.Quantity * sim.Mode.GetResourceSellRefund(n.ResourceType, null));

    internal static void Salvage(string label)
    {
        var run = Autotest.RequireRun();
        var sim = run.Game.Sim;
        if (label == "mark")
        {
            _nuggetMark = NuggetValue(sim);
            long value = Hunters.HunterSpawner.Hunters(sim).Sum(h => (long)h.BaseBlueprintCost);
            HotCargo.Log.Info($"AUTOTEST BALANCE salvage mark nuggets={_nuggetMark:0} hunterValue={value}");
            foreach (var h in Hunters.HunterSpawner.Hunters(sim).ToList())
                foreach (var part in h.Parts.ToList()) part.Health = 0;
            return;
        }
        int fuel = sim.Nuggets.Where(n => n.ResourceType == GameApp.Rules.FtlFuelResource).Sum(n => n.Quantity);
        HotCargo.Log.Info($"AUTOTEST BALANCE salvage {label} nuggetValue={NuggetValue(sim) - _nuggetMark:0} fuelNuggets={fuel} money={run.Game.LocalPlayerMoney}");
    }

    static void LogFuel(Run run, SimRoot sim, string label)
    {
        var fuel = GameApp.Rules.FtlFuelResource;
        var rules = run.Mode.CurrentMapNodeData.SectorType.TradeRoutes;
        var stock = new Dictionary<ID<ResourceRules>, int>();
        var caps = new Dictionary<ID<ResourceRules>, int>();
        var stocks = new List<int>();
        foreach (var station in SpaceStation.GetStations(sim, onlyOperating: true).OrderBy(s => s.StationID))
        {
            // A station that never loaded has no resources yet: the game fills its stock on the first load (ShipSpawner.SimulateTrading).
            if (station.PhysicalShip is null && station.MetaShip.Spawner is Cosmoteer.Simulation.Stasis.StasisShipSpawner { ResourcesMemory: null })
            {
                stocks.Add(-1);
                continue;
            }
            stock.Clear();
            caps.Clear();
            station.MetaShip.GetTotalTradeResources(rules, stock, caps);
            stocks.Add(stock.GetValueOrDefault(fuel));
        }
        int price = sim.Mode.GetResourceBuyCost(fuel, null);
        var ship = SimQueries.FirstHumanShip(sim) ?? throw new InvalidOperationException("no human ship");
        string distances = string.Join(",", run.Game.CurrentMapNode.Connections.Select(n => $"{run.Game.CurrentMapNode.Location.DistanceTo(n.Location):0.0}"));
        string jumps = ship.Ftl.HasFtlDrives
            ? string.Join(",", run.Game.CurrentMapNode.Connections.Select(n => ship.Ftl.GetFtlMapJumpFuelCost(run.Game.CurrentMapNode.Location.DistanceTo(n.Location))))
            : "none";
        int held = ship.Resources.GetResourceTotal(fuel, includeStored: true, includeCarried: false);
        HotCargo.Log.Info($"AUTOTEST BALANCE {label} fuel stationStock=[{string.Join(",", stocks)}] buyPrice={price} shipMass={ship.Parts.Mass:0} " +
                          $"shipValue={ship.BaseBlueprintCost} held={held} jumpCosts=[{jumps}] mapDistances=[{distances}] money={run.Game.LocalPlayerMoney}");
    }
}

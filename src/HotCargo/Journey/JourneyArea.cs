using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Cosmoteer.Resources;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Simulation.Stasis;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Journey;

/// <summary>Entry point of the Journey area. Mod.Boot calls Register once at startup, before the game starts.</summary>
public static class JourneyArea
{
    /// <summary>NameKey of the Hot Cargo run map size in mod/journey/galaxy.rules.</summary>
    const string RunMapNameKey = "HotCargoJourney/MapSize";

    public static void Register()
    {
        Run.Hooks.SystemEntered += Buyer.OnSystemEntered;
        Run.Hooks.NewRunStarted += Buyer.OnNewRun;
        AddAutotest();
    }

    /// <summary>Index of the Hot Cargo run map in the career map size list. Throws if the mod rules did not add it.</summary>
    public static int RunMapSizeIndex()
    {
        int i = Array.FindIndex(GameApp.Rules.CareerMode.MapSizes, m => m.NameKey.Key == RunMapNameKey);
        return i >= 0 ? i : throw new InvalidOperationException($"map size {RunMapNameKey} not found; is the Hot Cargo rules mod on?");
    }

    /// <summary>Galaxy nodes by jump distance from the starting system, for map checks.</summary>
    static List<List<MapNode>> NodesByJumps(Run run)
    {
        var start = run.Game.Map.Nodes.Single(n => run.Mode.GetMapNodeData(n).IsInitNode);
        var seen = new HashSet<MapNode> { start };
        var rings = new List<List<MapNode>> { new() { start } };
        while (true)
        {
            var next = rings[^1].SelectMany(n => n.Connections).Where(seen.Add).ToList();
            if (next.Count == 0) return rings;
            rings.Add(next);
        }
    }

    static int LowTier(Run run, MapNode n) =>
        run.Mode.GetMapNodeData(n).TierRange?.Min ?? throw new InvalidOperationException($"node {n.Name} has no tier");

    static void AddAutotest()
    {
        Autotest.Commands["buyer"] = arg =>
        {
            var run = Autotest.RequireRun();
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts)
            {
                case ["spawn"]:
                    Buyer.Spawn(run);
                    break;
                // Calls the win path that the trade delivery patch calls. The real sale needs crew to walk to the station.
                case ["deliver", var units]:
                    Buyer.OnDelivered(run, int.Parse(units, CultureInfo.InvariantCulture));
                    break;
                // Test-only switch: treat a vanilla resource as the tech, so the Buyer price runs without the Tech area.
                case ["resource", var id]:
                    Buyer.TechResource = new ID<ResourceRules>(id);
                    break;
                // Test-only: changes the live tunable, so that a short jump test reaches the Buyer sector.
                case ["sectors", var count]:
                    run.T.Journey.RunSectorCount = int.Parse(count, CultureInfo.InvariantCulture);
                    break;
                // Test-only: puts the first human ship next to the Buyer. Run it twice: the first call loads the Buyer
                // out of stasis, the second puts the ship within trade distance.
                case ["goto"]:
                {
                    var sim = run.Game.Sim;
                    var ship = sim.Ships.HumanPlayerShips.FirstOrDefault() ?? throw new InvalidOperationException("no human ship");
                    var loaded = sim.Ships.FirstOrDefault(Buyer.IsBuyer);
                    Vector2 loc = loaded?.DetWorldCenter
                        ?? sim.Stasis.OfType<StasisShipSpawner>().FirstOrDefault(s => s.Tags?.Contains(Buyer.Tag) == true)?.Location
                        ?? throw new InvalidOperationException("no Buyer in this system");
                    float gap = loaded is null ? 1200f : loaded.DetWorldBoundingCircle.Radius + ship.DetWorldBoundingCircle.Radius + 100f;
                    ship.WorldCenter = loc + new Vector2(gap, 0f);
                    ship.Physics.PhysicsWrapper.LinearVelocity = Vector2.Zero;
                    Log.Info($"AUTOTEST goto: ship {ship.Name} to {ship.WorldCenter} (det {ship.DetWorldCenter}), Buyer at {loc}, loaded={loaded is not null}, humans={sim.Ships.HumanPlayerShips.Count()}");
                    break;
                }
                // Test-only: a real sale through the trade input. Adds the units to the first human ship first.
                case ["sell", var count]:
                {
                    var sim = run.Game.Sim;
                    var ship = sim.Ships.HumanPlayerShips.FirstOrDefault() ?? throw new InvalidOperationException("no human ship");
                    var buyer = sim.Ships.FirstOrDefault(Buyer.IsBuyer) ?? throw new InvalidOperationException("no loaded Buyer ship");
                    var res = Buyer.TechResource;
                    int units = int.Parse(count, CultureInfo.InvariantCulture);
                    ship.Resources.AddResources(res, units);
                    var source = ship.Resources.GetBestSendingSource(res, MPValueType.Confirmed, out int have)
                        ?? throw new InvalidOperationException($"no {res} on the ship");
                    var sink = buyer.Resources.GetBestReceivingSink(res, units, MPValueType.Confirmed, out int capacity,
                        includeAnticipatedDeliveries: false, externalDelivery: true)
                        ?? throw new InvalidOperationException($"the Buyer has no room for {res}");
                    int amount = Math.Min(units, Math.Min(have, capacity));
                    int price = sim.Mode.GetResourceSellRefund(res, buyer.Metadata.PlayerIndex);
                    Log.Info($"AUTOTEST sell {amount} {res} to the Buyer at {price} each");
                    run.Game.NetManager.EnqueueInput(new MPMoveResourcesInput(source, sink, amount, ship.Jobs.AllocLocalJobID(),
                        tradeType: TradeType.Sell, pricePerResource: price));
                    break;
                }
                default:
                    throw new InvalidOperationException("buyer needs: spawn | deliver <units> | resource <id> | sectors <count> | goto | sell <units>");
            }
        };
        // Single player only: jumps the human ships to an unvisited neighbor system, without the FTL animation or fuel.
        Autotest.Commands["jump"] = _ =>
        {
            var run = Autotest.RequireRun();
            var next = run.Game.CurrentMapNode.Connections.FirstOrDefault(n => !run.State.VisitedNodes.Contains(n.UniqueID))
                ?? throw new InvalidOperationException("no unvisited neighbor system");
            run.Game.JumpToNode(next, doFtlAnimation: false, run.Game.Sim.Ships.HumanPlayerShips.ToList())
                .ContinueWith(t => Log.Error("AUTOTEST jump failed", t.Exception), TaskContinuationOptions.OnlyOnFaulted);
        };
        Autotest.Commands["maptiers"] = _ =>
        {
            var run = Autotest.RequireRun();
            var rings = NodesByJumps(run);
            for (int jumps = 0; jumps < rings.Count; jumps++)
            {
                var tiers = rings[jumps].Select(n => LowTier(run, n)).Order().ToList();
                Log.Info($"AUTOTEST MAP jumps={jumps} sector={jumps + 1} nodes={tiers.Count} lowTiers=[{string.Join(",", tiers)}]");
            }
        };
        Autotest.Commands["rewardcheck"] = _ =>
        {
            var run = Autotest.RequireRun();
            var game = run.Game;
            float scale = run.T.Journey.RewardScale;
            var econ = run.Mode.EconDifficulty;
            var players = new List<PlayerState>();
            game.GetHumanPlayerStates(players, deterministic: true);
            int moneyBefore = players.Sum(p => p.Money);
            var mission = new Mission(game, Buyer.MissionType);
            new MoneyReward(mission, 1000).GiveReward(game);
            int moneyGained = players.Sum(p => p.Money) - moneyBefore;
            int expectedMoney = (int)(1000f * (econ.MoneyRewardFactor * scale)) / players.Count * players.Count;
            Autotest.Check(moneyGained == expectedMoney, $"money reward 1000 x econ {econ.MoneyRewardFactor} x scale {scale} = {expectedMoney} (actual {moneyGained})");
            int fameBefore = run.Mode.PlayerFame;
            new FameReward(10).GiveReward(game);
            int fameGained = run.Mode.PlayerFame - fameBefore;
            int expectedFame = (int)(10f * (econ.FameRewardFactor * scale));
            Autotest.Check(fameGained == expectedFame, $"fame reward 10 x econ {econ.FameRewardFactor} x scale {scale} = {expectedFame} (actual {fameGained})");
        };

        Autotest.Fields["buyers"] = () => Buyer.Count(Autotest.RequireRun().Game.Sim);
        Autotest.Fields["buyermission"] = () =>
            Autotest.RequireRun().Mode.Missions.Count(m => m.MetatypeID == Buyer.MissionType);
        // 1 if the loaded Buyer trades with the first human ship in the system.
        Autotest.Fields["buyertrades"] = () =>
        {
            var sim = Autotest.RequireRun().Game.Sim;
            var buyer = sim.Ships.FirstOrDefault(Buyer.IsBuyer) ?? throw new InvalidOperationException("no loaded Buyer ship");
            var human = sim.Ships.HumanPlayerShips.FirstOrDefault() ?? throw new InvalidOperationException("no human ship");
            return buyer.AI?.WillTradeWith(human) == true ? 1 : 0;
        };
        Autotest.Fields["nodes"] =() => Autotest.RequireRun().Game.Map.Nodes.NodeCount;
        Autotest.Fields["runmap"] = () => Autotest.RequireRun().Mode.Init is Cosmoteer.Modes.Career.CareerInit init && init.MapSizeIndex == RunMapSizeIndex() ? 1 : 0;
        // Highest low tier among systems that are RunSectorCount - 1 jumps from the start: the last sector of a direct route.
        Autotest.Fields["finaltier"] = () =>
        {
            var run = Autotest.RequireRun();
            var rings = NodesByJumps(run);
            int jumps = Math.Min(run.T.Journey.RunSectorCount - 1, rings.Count - 1);
            return rings[jumps].Max(n => LowTier(run, n));
        };
        // Sell price to the Buyer divided by the sell price with no destination. Expect BuyerPriceFactor.
        Autotest.Fields["buyerprice"] = () =>
        {
            var sim = Autotest.RequireRun().Game.Sim;
            double toBuyer = sim.Mode.GetResourceSellRefund(Buyer.TechResource, PlayerIndexes.Neutral);
            double plain = sim.Mode.GetResourceSellRefund(Buyer.TechResource, null);
            return plain > 0 ? toBuyer / plain : throw new InvalidOperationException($"resource {Buyer.TechResource} has no sell price");
        };
    }
}

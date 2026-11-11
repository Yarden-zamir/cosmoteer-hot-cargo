using System.Globalization;
using Cosmoteer;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Ships.Resources;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Tech;

/// <summary>Autotest commands and fields of the Tech area. The game runs them from the frame loop, in single player only.</summary>
static class TechAutotest
{
    internal static void Register()
    {
        Autotest.Commands["tech"] = Tech;
        Autotest.Commands["vault"] = Vault;
        Autotest.Commands["tostation"] = _ => ToStation();
        Autotest.Fields["techvalue"] = () => { var r = Active(); return r.T.Tech.Value(r.State); };
        Autotest.Fields["sellprice"] = () => Active().Game.Sim.Mode.GetResourceSellRefund(Vaults.TechId, null);
        Autotest.Fields["buyprice"] = () => Active().Game.Sim.Mode.GetResourceBuyCost(Vaults.TechId, null);
        Autotest.Fields["money"] = Money;
        Autotest.Fields["moneygain"] = () => Money() - _moneyAtSell;
        // Must stay 0: the price a station pays equals the tech value (spec 19, 20).
        Autotest.Fields["pricegap"] = () => Active().Game.Sim.Mode.GetResourceSellRefund(Vaults.TechId, null) - Autotest.Fields["techvalue"]();
        Autotest.Fields["vaults"] = () => Vaults.All(Active().Game.Sim).Count;
        Autotest.Fields["vaulttech"] = () => Vaults.All(Active().Game.Sim).Sum(g => g.Resources);
        Autotest.Fields["livetech"] = () => Vaults.CountFleetTech(Active().Game.Sim);
        Autotest.Fields["heatmult"] = () => HeatModel.Multiplier(Active().T.Heat.HeatMultiplierByUnits, Active().State.FleetTech);
        // Normal storage tiles on human ships that would accept tech. Must stay 0 (spec 14).
        Autotest.Fields["flexsinks"] = () => Vaults.HumanShips(Active().Game.Sim).Sum(s => FlexTilesTakingTech(s));
        Autotest.Fields["stationsinks"] = () => FlexTilesTakingTech(NearestStation());
        Autotest.Fields["stationtech"] = () => NearestStation().Resources.GetResourceTotal(Vaults.TechId);
    }

    static Run Active() => Autotest.RequireRun();

    static double _moneyAtSell;
    static bool _tradeLogged;

    static double Money() => Active().Game.GetPlayerState(Active().Game.LocalPlayerIndex, deterministic: true).Money;

    static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>tech add N | tech remove N | tech sell N | tech dump</summary>
    static void Tech(string arg)
    {
        var run = Active();
        var sim = run.Game.Sim;
        var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts)
        {
            case ["add", var n]: Log.Info($"AUTOTEST tech added {Vaults.Add(sim, Int(n))}"); break;
            case ["remove", var n]: Log.Info($"AUTOTEST tech removed {Vaults.Remove(sim, Int(n))}"); break;
            case ["sell", var n]: Sell(run, Int(n)); break;
            case ["dump"]: Dump(run); break;
            default: throw new InvalidOperationException($"tech: unknown arguments '{arg}'");
        }
    }

    /// <summary>vault damage F (fraction of max health) | vault destroy | vault repair. Acts on the first vault.</summary>
    static void Vault(string arg)
    {
        var grid = Vaults.All(Active().Game.Sim).FirstOrDefault() ?? throw new InvalidOperationException("no vault");
        Part part = grid.Part ?? throw new InvalidOperationException("vault grid has no part");
        var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts)
        {
            case ["damage", var f]:
                part.Health -= (int)(float.Parse(f, CultureInfo.InvariantCulture) * part.Rules.MaxHealth);
                break;
            case ["destroy"]: part.Health = 0; break;
            case ["repair"]: part.Health = part.Rules.MaxHealth; break;
            default: throw new InvalidOperationException($"vault: unknown arguments '{arg}'");
        }
        Log.Info($"AUTOTEST vault {arg}: health {part.Health}/{part.Rules.MaxHealth}, tech in it {grid.Resources}");
    }

    internal static Ship PlayerShip() => SimQueries.FirstHumanShip(Active().Game.Sim) ?? throw new InvalidOperationException("no player ship");

    /// <summary>The trading station nearest to the first human ship. Throws when the system has none.</summary>
    internal static Ship NearestStation()
    {
        var ship = PlayerShip();
        return Active().Game.Sim.Ships.AllShips
            .Where(s => s != ship && s.AI?.WillTradeWith(ship) == true)
            .OrderBy(s => s.WorldCenter.DistanceSquaredTo(ship.WorldCenter))
            .FirstOrDefault() ?? throw new InvalidOperationException("no trading station in this system");
    }

    /// <summary>Moves the player ship next to the nearest trading station, so that crew can trade. Test only.</summary>
    static void ToStation()
    {
        var ship = PlayerShip();
        var station = NearestStation();
        Autotest.Teleport(ship, station.WorldCenter + new Halfling.Geometry.Vector2(station.WorldBoundingRadius + ship.WorldBoundingRadius + 2f, 0f));
        Log.Info($"AUTOTEST moved {ship.Name} next to {station.Name}; trade allowed={Active().Game.Sim.Mode.AllowResourceTrade(ship, station)}");
    }

    static int FlexTilesTakingTech(Ship ship) =>
        FlexResourceGrid.GetAllOnShip(ship).SelectMany(g => g).Count(t => ((IResourceSink)t).GetRemainingCapacity(Vaults.TechId, allowPreferredTypeMismatch: false) > 0);

    /// <summary>Sells tech to the nearest station with the same inputs as the trade window (CrewAndResourceTransferWindow.DoTransfer).</summary>
    static void Sell(Run run, int units)
    {
        var game = run.Game;
        var ship = PlayerShip();
        var station = NearestStation();
        _moneyAtSell = Money();
        if (!_tradeLogged)
        {
            _tradeLogged = true;
            game.EventHub.ResourceTradePickup += (_, e) => Log.Info($"AUTOTEST trade pickup {e.TradeType} {e.ResourceType} x{e.Amount} by crew of player {e.Crew.PlayerIndex}, fleet count {Vaults.CountFleetTech(game.Sim)}");
            game.EventHub.ResourceTradeDelivery += (_, e) => Log.Info($"AUTOTEST trade delivery {e.TradeType} {e.ResourceType} x{e.Amount}, money {Money()}");
        }
        var sources = new List<(IResourceSource Source, int AnticipatedResources, float Distance)>();
        ship.Resources.SearchAllAvailableResourceSources(station, Vaults.TechId, MPValueType.Displayed, sources);
        var sinks = new List<(IResourceSink Sink, int AnticipatedCapacity, float Distance)>();
        station.Resources.SearchAllAvailableResourceSinks(ship, Vaults.TechId, MPValueType.Displayed, sinks);
        Log.Info($"AUTOTEST sell: {sources.Count} sources, {sinks.Count} station sinks with room {sinks.Sum(s => s.AnticipatedCapacity)}");
        int left = units;
        foreach (var (source, available, _) in sources)
        {
            int fromSource = available;
            for (int i = 0; i < sinks.Count && left > 0 && fromSource > 0; i++)
            {
                var (sink, room, dist) = sinks[i];
                int n = Math.Min(left, Math.Min(fromSource, room));
                if (n <= 0) continue;
                int price = game.Sim.Mode.GetResourceSellRefund(Vaults.TechId, sink.Ship?.Metadata.PlayerIndex);
                var id = MPMoveResourcesInput.GetJobOwner(source, sink, game.LocalPlayerIndex).Jobs.AllocLocalJobID();
                game.NetManager.EnqueueInput(new MPMoveResourcesInput(source, sink, n, id, sticky: false, TradeType.Sell, price));
                sinks[i] = (sink, room - n, dist);
                left -= n;
                fromSource -= n;
            }
        }
        Log.Info($"AUTOTEST sell: queued {units - left} of {units}");
    }

    static void Dump(Run run)
    {
        var sim = run.Game.Sim;
        foreach (var ship in Vaults.HumanShips(sim))
        {
            Log.Info($"AUTOTEST ship {ship.Name}: blueprint valid={ship.BlueprintParts.IsValid} matches={ship.PhysicalMatchesBlueprints}");
            foreach (var p in ship.BlueprintParts.OrderBy(p => p.Location.Y).ThenBy(p => p.Location.X))
                Log.Info($"  part {p.Rules.ID} at {p.Location} rect {p.Rect}");
            foreach (var d in ship.BlueprintDoors) Log.Info($"  door {d.Location}");
        }
        foreach (var starter in GameApp.Rules.CareerMode.StarterShips)
        {
            var s = Ship.Load(Ship.LoadBytes(starter.Ship.FilePath), starter.Ship.ShipName, blueprintsOnly: true);
            var storage = s.BlueprintParts.Where(p => p.Rules.ID.ToString().Contains("storage")).Select(p => $"{p.Rules.ID}@{p.Location}");
            Log.Info($"AUTOTEST starter {starter.Ship.ShipName}: {s.BlueprintParts.Count} parts; storage: {string.Join(", ", storage)}");
        }
        foreach (var s in sim.Ships.AllShips.Where(s => !sim.Mode.IsHumanPlayerShip(s)))
            Log.Info($"AUTOTEST other ship {s.Name} player={s.Metadata.PlayerIndex} trades={s.AI?.WillTradeWith(PlayerShip())} flexTilesForTech={FlexTilesTakingTech(s)}");
    }
}

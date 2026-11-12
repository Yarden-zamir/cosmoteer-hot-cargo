using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI.CommsModules;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Resources;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Missions;
using HotCargo.Tech;

namespace HotCargo.Factions;

/// <summary>Autotest commands and fields of the Factions area. Commands that change the sim run under "mp" in co-op tests.</summary>
static class FactionsAutotest
{
    static Run R() => Autotest.RequireRun();

    /// <summary>Values that fa_base records, so fields can report a price as a ratio to it.</summary>
    static double _baseBuy = double.NaN, _baseCrew = double.NaN, _baseFuel = double.NaN, _baseOffers = double.NaN;
    static double _markHeat, _markSeconds, _markNotoriety;
    static double _moneyBeforeBuy = double.NaN, _expectedBuy;

    internal static void Register()
    {
        // fa_roles <ally> <rival>: each is a faction ID, "here" (the controller of this system), "station" (the faction of the
        // nearest station), "other" (a faction that is neither this system's controller nor the nearest station's), or "-" (seed pick).
        Autotest.Commands["fa_roles"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new InvalidOperationException("fa_roles needs: <ally> <rival>");
            var t = R().T.Factions;
            t.AllyFaction = Pick(parts[0]);
            t.RivalFaction = Pick(parts[1]);
            var (ally, rival) = FactionsArea.Roles(R());
            Log.Info($"AUTOTEST factions roles: ally {ally.ID}, rival {rival.ID}, controller {FactionsArea.Controller(R().Game.CurrentMapNode)?.ID}");
        };
        // fa_rep <n>: sets the reputation with this system's controlling faction.
        Autotest.Commands["fa_rep"] = arg =>
        {
            var f = FactionsArea.Controller(R().Game.CurrentMapNode) ?? throw new InvalidOperationException("this system has no controlling faction");
            R().Mode.SetFactionReputation(f.ID, int.Parse(arg, CultureInfo.InvariantCulture));
        };
        // fa_set <FactionsTunables field> <value>: changes a live tunable for this run.
        Autotest.Commands["fa_set"] = arg => Autotest.SetField(R().T.Factions, arg);
        // fa_blind on|off: the real player action (the MP input of the switch under ENGAGE).
        Autotest.Commands["fa_blind"] = arg => R().Game.NetManager.EnqueueInput(new BlindJumpInput(arg == "on"));
        // fa_jump relay|blind|view: single player. Jumps to an unvisited neighbor like the "jump" command, as a jump of that kind.
        Autotest.Commands["fa_jump"] = arg =>
        {
            var run = R();
            var next = run.Game.CurrentMapNode.Connections.FirstOrDefault(n => !run.State.VisitedNodes.Contains(n.UniqueID))
                ?? throw new InvalidOperationException("no unvisited neighbor system");
            Jumps.TestOverride = Enum.Parse<JumpKind>(arg, ignoreCase: true);
            run.Game.JumpToNode(next, doFtlAnimation: false, run.Game.Sim.Ships.HumanPlayerShips.ToList())
                .ContinueWith(t => Log.Error("AUTOTEST fa_jump failed", t.Exception), TaskContinuationOptions.OnlyOnFaulted);
        };
        // fa_drive: single player. Builds a small hyperdrive on the player ship (the starter ships have none), for the fuel checks.
        Autotest.Commands["fa_drive"] = _ =>
        {
            var ship = TechAutotest.PlayerShip();
            // Careers start with the hyperdrive blueprint locked, and a locked part is not a valid blueprint part.
            R().Mode.Techs.UnlockTech(new ID<Cosmoteer.Modes.Career.TechTree.TechRules>("cosmoteer.hyperdrive_small"));
            StartingVault.PlaceOutside(ship, ship.Rules.PartsByID[new ID<Cosmoteer.Ships.Parts.PartRules>("cosmoteer.hyperdrive_small")], requireDoor: false);
            Log.Info($"AUTOTEST factions drive: {ship.Name} has drives={ship.Ftl.HasFtlDrives}");
        };
        // fa_base: records the prices that the ratio fields compare to.
        Autotest.Commands["fa_base"] = _ =>
        {
            _baseBuy = BuyPrice();
            _baseCrew = CrewPrice();
            _baseFuel = Fuel();
            _baseOffers = OfferMoney();
            Log.Info($"AUTOTEST factions base: buy {_baseBuy} crew {_baseCrew} fuel {_baseFuel} offers {_baseOffers}");
        };
        // fa_mark: records heat, game time and notoriety for fa_measuredrate and fa_notorietyratio.
        Autotest.Commands["fa_mark"] = _ => { var s = R().State; (_markHeat, _markSeconds, _markNotoriety) = (s.Heat, s.GameSeconds, s.Notoriety); };
        // fa_hail: hails the nearest station like the comms button (UI path, shows a refusal message).
        Autotest.Commands["fa_hail"] = arg =>
        {
            CommsPatches.LastRefusal = "";
            _ = ((CareerSimModeManager)R().Game.Sim.Mode).TryShipCall(TechAutotest.PlayerShip(), TechAutotest.NearestStation());
        };
        // fa_buy <n>: the real player action. Buys up to n units of the first resource that the nearest station sells, like the trade window.
        Autotest.Commands["fa_buy"] = arg => Buy(int.Parse(arg, CultureInfo.InvariantCulture));
        // fa_screen comms|close|hire: for screenshots. Opens or closes the comms list, or shows the hire tab of the open comms window.
        Autotest.Commands["fa_screen"] = arg =>
        {
            switch (arg)
            {
                case "comms" or "close": R().Mode.CommsToolbox.SelfActive = arg == "comms"; break;
                case "hire":
                    if (!Cosmoteer.Modes.Career.Comms.CommWindow.s_openWindows.TryGetValue(TechAutotest.NearestStation(), out var window))
                        throw new InvalidOperationException("no comms window with the nearest station");
                    window._tabs.TabsListBox.SelectedWidget = window._hireTab ?? throw new InvalidOperationException("the station has no hire tab");
                    break;
                default: throw new InvalidOperationException("fa_screen needs: comms | close | hire");
            }
        };
        Autotest.Commands["fa_tooltip"] = _ =>
        {
            var run = R();
            foreach (var node in run.Game.CurrentMapNode.Connections.Prepend(run.Game.CurrentMapNode))
                Log.Info($"AUTOTEST factions tooltip {node.Name}:{MapPatches.Lines(run, node).Replace('\n', '|')}");
            Log.Info($"AUTOTEST factions badges: {CommsPatches.Badges(run, TechAutotest.PlayerShip(), TechAutotest.NearestStation())}");
            Log.Info($"AUTOTEST factions blind button: {Jumps.ButtonText(run)}");
        };

        Autotest.Fields["fa_zone"] = () => (int)FactionsArea.ZoneOf(R(), R().Game.CurrentMapNode);
        Autotest.Fields["fa_timefactor"] = () => Run.Hooks.TimeHeatFactor(R());
        Autotest.Fields["fa_heatrate"] = () => R().TimeHeatPerMinute(R().State.FleetTech);
        // Heat per game minute since fa_mark, divided by the rate that the heat panel shows. 1 when the tick applies the zone factor.
        Autotest.Fields["fa_rateratio"] = () =>
            (R().State.Heat - _markHeat) / Math.Max(1e-6, (R().State.GameSeconds - _markSeconds) / 60.0) / R().TimeHeatPerMinute(R().State.FleetTech);
        Autotest.Fields["fa_notorietyratio"] = () => (R().State.Notoriety - _markNotoriety) / Math.Max(1e-6, R().State.Heat - _markHeat);
        Autotest.Fields["fa_markup"] = () => FactionsArea.Markup(R());
        Autotest.Fields["fa_crewmarkup"] = () => FactionsArea.CrewMarkup(R());
        Autotest.Fields["fa_buyratio"] = () => BuyPrice() / Base(_baseBuy);
        Autotest.Fields["fa_crewratio"] = () => CrewPrice() / Base(_baseCrew);
        Autotest.Fields["fa_fuel"] = Fuel;
        Autotest.Fields["fa_fuelratio"] = () => Fuel() / Base(_baseFuel);
        // Fuel now minus the relay fuel of fa_base times BlindFuelFactor, rounded up. 0 with the blind switch on (one player ship).
        Autotest.Fields["fa_blindfuelgap"] = () => Fuel() - FactionsModel.Scale((int)Base(_baseFuel), R().T.Factions.BlindFuelFactor);
        Autotest.Fields["fa_offerratio"] = () => OfferMoney() / Base(_baseOffers);
        Autotest.Fields["fa_rivalsell"] = () => SellRatio(FactionsArea.Roles(R()).Rival);
        Autotest.Fields["fa_plainsell"] = () => SellRatio(GameApp.Rules.Factions.First(f => f != FactionsArea.Roles(R()).Rival));
        Autotest.Fields["fa_refuses"] = () => FactionsArea.Refuses(R(), TechAutotest.PlayerShip(), TechAutotest.NearestStation()) ? 1 : 0;
        Autotest.Fields["fa_answers"] = () => ((CareerSimModeManager)R().Game.Sim.Mode).WillShipAnswerCall(TechAutotest.PlayerShip(), TechAutotest.NearestStation()) ? 1 : 0;
        Autotest.Fields["fa_trade"] = () => R().Game.Sim.Mode.AllowResourceTrade(TechAutotest.PlayerShip(), TechAutotest.NearestStation()) ? 1 : 0;
        Autotest.Fields["fa_refusal"] = () => CommsPatches.LastRefusal.Length > 0 ? 1 : 0;
        Autotest.Fields["fa_blind"] = () => R().State.Factions.BlindJump ? 1 : 0;
        Autotest.Fields["fa_markers"] = () => Markers.Compute(R()).Count;
        Autotest.Fields["fa_markershere"] = () => Markers.Compute(R()).Count(m => m.Node == R().Game.CurrentMapNode);
        Autotest.Fields["fa_spent"] = () => double.IsNaN(_moneyBeforeBuy) ? 0 : _moneyBeforeBuy - Money();
        // Money that fa_buy took minus amount x the price in its input. 0 when the sim accepted the trade at the price of the click.
        Autotest.Fields["fa_buygap"] = () => (double.IsNaN(_moneyBeforeBuy) ? 0 : _moneyBeforeBuy - Money()) - _expectedBuy;
    }

    static double Base(double value) => double.IsNaN(value) || value <= 0 ? throw new InvalidOperationException("run fa_base first (and the base must be above 0)") : value;

    static string Pick(string what)
    {
        var run = R();
        var controller = FactionsArea.Controller(run.Game.CurrentMapNode);
        var station = FactionRules.GetShipFaction(TechAutotest.NearestStation());
        return what switch
        {
            "-" => "",
            "here" => (controller ?? throw new InvalidOperationException("this system has no controlling faction")).ID.ToString(),
            "station" => (station ?? throw new InvalidOperationException("the nearest station has no faction")).ID.ToString(),
            "other" => GameApp.Rules.Factions.First(f => f != controller && f != station && f.ID.ToString() != run.T.Factions.AllyFaction).ID.ToString(),
            _ => FactionsArea.FactionById(what)?.ID.ToString() ?? throw new InvalidOperationException($"no faction '{what}'"),
        };
    }

    static double Money() => R().Game.GetPlayerState(R().Game.LocalPlayerIndex, deterministic: true).Money;

    /// <summary>A priced resource that is not tech, in rules order, so every run tests the same one.</summary>
    static ID<ResourceRules> PricedResource() => GameApp.Rules.Resources.First(r => r.BuyPrice > 0 && r.ID != Vaults.TechId).ID;

    static double BuyPrice() =>
        R().Game.Sim.Mode.GetResourceBuyCost(PricedResource(), TechAutotest.NearestStation().Metadata.PlayerIndex);

    static double SellRatio(FactionRules f) =>
        R().Game.Sim.Mode.GetResourceSellRefund(Vaults.TechId, f.CivilianPlayerIndex) / (double)R().TechValue();

    static double CrewPrice()
    {
        var run = R();
        var station = TechAutotest.NearestStation();
        if (station.AI?.GetCommsModule<AICrewProviderModule>() is not { } module) return double.NaN;
        return module.GetHireCost(run.Game, station, run.Mode.GetTotalPlayerCrew(run.Game.LocalPlayerIndex) + 1, run.Mode.GetCurrentMaxCrew());
    }

    /// <summary>Hyperium of all human ships to the first unvisited neighbor, as the jump window shows it.</summary>
    static double Fuel()
    {
        var run = R();
        if (!run.Game.Sim.Ships.HumanPlayerSignificantShips.Any(s => s.Ftl.HasFtlDrives)) return 0;
        var next = run.Game.CurrentMapNode.Connections.First(n => !run.State.VisitedNodes.Contains(n.UniqueID));
        return run.Game.Map.GetTotalFtlJumpFuelCost(run.Game.Sim.Ships.HumanPlayerSignificantShips, next, out _, out _);
    }

    /// <summary>Money rewards (vanilla MoneyReward, not the contract bonus) of the offers that this system gives, as their reward lines show it.</summary>
    static double OfferMoney() => MissionArea.Offers(R().Game.Sim)
        .Where(m => m.SpawnedAt is { } at && at.MapNodeID.Equals(R().Game.CurrentMapNode.UniqueID))
        .SelectMany(m => m.Rewards.OfType<MoneyReward>())
        .Sum(r => (double)r.GetRewardSortValue(Reward.SortValueType.Money, R().Game));

    static void Buy(int units)
    {
        var run = R();
        var game = run.Game;
        var ship = TechAutotest.PlayerShip();
        var station = TechAutotest.NearestStation();
        foreach (var res in GameApp.Rules.Resources.Where(r => r.BuyPrice > 0 && r.ID != Vaults.TechId).Select(r => r.ID))
        {
            var sources = new List<(IResourceSource Source, int AnticipatedResources, float Distance)>();
            station.Resources.SearchAllAvailableResourceSources(ship, res, MPValueType.Displayed, sources);
            var sinks = new List<(IResourceSink Sink, int AnticipatedCapacity, float Distance)>();
            ship.Resources.SearchAllAvailableResourceSinks(station, res, MPValueType.Displayed, sinks);
            if (sources.FirstOrDefault(s => s.AnticipatedResources > 0) is not { Source: { } source } src) continue;
            if (sinks.FirstOrDefault(s => s.AnticipatedCapacity > 0) is not { Sink: { } sink } dst) continue;
            int n = Math.Min(units, Math.Min(src.AnticipatedResources, dst.AnticipatedCapacity));
            int price = game.Sim.Mode.GetResourceBuyCost(res, source.Ship?.Metadata.PlayerIndex);
            _moneyBeforeBuy = Money();
            _expectedBuy = (double)n * price;
            var id = MPMoveResourcesInput.GetJobOwner(source, sink, game.LocalPlayerIndex).Jobs.AllocLocalJobID();
            game.NetManager.EnqueueInput(new MPMoveResourcesInput(source, sink, n, id, sticky: false, TradeType.Buy, price));
            Log.Info($"AUTOTEST factions buy {n} {res} at {price} each (markup {FactionsArea.Markup(run):0.00})");
            return;
        }
        throw new InvalidOperationException("the nearest station sells nothing that the ship can store");
    }
}

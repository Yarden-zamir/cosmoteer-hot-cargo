using System.Globalization;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Tech;

namespace HotCargo.Contracts;

/// <summary>Entry point of the contract economy (spec 100 to 108). Logic is in ContractBook, UI in ContractsUi.</summary>
public static class ContractsArea
{
    public static void Register()
    {
        Run.Hooks.Tick += ContractBook.OnTick;
        RegisterAutotest();
    }

    static Mission? _picked;
    static long _markedValue;
    static int _markedMoney;
    static float _markedHeat;
    static int _bonusGiven;

    static Run R() => Autotest.RequireRun();
    static SimRoot Sim() => R().Game.Sim ?? throw new InvalidOperationException("no sim");
    static Mission Picked() => _picked ?? throw new InvalidOperationException("no contract picked; run contract_pick first");

    /// <summary>Test only: the offer that contract_pick chose, for UI tests and screenshots.</summary>
    internal static Mission? PickedForTests => _picked;

    /// <summary>Test only: the live station ship that offers the picked contract.</summary>
    internal static Cosmoteer.Ships.Ship PickedStationForTests() => StationOf(Picked());

    static Cosmoteer.Ships.Ship StationOf(Mission m) =>
        SpaceStation.GetStations(Sim(), onlyOperating: false).FirstOrDefault(s => s.GetProvidedMissions()?.ContainsMission(m) == true).PhysicalShip
        ?? throw new InvalidOperationException("the picked offer's station is not live; is the offer still there?");

    static void RegisterAutotest()
    {
        // contract_pick <any|paid|free|flatheat|rival|informant|discreet> [metatype]: picks the first station offer that matches,
        // saves its terms, and moves the players next to its station, so that the station ship is live for the accept input.
        Autotest.Commands["contract_pick"] = arg =>
        {
            var args = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string filter = args.Length > 0 ? args[0] : "any";
            string? metatype = args.Length > 1 ? args[1] : null;
            var run = R();
            var sim = Sim();
            foreach (MetaSpaceStation station in SpaceStation.GetStations(sim, onlyOperating: false).OrderBy(s => s.StationID))
            {
                if (station.GetProvidedMissions() is not { } list) continue;
                foreach (Mission m in list)
                {
                    if (metatype is not null && m.MetatypeID.ToString() != metatype) continue;
                    var t = ContractBook.Find(run, m);
                    if (t is null) continue;
                    bool match = filter switch
                    {
                        "any" => true,
                        "paid" => t.Fee > 0,
                        "free" => t.Fee == 0 && t.FlatHeat == 0 && t.RivalGroup.Length == 0,
                        "flatheat" => t.FlatHeat > 0,
                        "rival" => ContractBook.RivalOf(run, sim, m) is not null,
                        "informant" => t.InformantChance > 0,
                        "discreet" => t.InformantChance == 0 && t.RivalGroup.Length == 0,
                        _ => throw new InvalidOperationException($"unknown filter '{filter}'"),
                    };
                    if (!match) continue;
                    _picked = m;
                    // Outside the station hull: a station in stasis has no size yet, so 300 stands in for its radius (as chain_tostation does).
                    float offset = 600f + (station.PhysicalShip?.WorldBoundingRadius ?? 300f);
                    foreach (var ship in sim.Ships.HumanPlayerSignificantShips.ToList())
                    {
                        Autotest.Teleport(ship, station.DetWorldCenter + new Halfling.Geometry.Vector2(offset + ship.WorldBoundingRadius, 0));
                        offset += 2 * ship.WorldBoundingRadius + 100f;
                    }
                    Log.Info($"AUTOTEST picked {m.MetatypeID} {m.MissionID}: fee={t.Fee} bonus={t.Bonus} flatHeat={t.FlatHeat} trail={t.TrailWeight} rival='{t.RivalGroup}' informant={t.InformantChance:0.00}");
                    Autotest.Wait(3);
                    return;
                }
            }
            throw new InvalidOperationException($"no station offer matches '{arg}'");
        };
        // Accepts the picked offer through the game's own accept input, as the comms window does.
        Autotest.Commands["contract_accept"] = _ =>
        {
            var m = Picked();
            var ship = StationOf(m);
            _markedMoney = R().Game.LocalPlayerMoney;
            R().Game.NetManager.EnqueueInput(new CommMissionsTab.MPAcceptMissionInput(CommMissionsTab.MissionSourceType.Ship, ship, m));
            Autotest.Wait(2);
        };
        // contract_terms <fee|flatheat|informant|trail> <value>: changes the saved terms of the picked offer. Test setup only.
        Autotest.Commands["contract_terms"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            float v = float.Parse(parts[1], CultureInfo.InvariantCulture);
            var t = ContractBook.Find(R(), Picked()) ?? throw new InvalidOperationException("picked offer has no terms");
            switch (parts[0])
            {
                case "fee": t.Fee = (int)v; break;
                case "flatheat": t.FlatHeat = v; break;
                case "informant": t.InformantChance = v; break;
                case "trail": t.TrailWeight = v; break;
                default: throw new InvalidOperationException($"unknown term '{parts[0]}'");
            }
        };
        Autotest.Commands["contract_informant_delay"] = arg =>
        {
            float s = float.Parse(arg, CultureInfo.InvariantCulture);
            R().T.Contracts.InformantDelayMin = s;
            R().T.Contracts.InformantDelayMax = s;
        };
        // Adds money to the acting player (the sender of an "mp" command in co-op).
        // contract_set <ContractsTunables field> <value>: changes a live tunable for this run. Test only.
        Autotest.Commands["contract_set"] = arg => Autotest.SetField(R().T.Contracts, arg);
        Autotest.Commands["contract_money"] = arg => R().Game.GetPlayerState(Autotest.ActingPlayerIndex, deterministic: true).AddMoney(int.Parse(arg, CultureInfo.InvariantCulture));
        Autotest.Commands["money_mark"] = _ => _markedMoney = R().Game.LocalPlayerMoney;
        Autotest.Commands["heat_mark"] = _ => _markedHeat = R().State.Heat;
        // Pairs the picked offer with the first other contract offer in this system, as the scan would. Test setup only.
        Autotest.Commands["contract_make_rival"] = _ =>
        {
            var run = R();
            var other = ContractBook.Offers(Sim()).Select(o => o.Mission).FirstOrDefault(o => o != Picked() && ContractBook.Find(run, o) is not null)
                ?? throw new InvalidOperationException("no second contract offer in this system");
            string group = $"test-{Picked().MissionID}";
            ContractBook.Find(run, Picked())!.RivalGroup = group;
            ContractBook.Find(run, other)!.RivalGroup = group;
        };
        Autotest.Commands["value_mark"] = _ => _markedValue = R().T.Tech.Value(R().State);
        // Logs terms statistics of the saved offers in this system.
        Autotest.Commands["contract_stats"] = label =>
        {
            var run = R();
            var offers = ContractBook.Offers(Sim()).Select(o => ContractBook.Find(run, o.Mission)).OfType<ContractTerms>().ToList();
            Log.Info($"AUTOTEST CONTRACTS {label}: offers={offers.Count} paid={offers.Count(t => t.Fee > 0)} informant={offers.Count(t => t.InformantChance > 0)} " +
                     $"rivals={offers.Count(t => t.RivalGroup.Length > 0)} flatHeat={offers.Count(t => t.FlatHeat > 0)} open={ContractBook.OpenWeight(run)}");
        };

        Autotest.Fields["contract_offers"] = () => ContractBook.Offers(Sim()).Count(o => ContractBook.Find(R(), o.Mission) is not null);
        Autotest.Fields["contract_open"] = () => ContractBook.OpenWeight(R());
        Autotest.Fields["contract_trail"] = () => ContractBook.TrailPerMinute(R(), ContractBook.OpenWeight(R()));
        Autotest.Fields["contract_accepted"] = () => R().Mode.Missions.ContainsMission(Picked()) ? 1 : 0;
        Autotest.Fields["contract_fee"] = () => ContractBook.Find(R(), Picked())?.Fee ?? -1;
        Autotest.Fields["heat_change"] = () => R().State.Heat - _markedHeat;
        Autotest.Fields["contract_trail_heat"] = () => ContractBook.TrailHeatAdded;
        // Money change since contract_accept plus the saved fee: 0 when exactly the fee was paid.
        Autotest.Fields["contract_fee_diff"] = () => R().Game.LocalPlayerMoney - _markedMoney + (ContractBook.Find(R(), Picked())?.Fee ?? 0);
        Autotest.Fields["contract_money_change"] = () => R().Game.LocalPlayerMoney - _markedMoney;
        Autotest.Fields["contract_rival_offered"] = () =>
        {
            string g = ContractBook.Find(R(), Picked())?.RivalGroup ?? "";
            return g.Length == 0 ? -1 : ContractBook.Offers(Sim()).Count(o => o.Mission != Picked() && ContractBook.Find(R(), o.Mission)?.RivalGroup == g);
        };
        Autotest.Fields["contract_bonus_payer"] = () => Picked().Rewards.OfType<ContractBonusReward>().Single().PayerIndex;
        Autotest.Fields["contract_bonus_payer_local"] = () => Picked().Rewards.OfType<ContractBonusReward>().Single().PayerIndex == R().Game.LocalPlayerIndex ? 1 : 0;
        Autotest.Fields["contract_bonus_given"] = () => _bonusGiven;
        // Gives the picked contract's bonus now, as completion would, and remembers money first (contract_money_change).
        Autotest.Commands["contract_give_bonus"] = _ =>
        {
            _markedMoney = R().Game.LocalPlayerMoney;
            Picked().Rewards.OfType<ContractBonusReward>().Single().GiveReward(R().Game);
            _bonusGiven++;
        };
        Autotest.Fields["contract_has_bonus"] = () => Picked().Rewards.OfType<ContractBonusReward>().Sum(b => b.Money);
        Autotest.Fields["contract_informants_pending"] = () => R().State.Contracts.Informants.Count;
        Autotest.Fields["contract_soldout"] = () => R().State.Contracts.SoldOut.Contains(Picked().MissionID) ? 1 : 0;
        Autotest.Fields["contract_informant_warnings"] = () => ContractBook.InformantWarnings;
        Autotest.Fields["contract_panel_lines"] = () => ContractsUi.Lines(R(), Picked()).Count;
        Autotest.Fields["notoriety"] = () => R().State.Notoriety;
        Autotest.Fields["peaktier"] = () => R().State.PeakTier;
        Autotest.Fields["techvalue_change"] = () => R().T.Tech.Value(R().State) - _markedValue;
        // Tech value in fractions of the peak tier's ship value (spec 19).
        Autotest.Fields["techvalue_ship_fraction"] = () => R().T.Tech.Value(R().State) / (double)GameData.TierValueMaximum(R().State.PeakTier);
    }
}

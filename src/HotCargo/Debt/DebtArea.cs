using System.Globalization;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Ui;

namespace HotCargo.Debt;

/// <summary>Entry point of the Debt area (spec 1400 to 1439). Mod.Boot calls Register after the Diplomacy area.</summary>
public static class DebtArea
{
    internal static string Text(string key) => Strings.GetText("HotCargoDebt/" + key);

    internal static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoDebt/" + key, args);

    internal static void Chat(Run run, string xml) => Ux.UxKit.ChatLine(run.Game, xml);

    internal static void Center(Run run, string xml) => Ux.UxKit.CenterText(run.Game, xml);

    public static void Register()
    {
        CommsHail.Providers.Add(DebtHailProvider.Instance);
        SharedChoice.SharedChoices.Register(DebtChoiceDomain.Instance);
        Run.Hooks.Tick += DebtTick.OnTick;
        Run.Hooks.SystemEntered += DebtTick.OnSystemEntered;
        var score = Run.Hooks.ScoreAdjust;
        Run.Hooks.ScoreAdjust = (run, s) => DebtModel.Score(score(run, s), run.State.Debt.Total, run.T.Debt.ScoreDebtFactor);
        HeatPanel.ExtraLines.Add(PanelLines);
        RegisterAutotest();
    }

    /// <summary>Spec 1412: one heat panel line for each debt.</summary>
    static string? PanelLines(Run run)
    {
        var s = run.State.Debt;
        if (s.Debts.Count == 0 || run.Game.Sim is not { } sim) return null;
        double now = run.State.GameSeconds;
        return string.Join("\n", s.Debts.Select(d =>
        {
            string name = Lenders.Name(d.Faction);
            var stage = Lenders.StageOf(run, d);
            string next = Lenders.Wants(sim, d.Faction) ? Format("PanelHunted")
                : s.Collectors.Any(c => c.Faction == d.Faction) ? Text("PanelCollectorHere")
                : run.State.Result != RunResult.Running ? ""
                : Format("PanelCollectorFmt", Pursuit.PursuitModel.Clock(Math.Max(0, d.NextCollectorAt - now)));
            string interest = run.State.Result != RunResult.Running ? ""
                : Format("PanelInterestFmt", (run.T.Debt.InterestRate * d.RateFactor * 100f).ToString("0.#"), Pursuit.PursuitModel.Clock(Math.Max(0, d.NextInterestAt - now)));
            string line = Format("PanelFmt", d.Balance.ToString("N0"), name, Lenders.Ratio(d).ToString("0.0"), interest, next);
            return stage switch { DebtStage.Hostile => $"<bad>{line}</bad>", DebtStage.Firm => $"<limited_color>{line}</limited_color>", _ => line };
        }));
    }

    static Run R() => Autotest.RequireRun();

    static SimRoot Sim() => R().Game.Sim;

    static Debt First() => R().State.Debt.Debts.FirstOrDefault() ?? throw new InvalidOperationException("no debt");

    static Ship Ship(Cosmoteer.Game.ObjectID id) => Sim().Ships[id] ?? throw new InvalidOperationException($"no ship {id}");

    /// <summary>"loan": a station that offers a loan. "lender": a station of the first debt's lender. "collector": the first collector.</summary>
    static Ship Target(string kind) => kind switch
    {
        "loan" => Stations().FirstOrDefault(s => Lenders.OffersLoan(R(), s)) ?? throw new InvalidOperationException("no station offers a loan"),
        "lender" => Stations().FirstOrDefault(s => Lenders.DebtAt(R(), s) is not null) ?? throw new InvalidOperationException("no station of the lender"),
        "collector" => Ship((R().State.Debt.Collectors.FirstOrDefault() ?? throw new InvalidOperationException("no collector")).Ship),
        _ => throw new ArgumentException($"debt target '{kind}': loan, lender or collector"),
    };

    /// <summary>Operating stations, nearest to the acting ship first, lowest ID on a tie.</summary>
    static IEnumerable<Ship> Stations()
    {
        var at = Autotest.ActingShip(Sim())?.DetWorldCenter ?? default;
        return Cosmoteer.Ships.Special.SpaceStation.GetStations(Sim(), onlyOperating: true).Select(s => s.PhysicalShip).OfType<Ship>()
            .OrderBy(s => s.DetWorldCenter.DistanceSquaredTo(at)).ThenBy(s => s.UniqueID);
    }

    static long _marked;
    static string _oldFaction = "";
    static int _oldRep;

    static IEnumerable<Cosmoteer.Modes.Career.Missions.Mission> CollectOffers() =>
        Missions.MissionArea.Offers(Sim()).Where(m => m.MetatypeID.Equals(new Cosmoteer.Data.ID<Cosmoteer.Modes.Career.Missions.MissionMetatype>(DebtMissions.CollectMetatype)));

    static Cosmoteer.Modes.Career.Missions.Rewards.MoneyReward CollectMoney() =>
        CollectOffers().SelectMany(m => m.Rewards).OfType<Cosmoteer.Modes.Career.Missions.Rewards.MoneyReward>().FirstOrDefault()
        ?? throw new InvalidOperationException("no collect offer with a money reward");

    static void RegisterAutotest()
    {
        Autotest.Fields["debt_count"] = () => R().State.Debt.Debts.Count;
        Autotest.Fields["debt_balance"] = () => First().Balance;
        Autotest.Fields["debt_principal"] = () => First().Principal;
        Autotest.Fields["debt_ratio"] = () => Lenders.Ratio(First());
        Autotest.Fields["debt_stage"] = () => (int)Lenders.StageOf(R(), First());
        Autotest.Fields["debt_wanted"] = () => Lenders.Wants(Sim(), First().Faction) ? 1 : 0;
        Autotest.Fields["debt_collectors"] = () => R().State.Debt.Collectors.Count;
        // Spec 1407: 1 when the first collector's comms page is the debt page alone (no other area claims its choices).
        Autotest.Fields["debt_collector_page"] = () =>
        {
            var me = Autotest.ActingShip(Sim()) ?? throw new InvalidOperationException("no acting ship");
            var page = CommsHail.Page(me, Target("collector"), null);
            return page?.Shared?.Domain == DebtChoiceDomain.Name && page.Rows.All(r => r.Key == "debtpay") ? 1 : 0;
        };
        Autotest.Fields["debt_hailed"] = () => R().State.Debt.Collectors.Count(c => c.Hailed);
        Autotest.Fields["debt_loan_stations"] = () => Stations().Count(s => Lenders.OffersLoan(R(), s));
        Autotest.Fields["debt_loan_credits"] = () => DebtDeals.LoanCredits(R());
        Autotest.Fields["debt_collector_in"] = () => First().NextCollectorAt - R().State.GameSeconds;
        Autotest.Fields["debt_panel"] = () => HeatPanel.ExtraText.Contains(Lenders.Name(First().Faction), StringComparison.Ordinal) ? 1 : 0;
        // Spec 1411: (money + held units × the Buyer price − the score) ÷ the debt. The debt factor when the score counts the debt.
        Autotest.Fields["debt_score_factor"] = () =>
        {
            var run = R();
            var players = new List<Cosmoteer.Game.PlayerState>();
            run.Game.GetHumanPlayerStates(players, deterministic: true);
            long plain = Journey.Buyer.Score(players.Sum(p => (long)p.Money), Run.Hooks.CountFleetTech(Sim()), Journey.Buyer.UnitPrice(run));
            return (double)(plain - run.State.Score) / run.State.Debt.Total;
        };
        // debt_set <DebtTunables field> <value>: changes a live tunable for this run.
        Autotest.Commands["debt_set"] = arg => Autotest.SetField(R().T.Debt, arg);
        // debt_goto loan|lender|collector: single player. Moves the player ship next to that ship.
        Autotest.Commands["debt_goto"] = arg =>
        {
            var target = Target(arg.Trim());
            var me = Autotest.ActingShip(Sim()) ?? throw new InvalidOperationException("no acting ship");
            Autotest.Teleport(me, Autotest.ClearSpotNear(Sim(), target.DetWorldCenter, target.WorldBoundingRadius + me.WorldBoundingRadius + 60f, me.WorldBoundingRadius));
        };
        // debt_loan: takes the loan of the first station that offers one, as the acting player.
        Autotest.Commands["debt_loan"] = _ => R().Game.NetManager.EnqueueInput(DebtChoiceDomain.Pick(Target("loan"), DebtChoice.Loan, 0));
        // debt_pay lender|collector <credits|half|all>: pays at that ship, as the acting player.
        Autotest.Commands["debt_pay"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int hundreds = p[1] switch
            {
                "all" => DebtModel.Hundreds(First().Balance),
                "half" => DebtModel.Hundreds(First().Balance / 2),
                _ => int.Parse(p[1], CultureInfo.InvariantCulture) / 100,
            };
            R().Game.NetManager.EnqueueInput(DebtChoiceDomain.Pick(Target(p[0]), DebtChoice.Pay, hundreds));
        };
        Autotest.Commands["debt_notnow"] = _ => R().Game.NetManager.EnqueueInput(DebtChoiceDomain.Pick(Target("collector"), DebtChoice.NotNow, 0));
        // debt_age <seconds>: the timers of every debt come that much closer (interest steps and the next collector).
        Autotest.Commands["debt_age"] = arg =>
        {
            double sec = double.Parse(arg, CultureInfo.InvariantCulture);
            foreach (var d in R().State.Debt.Debts) { d.NextInterestAt -= sec; d.NextCollectorAt -= sec; }
        };
        // debt_ratio <x>: sets the balance of the first debt to x × its principal.
        Autotest.Commands["debt_ratio"] = arg => First().Balance = (long)Math.Ceiling(First().Principal * double.Parse(arg, CultureInfo.InvariantCulture));
        // debt_unwant: the lender of the first debt stops wanting the players, as after a lost search.
        Autotest.Commands["debt_unwant"] = _ => ((Cosmoteer.Modes.Career.CareerSimModeManager)Sim().Mode).Wanted.RemoveFactionFromWanting(First().FactionId);
        // Debt missions (spec 1420 to 1422).
        Autotest.Fields["debt_collect_offers"] = () => CollectOffers().Count();
        Autotest.Fields["debt_collect_line"] = () => CollectMoney().GetRewardText(1f, R().Game)?.Contains(DebtArea.Text("PayoffLineTag"), StringComparison.Ordinal) == true ? 1 : 0;
        // The debt that the last debt_mark saw, minus the debt now, ÷ the collect offer's money: 1 when the payout went off the debt.
        Autotest.Fields["debt_drop_share"] = () => (double)(_marked - R().State.Debt.Total) / CollectMoney().GetScaledMoney(R().Game);
        Autotest.Commands["debt_mark"] = _ => _marked = R().State.Debt.Total;
        // debt_collect_pay: gives the money reward of the collect offer, the path that the vanilla payment input calls.
        Autotest.Commands["debt_collect_pay"] = _ => CollectMoney().GiveReward(R().Game);
        Autotest.Fields["debt_refinance_offers"] = () => Stations().Count(st => Lenders.Refinances(R(), st).Count > 0);
        Autotest.Fields["debt_rate"] = () => First().RateFactor;
        Autotest.Fields["debt_rep_drop"] = () => _oldRep - R().Mode.GetFactionReputation(new Cosmoteer.Data.ID<Cosmoteer.Factions.FactionRules>(_oldFaction));
        // debt_rehome: the first debt moves to a faction that is not the nearest station's and not the corporation's ally, so that
        // station can buy it (spec 1421). Notes the reputation of that faction for debt_rep_drop.
        Autotest.Commands["debt_rehome"] = _ =>
        {
            var station = Stations().First();
            string here = Lenders.FactionOf(station)?.ID.ToString() ?? throw new InvalidOperationException("the nearest station has no faction");
            string ally = Factions.FactionsArea.Roles(R()).Ally.ID.ToString();
            var to = Cosmoteer.GameApp.Rules.Factions.Select(f => f.ID.ToString()).Where(id => id != here && id != ally).OrderBy(id => id, StringComparer.Ordinal).First();
            First().Faction = to;
            _oldFaction = to;
            _oldRep = R().Mode.GetFactionReputation(First().FactionId);
            Log.Info($"AUTOTEST debt moved to {to}; station {here}, ally {ally}");
        };
        Autotest.Commands["debt_refinance"] = _ =>
        {
            var station = Stations().FirstOrDefault(st => Lenders.Refinances(R(), st).Count > 0) ?? throw new InvalidOperationException("no station buys a debt");
            R().Game.NetManager.EnqueueInput(DebtChoiceDomain.Pick(station, DebtChoice.Refinance, R().State.Debt.Debts.IndexOf(Lenders.Refinances(R(), station)[0])));
        };
        // debt_open loan|lender|collector: opens the comms window to that ship on its dialogue tab (for screenshots). debt_close closes it.
        Autotest.Commands["debt_open"] = arg =>
        {
            var me = Autotest.ActingShip(Sim()) ?? throw new InvalidOperationException("no acting ship");
            var window = new Cosmoteer.Modes.Career.Comms.CommWindow(R().Game, me, Target(arg.Trim()));
            R().Game.Gui.FloatingWindows.AddChild(window);
            // The window builds its tabs once it is in the GUI, so the dialogue tab exists from the next frame.
            Run.OnNextFrame(() => window._tabs.TabsListBox.SelectedWidget = window._dialogueTab ?? throw new InvalidOperationException("the ship has no dialogue tab"));
        };
        Autotest.Commands["debt_close"] = _ =>
        {
            foreach (var w in R().Game.Gui.FloatingWindows.Children.OfType<Cosmoteer.Modes.Career.Comms.CommWindow>().ToList()) Cosmoteer.Game.Gui.Extensions.Unparent(w);
        };
        // debt_page lender|collector|loan: logs the page text and rows of that ship's comms for the acting ship.
        Autotest.Commands["debt_page"] = arg =>
        {
            var target = Target(arg.Trim());
            var me = Autotest.ActingShip(Sim()) ?? throw new InvalidOperationException("no acting ship");
            var page = CommsHail.Page(me, target, null) ?? throw new InvalidOperationException($"{target.Metadata.PlainShipName} has no page");
            Log.Info($"AUTOTEST debt page: text='{page.PrimaryDialogue}' rows={string.Join("|", page.Rows.Select(r => r.Text))} buttons={page.Buttons.Count}");
        };
    }
}

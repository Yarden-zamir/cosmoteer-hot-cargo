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
                : Format("PanelInterestFmt", (run.T.Debt.InterestRate * 100f).ToString("0.#"), Pursuit.PursuitModel.Clock(Math.Max(0, d.NextInterestAt - now)));
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

    static IEnumerable<Ship> Stations() =>
        Cosmoteer.Ships.Special.SpaceStation.GetStations(Sim(), onlyOperating: true).Select(s => s.PhysicalShip).OfType<Ship>().OrderBy(s => s.UniqueID);

    static void RegisterAutotest()
    {
        Autotest.Fields["debt_count"] = () => R().State.Debt.Debts.Count;
        Autotest.Fields["debt_balance"] = () => First().Balance;
        Autotest.Fields["debt_principal"] = () => First().Principal;
        Autotest.Fields["debt_ratio"] = () => Lenders.Ratio(First());
        Autotest.Fields["debt_stage"] = () => (int)Lenders.StageOf(R(), First());
        Autotest.Fields["debt_wanted"] = () => Lenders.Wants(Sim(), First().Faction) ? 1 : 0;
        Autotest.Fields["debt_collectors"] = () => R().State.Debt.Collectors.Count;
        Autotest.Fields["debt_hailed"] = () => R().State.Debt.Collectors.Count(c => c.Hailed);
        // Why a collector does not hail: its distance to the nearest human ship, and whether that ship can call it and it answers.
        Autotest.Fields["debt_collector_distance"] = () => Target("collector") is var c && SimQueries.NearestHumanShip(Sim(), c.DetWorldCenter) is { } h ? h.DetWorldCenter.DistanceTo(c.DetWorldCenter) : -1;
        Autotest.Fields["debt_collector_callable"] = () => Target("collector") is var c && Sim().Ships.HumanPlayerShips.Any(h => Diplomacy.Situation.Career(Sim()).CanShipCallShip(h, c)) ? 1 : 0;
        Autotest.Fields["debt_comms_radius"] = () => Sim().Ships.HumanPlayerShips.Max(h => h.Sensors.DetWorldCommsCircle.Radius);
        Autotest.Fields["debt_collector_crew"] = () => Target("collector") is var c && c.Commands.CanExecuteCrewCommandsFrom(c.Metadata.PlayerIndex) ? 1 : 0;
        Autotest.Fields["debt_collector_ai"] = () => Target("collector").AI is null ? 0 : 1;
        Autotest.Fields["debt_collector_answers"] = () => Target("collector") is var c && Sim().Ships.HumanPlayerShips.Any(h => Diplomacy.Situation.Career(Sim()).WillShipAnswerCall(h, c)) ? 1 : 0;
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

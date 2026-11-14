using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Tech;
using HotCargo.SharedChoice;
using HotCargo.Ui;

namespace HotCargo.Debt;

/// <summary>
/// Spec 1402 to 1408: the debt part of station and collector comms. A loan offer at a trade station, the pay row at a lender's
/// station and at a collector, and "Not now" at a collector. Every choice is a shared choice of <see cref="DebtChoiceDomain"/>.
/// </summary>
public sealed class DebtHailProvider : IHailProvider
{
    public static readonly DebtHailProvider Instance = new();

    public bool Answers(Ship caller, Ship callee) =>
        Run.Current is { } run && caller.Sim is { } sim && sim == run.Game.Sim && Offers(run, callee);

    internal static bool Offers(Run run, Ship callee) =>
        Lenders.OffersLoan(run, callee) || Lenders.DebtAt(run, callee) is not null || Lenders.Refinances(run, callee).Count > 0;

    /// <summary>Spec 1407: a collector's page is the debt page only.</summary>
    public bool Owns(Ship caller, Ship callee) => Run.Current is { } run && Lenders.CollectorOf(run, callee) is not null;

    /// <summary>
    /// Spec 1416: a station's normal page shows one line that opens the debt page; the debt page shows only the debt part and Back.
    /// A collector's page is always the debt page.
    /// </summary>
    public void Build(Ship caller, Ship callee, HailPage page)
    {
        if (Run.Current is not { } run) return;
        bool collectorPage = Lenders.CollectorOf(run, callee) is not null;
        if (!collectorPage && !CommsHail.IsOwnPageOpen(callee, this))
        {
            bool talk = Lenders.DebtAt(run, callee) is not null || Lenders.Refinances(run, callee).Count > 0;
            page.AddButton(DebtArea.Text(talk ? "DebtLine" : "LoanLine"), () => CommsHail.OpenOwnPage(callee, this));
            return;
        }
        DebtPage(run, callee, page);
        if (!collectorPage) page.AddButton(DebtArea.Text("Back"), () => CommsHail.CloseOwnPage(callee));
    }

    /// <summary>Spec 1402 to 1408 and 1421: the loan offer, the refinance offers, the debt state, the pay row and "Not now".</summary>
    static void DebtPage(Run run, Ship callee, HailPage page)
    {
        // Limit: one shared choice domain per page (CommsHail). A page that another area owns shows no debt choices.
        if (page.Shared is { } other && other.Domain != DebtChoiceDomain.Name) return;
        page.Shared = (DebtChoiceDomain.Name, DebtChoiceDomain.Serial(callee));
        var t = run.T.Debt;
        if (Lenders.OffersLoan(run, callee) && Lenders.FactionOf(callee) is { } f)
        {
            int credits = DebtDeals.LoanCredits(run);
            page.AddText(DebtArea.Format("LoanOfferFmt", f.NameKey.Text, Money(credits), Percent(t.InterestRate), Minutes(t.InterestSeconds),
                Minutes(t.CollectorSeconds), Ratio(t.FirmRatio), Ratio(t.HostileRatio), Ratio(t.ScoreDebtFactor)));
            page.AddChoice(DebtArea.Format("TakeLoanFmt", Money(credits)), DebtModel.Option(DebtChoice.Loan, 0));
        }
        // Spec 1421: one choice for each debt that this station buys. The option amount is the debt's place in the list.
        if (Lenders.FactionOf(callee) is { } buyer)
            foreach (var old in Lenders.Refinances(run, callee))
            {
                string oldName = Lenders.Name(old.Faction);
                page.AddText(DebtArea.Format("RefinanceOfferFmt", buyer.NameKey.Text, oldName, Money(old.Balance),
                    Percent(t.InterestRate * t.RefinanceRateFactor), Minutes(t.InterestSeconds), t.RefinanceReputationLoss));
                page.AddChoice(DebtArea.Format("RefinanceFmt", oldName), DebtModel.Option(DebtChoice.Refinance, run.State.Debt.Debts.IndexOf(old)));
            }
        if (Lenders.DebtAt(run, callee) is not { } d) return;
        var collector = Lenders.CollectorOf(run, callee);
        string name = Lenders.Name(d.Faction);
        var stage = Lenders.StageOf(run, d);
        if (collector is not null)
            page.AddText(DebtArea.Format(stage switch { DebtStage.Hostile => "CollectorHostileFmt", DebtStage.Firm => "CollectorFirmFmt", _ => "CollectorCalmFmt" },
                name, Ratio(t.HostileRatio)));
        page.AddText(DebtArea.Format("BalanceFmt", Money(d.Balance), name, Ratio(Lenders.Ratio(d)), Percent(t.InterestRate * d.RateFactor),
            Pursuit.PursuitModel.Clock(Math.Max(0, d.NextInterestAt - run.State.GameSeconds))));
        long money = run.Game.GetPlayerState(run.Game.LocalPlayerIndex, deterministic: true).Money;
        if (DebtModel.PayRange(d.Balance, money, t.PayStepShare) is { } range)
        {
            long balance = d.Balance;
            var spec = new SliderSpec("debtpay", DebtArea.Text("SliderPay"), range.Min, range.Max, range.Step, range.Max, v => Money(DebtModel.PayCredits(v, balance)));
            int value = CommsHail.SliderValue(callee, spec);
            long pay = DebtModel.PayCredits(value, balance);
            page.AddRow(new HailRow("debtpay", DebtArea.Format("PayFmt", Money(pay), Money(balance - pay)), DebtModel.Option(DebtChoice.Pay, value), spec));
        }
        else page.AddText(DebtArea.Text("NoMoney"));
        if (collector is not null)
            page.AddChoice(DebtArea.Text(stage == DebtStage.Hostile ? "NotNowHostile" : "NotNow"), DebtModel.Option(DebtChoice.NotNow, 0), close: true);
    }

    static string Money(long credits) => Cosmoteer.Extensions.ToXmlCostString((int)Math.Min(int.MaxValue, credits));
    static string Percent(float share) => (share * 100f).ToString("0.#");
    static string Minutes(float seconds) => (seconds / 60f).ToString("0.#");
    static string Ratio(float ratio) => ratio.ToString("0.#");
}

/// <summary>Spec 1413: debt choices as a shared choice domain. The page serial is the station's or collector's ship ID.</summary>
internal sealed class DebtChoiceDomain : ISharedChoiceDomain
{
    public const string Name = "debt";
    public static readonly DebtChoiceDomain Instance = new();
    static readonly int Count = (Enum.GetValues<DebtChoice>().Max(k => (int)k) + 1) * DebtModel.Span;

    string ISharedChoiceDomain.Name => Name;

    public static int Serial(Ship ship) => unchecked((int)(uint)ship.UniqueID);

    internal static Ship? Callee(Run run, int serial)
    {
        if (run.Game.Sim is not { } sim) return null;
        var ship = sim.Ships[new Cosmoteer.Game.ObjectID(unchecked((uint)serial))];
        return ship is not null && ship.ExistsInSim(sim) ? ship : null;
    }

    /// <summary>The chooser's ship that talks with the callee: in comms range, nearest, lowest ID on a tie.</summary>
    internal static Ship? Caller(SimRoot sim, int player, Ship callee) =>
        sim.Ships.HumanPlayerShips
            .Where(s => s.Commands.CanExecuteCrewCommandsFrom(player) && Diplomacy.Situation.Career(sim).CanShipCallShip(s, callee))
            .OrderBy(s => s.DetWorldCenter.DistanceSquaredTo(callee.DetWorldCenter)).ThenBy(s => s.UniqueID)
            .FirstOrDefault();

    public int OptionCount(Run run, int serial)
    {
        if (Callee(run, serial) is not { } callee || callee.Sim is not { } sim || !DebtHailProvider.Offers(run, callee)) return 0;
        var career = Diplomacy.Situation.Career(sim);
        return sim.Ships.HumanPlayerShips.Any(h => career.CanShipCallShip(h, callee)) ? Count : 0;
    }

    public bool Apply(Run run, SimRoot sim, int playerIndex, int serial, int option)
    {
        if (Callee(run, serial) is not { } callee) return false;
        if (Caller(sim, playerIndex, callee) is null) { Log.Info($"debt: player {playerIndex} has no ship in comms range of {callee.Metadata.PlainShipName}"); return false; }
        var (kind, amount) = DebtModel.Decode(option);
        return DebtDeals.Choose(run, sim, playerIndex, callee, kind, amount);
    }

    /// <summary>The input that a test command sends for a pick.</summary>
    public static SharedChoiceInput Pick(Ship callee, DebtChoice kind, int amount) => new(Name, Serial(callee), SharedChoiceAction.Choose, DebtModel.Option(kind, amount));
}

/// <summary>Spec 1403 and 1405: what a debt choice does. Runs in the shared choice apply, on every machine.</summary>
static class DebtDeals
{
    public static int LoanCredits(Run run) => Events.EventRunner.Credits(run, run.T.Debt.LoanUnits, 0f, run.CurrentTier, run.TechValue());

    /// <summary>Takes credits off a debt. A debt at 0 closes with a run log line; its collectors leave in the next tick.</summary>
    public static void Reduce(Run run, Debt d, long credits)
    {
        if (credits < 0 || credits > d.Balance) throw new ArgumentOutOfRangeException(nameof(credits), credits, $"a debt of {d.Balance} cannot lose this");
        d.Balance -= credits;
        d.Stage = Lenders.StageOf(run, d);
        if (d.Balance > 0) return;
        run.State.Debt.Debts.Remove(d);
        Onboarding.RunLog.Add(run, Onboarding.RunLogKind.Debt, DebtArea.Format("LogPaidOffFmt", Lenders.Name(d.Faction)));
    }

    public static bool Choose(Run run, SimRoot sim, int player, Ship callee, DebtChoice kind, int amount)
    {
        var s = run.State.Debt;
        var playerState = run.Game.GetPlayerState(player, deterministic: true);
        switch (kind)
        {
            case DebtChoice.Loan:
            {
                if (!Lenders.OffersLoan(run, callee) || Lenders.FactionOf(callee) is not { } f) return false;
                int credits = LoanCredits(run);
                double now = run.State.GameSeconds;
                s.Debts.Add(new Debt
                {
                    Faction = f.ID.ToString(), Principal = credits, Balance = credits,
                    NextInterestAt = now + run.T.Debt.InterestSeconds, NextCollectorAt = now + run.T.Debt.CollectorSeconds,
                });
                playerState.AddMoney(credits);
                DebtArea.Chat(run, DebtArea.Format("LoanTakenFmt", f.NameKey.Text, Cosmoteer.Extensions.ToXmlCostString(credits)));
                Onboarding.RunLog.Add(run, Onboarding.RunLogKind.Debt, DebtArea.Format("LogLoanFmt", f.NameKey.Text, credits.ToString("N0")));
                Log.Info($"debt: player {player} took a loan of {credits} from {f.ID}");
                return true;
            }
            case DebtChoice.Pay:
            {
                if (Lenders.DebtAt(run, callee) is not { } d) return false;
                long credits = DebtModel.PayCredits(amount, d.Balance);
                if (credits <= 0 || playerState.Money < credits) { Log.Info($"debt: player {player} cannot pay {credits}"); return false; }
                playerState.AddMoney(-(int)credits);
                var collector = Lenders.CollectorOf(run, callee);
                Reduce(run, d, credits);
                DebtArea.Chat(run, DebtArea.Format("PaidFmt", Cosmoteer.Extensions.ToXmlCostString((int)credits), Lenders.Name(d.Faction), Cosmoteer.Extensions.ToXmlCostString((int)Math.Min(int.MaxValue, d.Balance))));
                Log.Info($"debt: player {player} paid {credits} to {d.Faction}, {d.Balance} left");
                if (collector is not null) DebtTick.Leave(run, sim, collector, callee);
                return true;
            }
            case DebtChoice.Refinance:
            {
                if (amount >= s.Debts.Count || Lenders.FactionOf(callee) is not { } buyer || !Lenders.Refinances(run, callee).Contains(s.Debts[amount])) return false;
                var old = s.Debts[amount];
                var t = run.T.Debt;
                double now = run.State.GameSeconds;
                s.Debts.Remove(old);
                s.Debts.Add(new Debt
                {
                    Faction = buyer.ID.ToString(), Principal = old.Balance, Balance = old.Balance, RateFactor = t.RefinanceRateFactor,
                    NextInterestAt = now + t.InterestSeconds, NextCollectorAt = now + t.CollectorSeconds,
                });
                var oldId = old.FactionId;
                run.Mode.SetFactionReputation(oldId, run.Mode.GetFactionReputation(oldId) - t.RefinanceReputationLoss);
                string oldName = Lenders.Name(old.Faction);
                DebtArea.Chat(run, DebtArea.Format("RefinancedFmt", buyer.NameKey.Text, oldName, t.RefinanceReputationLoss));
                Onboarding.RunLog.Add(run, Onboarding.RunLogKind.Debt, DebtArea.Format("LogRefinancedFmt", buyer.NameKey.Text, oldName));
                Log.Info($"debt: {buyer.ID} bought the {old.Faction} debt of {old.Balance}");
                return true;
            }
            case DebtChoice.NotNow:
            {
                if (Lenders.CollectorOf(run, callee) is not { } c) return false;
                DebtTick.Leave(run, sim, c, callee);
                return true;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "no debt choice");
        }
    }
}

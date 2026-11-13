using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI.CommsModules;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Ui;

namespace HotCargo.Diplomacy;

/// <summary>
/// The Hot Cargo options in ship comms (spec 560 to 583): the hunter demand page, surrender talk with enemies and the friendly ship page.
/// <see cref="Answers"/> and <see cref="Allowed"/> read sim state only; <see cref="Build"/> is UI; its choices go through <see cref="DiplomacyChoiceDomain"/>.
/// </summary>
public sealed class DiplomacyHail : IHailProvider
{
    public static readonly DiplomacyHail Instance = new();

    DiplomacyHail() { }

    static DiplomacyState S(Run run) => run.State.Diplomacy;

    static bool Done(Run run, Ship ship, Offer o) => S(run).WasUsed(ship.UniqueID, o);

    /// <summary>Ships that already made peace through a surrender keep the vanilla talk only.</summary>
    static bool Surrendered(SimRoot sim, Ship ship) =>
        Situation.Career(sim).SurrenderedAIs.Contains(ship.UniqueID) || Situation.Career(sim).SurrenderedToAIs.Contains(ship.UniqueID);

    public bool Answers(Ship caller, Ship callee)
    {
        if (Run.Current is not { } run || !run.T.Diplomacy.Enabled || caller.Sim is not { } sim || run.Game.Sim != sim) return false;
        if (callee.AI is null || !callee.IsSignificant || callee.IsJunk || Situation.IsStation(callee) || Surrendered(sim, callee)) return false;
        if (S(run).Leaving.Any(l => l.Ship == callee.UniqueID)) return false;
        if (caller.IsEnemiesWith(callee))
            return Situation.IsHunter(callee) || (callee.AI.GetCommsModule<AIDialogueModule>() is not null
                && (!Done(run, callee, Offer.DemandSurrender) || !Done(run, callee, Offer.OfferSurrender)));
        if (!Situation.IsFriendlyShip(caller, callee)) return false;
        return Situation.VaultTech(sim) > 0 && !Done(run, callee, Offer.Hold) && Situation.MayHold(run, callee)
            || S(run).Held.Any(h => h.Ship == callee.UniqueID)
            || S(run).FeeAsks.Any(f => f.Ship == callee.UniqueID)
            || Situation.Hireable(callee) && (!Done(run, callee, Offer.Hire) || S(run).Mercs.Contains(callee.UniqueID))
            || S(run).Hires.Any(h => h.Ship == callee.UniqueID);
    }

    /// <summary>
    /// The sim check of a choice: the rules of the page at this tick. The dialogue greys a button out with the same check,
    /// and the input runs it again, because the state can change between the click and the input.
    /// </summary>
    public static bool Allowed(Run run, SimRoot sim, int player, Ship caller, Ship callee, Offer offer, int amount, out string why)
    {
        why = "";
        bool enemy = caller.IsEnemiesWith(callee);
        bool hunter = Situation.IsHunter(callee);
        int stock = Situation.VaultTech(sim);
        bool merc = S(run).Mercs.Contains(callee.UniqueID);
        if (!Situation.Career(sim).CanShipCallShip(caller, callee)) { why = "out of comms range"; return false; }
        switch (offer)
        {
            case Offer.PayOff:
                if (!enemy || !hunter) { why = "not a hunter"; return false; }
                return Deals.PayAllowed(run, sim, player, Deals.PayDraft(run, callee), out why);
            case Offer.Bluff:
            {
                if (!enemy || !hunter) { why = "not a hunter"; return false; }
                int units = Quotes.BluffUnits(run, sim);
                if (units == 0) { why = "too little tech"; return false; }
                if (amount != units) { why = $"amount {amount}, not {units}"; return false; }
                return true;
            }
            case Offer.Refuse:
                if (!hunter) { why = "not a hunter"; return false; }
                return true;
            case Offer.DemandSurrender:
                if (!enemy || callee.AI?.GetCommsModule<AIDialogueModule>() is null) { why = "not an enemy that talks"; return false; }
                return true;
            case Offer.AcceptYield:
                if (!enemy || !S(run).Rolled.Contains(DiplomacyState.Key(callee.UniqueID, "yieldok"))) { why = "no surrender offer"; return false; }
                return true;
            case Offer.OfferSurrender:
                if (!enemy || hunter || callee.AI?.GetCommsModule<AIDialogueModule>() is null) { why = "not an enemy that takes a surrender"; return false; }
                if (!Situation.CanPay(run, player, Quotes.OfferPrice(run, sim, player, callee))) { why = "money"; return false; }
                return true;
            case Offer.Hold:
                if (!Situation.IsFriendlyShip(caller, callee)) { why = "not friendly"; return false; }
                if (!Situation.MayHold(run, callee)) { why = "they work with the corporation"; return false; }
                if (amount < 1 || amount > stock) { why = $"amount {amount} of {stock}"; return false; }
                if (S(run).FeeAsks.Any(f => f.Ship == callee.UniqueID)) { why = "a fee question waits"; return false; }
                return true;
            case Offer.PayHoldFee:
                if (S(run).FeeAsks.FirstOrDefault(f => f.Ship == callee.UniqueID) is not { } ask) { why = "no fee asked"; return false; }
                if (stock < 1) { why = "no tech"; return false; }
                if (!Situation.CanPay(run, player, ask.Fee)) { why = "money"; return false; }
                return true;
            case Offer.DeclineHoldFee:
                if (!S(run).FeeAsks.Any(f => f.Ship == callee.UniqueID)) { why = "no fee asked"; return false; }
                return true;
            case Offer.Return:
                if (!S(run).Held.Any(h => h.Ship == callee.UniqueID && h.Player == player)) { why = "holds no tech of this player"; return false; }
                return true;
            case Offer.Hire:
                if (!Situation.IsFriendlyShip(caller, callee) || !Situation.Hireable(callee) || merc) { why = "not a friendly armed ship"; return false; }
                if (S(run).Hires.Any(h => h.Ship == callee.UniqueID)) { why = "already hired"; return false; }
                if (Situation.WantedAnywhere(sim, player)) { why = "wanted"; return false; }
                if (amount < 0 || !Situation.CanPay(run, player, amount * Quotes.HireStep(run))) { why = "money"; return false; }
                return true;
            case Offer.Merc:
                if (!Situation.IsFriendlyShip(caller, callee) || !merc) { why = "not a mercenary"; return false; }
                if (amount < 1 || amount > run.T.Diplomacy.MercMaxMinutes) { why = $"minutes {amount}"; return false; }
                if (!Situation.CanPay(run, player, Quotes.MercPrice(run, amount))) { why = "money"; return false; }
                if (S(run).Hires.FirstOrDefault(h => h.Ship == callee.UniqueID) is { } hire && hire.Player != player) { why = "hired by another player"; return false; }
                return true;
            default:
                why = "unknown offer";
                return false;
        }
    }

    public void Build(Ship caller, Ship callee, HailPage page)
    {
        var run = Run.Current ?? throw new InvalidOperationException("no run");
        var sim = caller.Sim ?? throw new InvalidOperationException("caller has no sim");
        int player = run.Game.LocalPlayerIndex;
        // One shared choice domain per page (CommsHail limit): a page that another provider owns (an event page) shows no diplomacy options.
        if (page.Shared is { } owner && owner.Domain != DiplomacyChoiceDomain.Name) return;
        page.Shared = (DiplomacyChoiceDomain.Name, DiplomacyChoiceDomain.Serial(callee));
        if (S(run).Results.FirstOrDefault(r => r.Ship == callee.UniqueID) is { } result) page.AddText(result.Text);
        if (caller.IsEnemiesWith(callee))
        {
            if (Situation.IsHunter(callee)) HunterPage(run, sim, player, caller, callee, page);
            else EnemyPage(run, sim, player, caller, callee, page);
        }
        else FriendPage(run, sim, player, caller, callee, page);
    }

    void Button(Run run, SimRoot sim, int player, Ship caller, Ship callee, HailPage page, Offer offer, int amount, string xml, bool close = false)
    {
        if (S(run).WasUsed(callee.UniqueID, offer)) return;
        bool ok = Allowed(run, sim, player, caller, callee, offer, amount, out string why);
        page.AddChoice(xml, DiplomacyChoiceDomain.Option(offer, amount), ok, ok ? null : DipText.Format("WhyNotFmt", why), close);
    }

    /// <summary>An option row of a shared choice with its own slider (spec 591). The text is built for the slider value.</summary>
    void Row(Run run, SimRoot sim, int player, Ship caller, Ship callee, HailPage page, Offer offer, SliderSpec spec, Func<int, string> text)
    {
        if (S(run).WasUsed(callee.UniqueID, offer)) return;
        int value = CommsHail.SliderValue(callee, spec);
        bool ok = Allowed(run, sim, player, caller, callee, offer, value, out string why);
        page.AddRow(new HailRow(offer.ToString(), text(value), DiplomacyChoiceDomain.Option(offer, value), spec, ok, ok ? null : DipText.Format("WhyNotFmt", why)));
    }

    static string Pct(float chance) => DipText.Format("ChanceFmt", (int)MathF.Round(chance * 100f));

    static string Cost(Run run, int credits) => Cosmoteer.Extensions.ToXmlCostString(credits, run.Game);

    static int Percent(float chance) => (int)MathF.Round(chance * 100f);

    /// <summary>
    /// Spec 562: the demand page. Three choices: pay them off (multiselect rows and one button, spec 594), bluff (spec 565), fight.
    /// The general enemy talk (demand surrender, accept their surrender) stays.
    /// </summary>
    void HunterPage(Run run, SimRoot sim, int player, Ship caller, Ship hunter, HailPage page)
    {
        var t = run.T.Diplomacy;
        int stock = Situation.VaultTech(sim);
        string name = hunter.Metadata.XmlShipName;
        int drop = (int)MathF.Round(t.HandOverHeatDrop);
        bool talked = Done(run, hunter, Offer.Refuse);
        if (!talked)
        {
            page.AddText(stock > 0 ? DipText.Format("DemandFmt", name, stock) : DipText.Format("DemandEmptyFmt", name));
            PayRows(run, sim, hunter, page);
            float pay = Quotes.PayOff(run, sim, hunter, Deals.PayDraft(run, hunter));
            Button(run, sim, player, caller, hunter, page, Offer.PayOff, 0, DipText.Format("PayOffFmt", Percent(pay), drop));
            int units = Quotes.BluffUnits(run, sim);
            if (units > 0)
                Button(run, sim, player, caller, hunter, page, Offer.Bluff, units,
                    DipText.Format("BluffFmt", units, Percent(Quotes.Bluff(run, sim, player)), drop, (int)MathF.Round(t.BluffCaughtHeat)));
        }
        if (S(run).Rolled.Contains(DiplomacyState.Key(hunter.UniqueID, "yieldok")))
        {
            page.AddText(DipText.Format("YieldFmt", name));
            int payout = Quotes.SurrenderPayout(run, sim, hunter);
            Button(run, sim, player, caller, hunter, page, Offer.AcceptYield, 0,
                payout > 0 ? DipText.Format("AcceptYieldFmt", Events.EventText.Money(payout)) : DipText.Text("AcceptYieldFuel"));
        }
        Button(run, sim, player, caller, hunter, page, Offer.DemandSurrender, 0, DipText.Format("DemandSurrenderFmt", Pct(Quotes.DemandSurrender(run, sim, player, hunter))));
        if (!talked) Button(run, sim, player, caller, hunter, page, Offer.Refuse, 0, DipText.Text("Fight"), close: true);
    }

    /// <summary>
    /// Spec 594: the pay-off rows of the shared draft. Each row shows what it adds to the chance (after the crowd factor); the sliders set
    /// the amounts. The pay-off button sends the whole draft.
    /// </summary>
    static void PayRows(Run run, SimRoot sim, Ship hunter, HailPage page)
    {
        int serial = DiplomacyChoiceDomain.Serial(hunter);
        page.Multi = (DiplomacyMultiDomain.Name, serial);
        int others = Situation.OtherHunters(run, sim, hunter);
        float crowd = Quotes.PayCrowd(run, sim, hunter);
        page.AddText(DipText.Text("PayHint") + (others == 0 ? "" : " " + DipText.Format(crowd > 0 ? "PayCrowdFmt" : "PayCrowdNoneFmt", others)));
        var picks = Deals.PayDraft(run, hunter);
        foreach (var option in Quotes.PayOptions(run, sim))
        {
            var kind = (PayOption)option.Id;
            var pick = picks.FirstOrDefault(p => p.Option == option.Id);
            int start = kind switch { PayOption.Tech => Math.Max(1, option.Max / 2), PayOption.Money => Quotes.PayMoneySteps / 2, _ => option.Min };
            var spec = new SliderSpec("pay" + kind, "", option.Min, option.Max, option.Step, Math.Clamp(start, option.Min, option.Max),
                v => Quotes.PayValueText(run, sim, kind, v));
            int value = pick?.Value ?? CommsHail.SliderValue(hunter, spec);
            string text = DipText.Format("Pay" + kind + "Fmt", DipText.Format("PartFmt", Percent(crowd * Quotes.PayPart(run, sim, kind, value))));
            page.AddRow(new HailRow("pay" + kind, text, option.Id, spec, Active: true, Toggle: true, Selected: pick is not null, Value: value));
        }
    }

    void EnemyPage(Run run, SimRoot sim, int player, Ship caller, Ship ai, HailPage page)
    {
        if (page.PrimaryDialogue.Length == 0) page.AddText(DipText.Format("EnemyFmt", ai.Metadata.XmlShipName));
        Button(run, sim, player, caller, ai, page, Offer.DemandSurrender, 0, DipText.Format("DemandSurrenderFmt", Pct(Quotes.DemandSurrender(run, sim, player, ai))));
        Button(run, sim, player, caller, ai, page, Offer.OfferSurrender, 0,
            DipText.Format("OfferSurrenderFmt", Cost(run, Quotes.OfferPrice(run, sim, player, ai)), Pct(Quotes.OfferSurrender(run, sim, player, ai))));
    }

    void FriendPage(Run run, SimRoot sim, int player, Ship caller, Ship ship, HailPage page)
    {
        var s = S(run);
        string name = ship.Metadata.XmlShipName;
        bool merc = s.Mercs.Contains(ship.UniqueID);
        var hire = s.Hires.FirstOrDefault(h => h.Ship == ship.UniqueID);
        int held = s.Held.Where(h => h.Ship == ship.UniqueID).Sum(h => h.Units);
        page.AddText(DipText.Format(merc ? "MercHelloFmt" : "FriendHelloFmt", name));
        if (held > 0) page.AddText(DipText.Format("HoldsFmt", held));
        if (hire is not null) page.AddText(DipText.Format("HiredFmt", (int)Math.Ceiling((hire.Until - run.State.GameSeconds) / 60.0)));
        if (s.FeeAsks.FirstOrDefault(f => f.Ship == ship.UniqueID) is { } ask)
        {
            Button(run, sim, player, caller, ship, page, Offer.PayHoldFee, 0, DipText.Format("PayFeeFmt", Cost(run, ask.Fee), ask.Units));
            Button(run, sim, player, caller, ship, page, Offer.DeclineHoldFee, 0, DipText.Text("DeclineFee"));
            return;
        }
        int stock = Situation.VaultTech(sim);
        if (stock > 0 && Situation.MayHold(run, ship))
        {
            var spec = new SliderSpec("hold", DipText.Text("SliderUnits"), 1, stock, 1, 1, v => DipText.Format("UnitsFmt", v, stock));
            Row(run, sim, player, caller, ship, page, Offer.Hold, spec, k => DipText.Format("HoldFmt", k, Pct(Quotes.Hold(run, sim, ship, k))));
        }
        if (held > 0) Button(run, sim, player, caller, ship, page, Offer.Return, 0, DipText.Format("ReturnAskFmt", held));
        if (merc)
        {
            int max = Math.Max(1, run.T.Diplomacy.MercMaxMinutes);
            var spec = new SliderSpec("merc", DipText.Text("SliderMinutes"), 1, max, 1, Math.Min(3, max), v => DipText.Format("MinutesFmt", v));
            Row(run, sim, player, caller, ship, page, Offer.Merc, spec,
                m => DipText.Format(hire is null ? "MercFmt" : "MercMoreFmt", m, Cost(run, Quotes.MercPrice(run, m))));
        }
        else if (Situation.Hireable(ship) && hire is null)
        {
            int step = Quotes.HireStep(run);
            var spec = new SliderSpec("hire", DipText.Text("SliderOffer"), 0, 20, 1, 10, v => Cosmoteer.Extensions.ToXmlCostString(v * step));
            if (Situation.WantedAnywhere(sim, player) && !Done(run, ship, Offer.Hire)) page.AddText(DipText.Text("HireWanted"));
            Row(run, sim, player, caller, ship, page, Offer.Hire, spec, steps =>
                DipText.Format("HireFmt", Cost(run, steps * step), (int)Math.Ceiling(run.T.Diplomacy.HireSeconds / 60.0), Pct(Quotes.Hire(run, sim, player, ship, steps * step))));
        }
    }
}

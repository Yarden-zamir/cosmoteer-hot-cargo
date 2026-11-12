using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI.CommsModules;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Ui;

namespace HotCargo.Diplomacy;

/// <summary>
/// The Hot Cargo options in ship comms (spec 560 to 585): the hunter demand page, surrender talk with enemies and the friendly ship page.
/// <see cref="Answers"/> and <see cref="Allowed"/> read sim state only; <see cref="Build"/> is UI and sends <see cref="DiplomacyInput"/>.
/// </summary>
public sealed class DiplomacyHail : IHailProvider
{
    /// <summary>Which friendly sub-page each callee shows. Local UI state.</summary>
    static readonly Dictionary<ObjectID, string> Topics = new();

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
        return Situation.VaultTech(sim) > 0 && !Done(run, callee, Offer.Hold)
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
            case Offer.HandOver or Offer.Deal or Offer.Bluff:
                if (!enemy || !hunter) { why = "not a hunter"; return false; }
                if (amount < 1 || amount > stock || (offer == Offer.Bluff && amount >= stock)) { why = $"amount {amount} of {stock}"; return false; }
                if (offer == Offer.Deal && !Situation.CanPay(run, player, Quotes.DealPrice(run, amount))) { why = "money"; return false; }
                return true;
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
                if (amount < 0 || !Situation.CanPay(run, player, amount)) { why = "money"; return false; }
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
        page.AddButton(xml, () => run.Game.NetManager.EnqueueInput(new DiplomacyInput(caller, callee, offer, amount)), ok,
            ok ? null : DipText.Format("WhyNotFmt", why), close);
    }

    static string Pct(float chance) => DipText.Format("ChanceFmt", (int)MathF.Round(chance * 100f));

    void HunterPage(Run run, SimRoot sim, int player, Ship caller, Ship hunter, HailPage page)
    {
        int stock = Situation.VaultTech(sim);
        string name = hunter.Metadata.XmlShipName;
        bool talked = Done(run, hunter, Offer.Refuse);
        if (!talked)
        {
            page.AddText(stock > 0 ? DipText.Format("DemandFmt", name, stock) : DipText.Format("DemandEmptyFmt", name));
            if (stock > 0)
            {
                var spec = new SliderSpec("units", DipText.Text("SliderUnits"), 1, stock, 1, Math.Max(1, stock / 2), v => DipText.Format("UnitsFmt", v, stock));
                page.Slider = spec;
                int k = CommsHail.SliderValue(hunter, spec);
                Button(run, sim, player, caller, hunter, page, Offer.HandOver, k, k >= stock
                    ? DipText.Format("HandOverAllFmt", stock)
                    : DipText.Format("HandOverFmt", k, Pct(Quotes.HandOver(run, sim, player, hunter, k))));
                if (k < stock) Button(run, sim, player, caller, hunter, page, Offer.Bluff, k, DipText.Format("BluffFmt", k, Pct(Quotes.Bluff(run, sim, player, k))));
                Button(run, sim, player, caller, hunter, page, Offer.Deal, k,
                    DipText.Format("DealFmt", Events.EventText.Money(-Quotes.DealPrice(run, k)), k, Pct(Quotes.Deal(run, sim, hunter, k))));
            }
        }
        if (S(run).Rolled.Contains(DiplomacyState.Key(hunter.UniqueID, "yieldok")))
        {
            page.AddText(DipText.Format("YieldFmt", name));
            Button(run, sim, player, caller, hunter, page, Offer.AcceptYield, 0, DipText.Format("AcceptYieldFmt", Events.EventText.Money(Quotes.SurrenderPayout(run, sim, hunter))));
        }
        Button(run, sim, player, caller, hunter, page, Offer.DemandSurrender, 0, DipText.Format("DemandSurrenderFmt", Pct(Quotes.DemandSurrender(run, sim, player, hunter))));
        if (!talked) Button(run, sim, player, caller, hunter, page, Offer.Refuse, 0, DipText.Text("Refuse"), close: true);
    }

    void EnemyPage(Run run, SimRoot sim, int player, Ship caller, Ship ai, HailPage page)
    {
        if (page.PrimaryDialogue.Length == 0) page.AddText(DipText.Format("EnemyFmt", ai.Metadata.XmlShipName));
        Button(run, sim, player, caller, ai, page, Offer.DemandSurrender, 0, DipText.Format("DemandSurrenderFmt", Pct(Quotes.DemandSurrender(run, sim, player, ai))));
        Button(run, sim, player, caller, ai, page, Offer.OfferSurrender, 0,
            DipText.Format("OfferSurrenderFmt", Events.EventText.Money(-Quotes.OfferPrice(run, sim, player, ai)), Pct(Quotes.OfferSurrender(run, sim, player, ai))));
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
            Button(run, sim, player, caller, ship, page, Offer.PayHoldFee, 0, DipText.Format("PayFeeFmt", Events.EventText.Money(-ask.Fee), ask.Units));
            Button(run, sim, player, caller, ship, page, Offer.DeclineHoldFee, 0, DipText.Text("DeclineFee"));
            return;
        }
        string topic = Topics.GetValueOrDefault(ship.UniqueID, "");
        int stock = Situation.VaultTech(sim);
        switch (topic)
        {
            case "hold" when stock > 0 && !Done(run, ship, Offer.Hold):
            {
                var spec = new SliderSpec("hold", DipText.Text("SliderUnits"), 1, stock, 1, 1, v => DipText.Format("UnitsFmt", v, stock));
                page.Slider = spec;
                int k = CommsHail.SliderValue(ship, spec);
                Button(run, sim, player, caller, ship, page, Offer.Hold, k, DipText.Format("HoldFmt", k, Pct(Quotes.Hold(run, sim, ship, k))));
                break;
            }
            case "hire" when !merc && hire is null && !Done(run, ship, Offer.Hire):
            {
                int fair = Quotes.HireFair(run);
                int step = Math.Max(1, fair / 10);
                var spec = new SliderSpec("hire", DipText.Text("SliderOffer"), 0, step * 20, step, fair, v => Events.EventText.Money(v));
                page.Slider = spec;
                int offer = CommsHail.SliderValue(ship, spec);
                if (Situation.WantedAnywhere(sim, player)) page.AddText(DipText.Text("HireWanted"));
                Button(run, sim, player, caller, ship, page, Offer.Hire, offer,
                    DipText.Format("HireFmt", Events.EventText.Money(-offer), (int)Math.Ceiling(run.T.Diplomacy.HireSeconds / 60.0), Pct(Quotes.Hire(run, sim, player, ship, offer))));
                break;
            }
            case "merc" when merc:
            {
                int max = Math.Max(1, run.T.Diplomacy.MercMaxMinutes);
                var spec = new SliderSpec("merc", DipText.Text("SliderMinutes"), 1, max, 1, Math.Min(3, max), v => DipText.Format("MinutesFmt", v));
                page.Slider = spec;
                int m = CommsHail.SliderValue(ship, spec);
                Button(run, sim, player, caller, ship, page, Offer.Merc, m, DipText.Format(hire is null ? "MercFmt" : "MercMoreFmt", m, Events.EventText.Money(-Quotes.MercPrice(run, m))));
                break;
            }
            default:
                if (stock > 0 && !Done(run, ship, Offer.Hold)) page.AddButton(DipText.Text("TopicHold"), () => Topics[ship.UniqueID] = "hold");
                if (held > 0) Button(run, sim, player, caller, ship, page, Offer.Return, 0, DipText.Format("ReturnAskFmt", held));
                if (merc) page.AddButton(DipText.Text("TopicMerc"), () => Topics[ship.UniqueID] = "merc");
                else if (Situation.Hireable(ship) && hire is null && !Done(run, ship, Offer.Hire)) page.AddButton(DipText.Text("TopicHire"), () => Topics[ship.UniqueID] = "hire");
                return;
        }
        page.AddButton(DipText.Text("Back"), () => Topics.Remove(ship.UniqueID));
    }

    /// <summary>Opens a friendly sub-page as the topic button does. For autotests and screenshots.</summary>
    internal static void SetTopic(Ship ship, string topic) => Topics[ship.UniqueID] = topic;
}

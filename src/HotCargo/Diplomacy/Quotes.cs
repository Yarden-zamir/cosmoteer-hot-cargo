using Cosmoteer.Ships;
using Cosmoteer.Ships.AI.CommsModules;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Events;
using HotCargo.Hunters;
using HotCargo.Tech;

namespace HotCargo.Diplomacy;

/// <summary>
/// The chance and price of each option at this tick. The dialogue shows them, and <see cref="Deals"/> decides with the same numbers.
/// Read only.
/// </summary>
public static class Quotes
{
    static DiplomacyTunables T(Run run) => run.T.Diplomacy;

    public static float HandOver(Run run, SimRoot sim, int player, Ship hunter, int units)
    {
        var t = T(run);
        int stock = Situation.VaultTech(sim);
        return DiplomacyModel.HandOverChance(units, stock, Situation.Concentration(run, sim, player, units), Situation.Leverage(run, sim, hunter),
            t.HandOverChanceMin, t.HandOverChanceMax, t.HandOverVisiblePenalty, t.HandOverPerLeverage);
    }

    public static float Bluff(Run run, SimRoot sim, int player, int units)
    {
        var t = T(run);
        return DiplomacyModel.BluffChance(units, Situation.VaultTech(sim), Situation.Concentration(run, sim, player, units), t.BluffChanceBase, t.BluffPerShare, t.BluffVisiblePenalty);
    }

    /// <summary>Most other hunters in the area for a deal: the tunable × co-op enemy scaling.</summary>
    public static int DealMaxOthers(Run run, SimRoot sim) => Scaling.EnemyScale.Cap(T(run).DealMaxOtherHunters, sim);

    public static float Deal(Run run, SimRoot sim, Ship hunter, int units)
    {
        var t = T(run);
        return DiplomacyModel.DealChance(Situation.OtherHunters(run, sim, hunter), DealMaxOthers(run, sim), units, Situation.VaultTech(sim), t.DealChanceAlone, t.DealShareMin);
    }

    public static int DealPrice(Run run, int units) => (int)Math.Round(units * run.T.Tech.Value(run.State) * T(run).DealPriceFactor);

    public static float DemandSurrender(Run run, SimRoot sim, int player, Ship ai)
    {
        var t = T(run);
        var inputs = new DiplomacyModel.SurrenderInputs(Situation.Leverage(run, sim, ai), 1f - HunterOrders.HealthFraction(ai), Situation.Fame(run),
            Situation.Reputation(run, ai), Situation.Allies(run, ai), Situation.Wanted(sim, player, ai), Situation.IsHunter(ai), run.State.Heat);
        return DiplomacyModel.SurrenderChance(inputs, t.SurrenderChanceBase, t.SurrenderPerLeverage, t.SurrenderPerDamage, t.SurrenderPerFame,
            t.SurrenderPerReputation, t.SurrenderPerAlly, t.SurrenderWantedPenalty, t.HunterSurrenderPerHeat);
    }

    /// <summary>What a surrendering ship pays: the vanilla payout (ship value × AISurrenderPayoutMultiple) × the scale, more with leverage.</summary>
    public static int SurrenderPayout(Run run, SimRoot sim, Ship ai)
    {
        var module = ai.AI?.GetCommsModule<AIDialogueModule>() ?? throw new InvalidOperationException($"{ai.Metadata.PlainShipName} has no dialogue module");
        var t = T(run);
        float lev = Math.Max(0f, Situation.Leverage(run, sim, ai));
        float pay = ai.BasePhysicalValue * module.Rules.AISurrenderPayoutMultiple * t.SurrenderPayoutScale * (1f + t.SurrenderPayoutPerLeverage * lev);
        int round = Math.Max(1, Situation.Career(sim).Rules.Dialogue.SurrenderPayoutRoundToMultiple);
        return Math.Max(0, (int)Math.Round(pay / round) * round);
    }

    public static float OfferSurrender(Run run, SimRoot sim, int player, Ship ai)
    {
        var t = T(run);
        return DiplomacyModel.OfferChance(Situation.Leverage(run, sim, ai), Situation.Reputation(run, ai), Situation.Wanted(sim, player, ai),
            t.OfferChanceBase, t.OfferPerLeverage, t.OfferPerReputation, t.OfferWantedPenalty);
    }

    public static int OfferPrice(Run run, SimRoot sim, int player, Ship ai)
    {
        var t = T(run);
        float units = t.OfferPriceUnits * (Situation.Wanted(sim, player, ai) ? t.OfferWantedPriceFactor : 1f);
        return Money(run, units);
    }

    /// <summary>Credits of event money units at the sector tier (spec 73).</summary>
    public static int Money(Run run, float units) => EventRunner.Credits(run, units, 0f, run.CurrentTier, run.T.Tech.Value(run.State));

    /// <summary>A hunter within HoldHunterRadius of the friendly ship blocks every hold.</summary>
    public static bool HunterNear(Run run, SimRoot sim, Ship ship) =>
        HunterSpawner.Hunters(sim).Any(h => h.DetWorldCenter.DistanceTo(ship.DetWorldCenter) <= T(run).HoldHunterRadius);

    public static float Hold(Run run, SimRoot sim, Ship ship, int units)
    {
        var t = T(run);
        if (HunterNear(run, sim, ship)) return 0f;
        return DiplomacyModel.HoldChance(Situation.Reputation(run, ship), run.State.Heat, HunterSpawner.Hunters(sim).Any(), units,
            t.HoldChanceBase, t.HoldPerReputation, t.HoldPerHeat, t.HoldHuntersPenalty, t.HoldPerUnit);
    }

    public static float Fee(Run run, Ship ship)
    {
        var t = T(run);
        return DiplomacyModel.FeeChance(run.State.Heat, Situation.Reputation(run, ship), t.FeeChanceBase, t.FeePerHeat, t.FeePerReputation);
    }

    public static int FeePrice(Run run, int units) => (int)Math.Round(units * run.T.Tech.Value(run.State) * T(run).FeePerUnit);

    public static float Betray(Run run, SimRoot sim, Ship ship, HeldTech held)
    {
        var t = T(run);
        return DiplomacyModel.BetrayChance(run.State.Heat, HunterSpawner.Hunters(sim).Count(), Situation.Reputation(run, ship), held.FeePaid,
            t.BetrayChanceBase, t.BetrayPerHeat, t.BetrayPerHunter, t.BetrayPerReputation, t.BetrayFeePaid);
    }

    public static int HireFair(Run run) => Math.Max(1, Money(run, T(run).HireFairUnits));

    /// <summary>Credits per step of the help offer slider: a tenth of the fair price. The slider goes from 0 to 20 steps.</summary>
    public static int HireStep(Run run) => Math.Max(1, HireFair(run) / 10);

    public static float Hire(Run run, SimRoot sim, int player, Ship ship, int offer)
    {
        var t = T(run);
        return DiplomacyModel.HireChance(offer, HireFair(run), Situation.Reputation(run, ship), run.State.Heat, Situation.WantedAnywhere(sim, player),
            t.HireChanceBase, t.HirePerOffer, t.HirePerReputation, t.HirePerHeat);
    }

    public static int MercPrice(Run run, int minutes) => Money(run, T(run).MercRateUnits * minutes);
}

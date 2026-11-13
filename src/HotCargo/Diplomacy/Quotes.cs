using Cosmoteer.Ships;
using Cosmoteer.Ships.AI.CommsModules;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Events;
using HotCargo.Hunters;
using HotCargo.Tech;
using HotCargo.MultiChoice;

namespace HotCargo.Diplomacy;

/// <summary>
/// The chance and price of each option at this tick. The dialogue shows them, and <see cref="Deals"/> decides with the same numbers.
/// Read only.
/// </summary>
public static class Quotes
{
    static DiplomacyTunables T(Run run) => run.T.Diplomacy;

    /// <summary>Units that the bluff hands over now: BluffShare of the vault tech, at least 1, never all. 0 when the stock is too small.</summary>
    public static int BluffUnits(Run run, SimRoot sim) => DiplomacyModel.BluffUnits(Situation.VaultTech(sim), T(run).BluffShare);

    /// <summary>Spec 565. 0 when the stock is too small to bluff.</summary>
    public static float Bluff(Run run, SimRoot sim, int player)
    {
        var t = T(run);
        int units = BluffUnits(run, sim);
        if (units == 0) return 0f;
        return DiplomacyModel.BluffChance(units, Situation.VaultTech(sim), Situation.Concentration(run, sim, player, units), t.BluffChanceBase, t.BluffPerShare, t.BluffVisiblePenalty);
    }

    public static float DemandSurrender(Run run, SimRoot sim, int player, Ship ai)
    {
        var t = T(run);
        var inputs = new DiplomacyModel.SurrenderInputs(Situation.Leverage(run, sim, ai), 1f - HunterOrders.HealthFraction(ai), Situation.Fame(run),
            Situation.Reputation(run, ai), Situation.Allies(run, ai), Situation.Wanted(sim, player, ai), Situation.IsHunter(ai), run.State.Heat);
        return DiplomacyModel.SurrenderChance(inputs, t.SurrenderChanceBase, t.SurrenderPerLeverage, t.SurrenderPerDamage, t.SurrenderPerFame,
            t.SurrenderPerReputation, t.SurrenderPerAlly, t.SurrenderWantedPenalty, t.HunterSurrenderPerHeat);
    }

    /// <summary>What a surrendering ship pays: the vanilla payout (ship value × AISurrenderPayoutMultiple) × the scale, more with leverage. A hunter pays HunterSurrenderMoneyShare of it.</summary>
    public static int SurrenderPayout(Run run, SimRoot sim, Ship ai)
    {
        var module = ai.AI?.GetCommsModule<AIDialogueModule>() ?? throw new InvalidOperationException($"{ai.Metadata.PlainShipName} has no dialogue module");
        var t = T(run);
        float lev = Math.Max(0f, Situation.Leverage(run, sim, ai));
        float pay = ai.BasePhysicalValue * module.Rules.AISurrenderPayoutMultiple * t.SurrenderPayoutScale * (1f + t.SurrenderPayoutPerLeverage * lev);
        if (Situation.IsHunter(ai)) pay *= t.HunterSurrenderMoneyShare;
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
    public static int Money(Run run, float units) => EventRunner.Credits(run, units, 0f, run.CurrentTier, run.TechValue());

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

    public static int FeePrice(Run run, int units) => (int)Math.Round(units * run.TechValue() * T(run).FeePerUnit);

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

    // ---- Pay them off (spec 594 to 597)

    /// <summary>Steps of the money slider: 1 to this, each step a twentieth of the most money.</summary>
    public const int PayMoneySteps = 20;

    /// <summary>Crew the players can send to cryo jail now: every human ship keeps one, at most PayCrewMax.</summary>
    public static int PayCrewAvailable(Run run, SimRoot sim)
    {
        int crew = run.Game.HumanPlayersIndexes.Sum(sim.Crew.CountCrewForPlayer);
        int ships = Vaults.HumanShips(sim).Count();
        return Math.Clamp(crew - ships, 0, Math.Max(0, T(run).PayCrewMax));
    }

    /// <summary>
    /// The rows of the pay-off offer. Tech stops at all but one unit, so a pay-off never ends the run (spec 564 is removed).
    /// A row shows only when it can offer something.
    /// </summary>
    public static IReadOnlyList<MultiOption> PayOptions(Run run, SimRoot sim)
    {
        var options = new List<MultiOption>();
        int stock = Situation.VaultTech(sim);
        if (stock >= 2) options.Add(new((int)PayOption.Tech, 1, stock - 1, 1));
        options.Add(new((int)PayOption.Money, 1, PayMoneySteps, 1));
        int crew = PayCrewAvailable(run, sim);
        if (crew >= 1) options.Add(new((int)PayOption.Crew, 1, crew, 1));
        return options;
    }

    /// <summary>Credits that a row costs at a slider value. Only the money row costs money.</summary>
    public static int PayCredits(Run run, PayOption option, int value) =>
        option == PayOption.Money ? Money(run, T(run).PayMoneyMaxUnits * value / PayMoneySteps) : 0;

    /// <summary>Most other hunters near the hailed one before a pay-off has no chance: the tunable × co-op enemy scaling.</summary>
    public static int PayMaxOthers(Run run, SimRoot sim) => Scaling.EnemyScale.Cap(T(run).PayMaxOtherHunters, sim);

    /// <summary>The factor that other hunters near the hailed one put on the whole offer.</summary>
    public static float PayCrowd(Run run, SimRoot sim, Ship hunter) => DiplomacyModel.Crowd(Situation.OtherHunters(run, sim, hunter), PayMaxOthers(run, sim));

    /// <summary>The chance that one row adds at a slider value, before the crowd factor and the cap.</summary>
    public static float PayPart(Run run, SimRoot sim, PayOption option, int value)
    {
        var t = T(run);
        return option switch
        {
            PayOption.Tech => Situation.VaultTech(sim) is var stock and > 0 ? t.PayTechChance * Math.Clamp(value, 0, stock) / stock : 0f,
            PayOption.Money => MultiChoiceModel.Scaled(value, 0, PayMoneySteps, t.PayMoneyChance),
            PayOption.Crew => Math.Max(0, value) * t.PayCrewChancePer,
            _ => throw new ArgumentOutOfRangeException(nameof(option), option, null),
        };
    }

    /// <summary>Spec 594: chance = min(PayChanceCap, crowd × the sum of the selected rows).</summary>
    public static float PayOff(Run run, SimRoot sim, Ship hunter, IReadOnlyList<Pick> picks)
    {
        float crowd = PayCrowd(run, sim, hunter);
        return MultiChoiceModel.Combine(picks.Select(p => crowd * PayPart(run, sim, (PayOption)p.Option, p.Value)), T(run).PayChanceCap);
    }

    /// <summary>The value text right of a row slider: tech of the stock, credits, or a crew count.</summary>
    public static string PayValueText(Run run, SimRoot sim, PayOption option, int value) => option switch
    {
        PayOption.Tech => DipText.Format("UnitsFmt", value, Situation.VaultTech(sim)),
        PayOption.Money => Cosmoteer.Extensions.ToXmlCostString(PayCredits(run, option, value)),
        PayOption.Crew => DipText.Format("CrewFmt", value),
        _ => throw new ArgumentOutOfRangeException(nameof(option), option, null),
    };
}

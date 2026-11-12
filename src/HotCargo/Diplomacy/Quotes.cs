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

    public static int DealPrice(Run run, int units) => (int)Math.Round(units * run.TechValue() * T(run).DealPriceFactor);

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

    // ---- Punishment (spec 594 to 597)

    /// <summary>Steps of the money and bribe sliders: 0 to this, each step a twentieth of the most money.</summary>
    public const int PunishMoneySteps = 20;

    /// <summary>Crew the players can send to cryo jail now: every human ship keeps one, at most PunishCrewMax.</summary>
    public static int PunishCrewAvailable(Run run, SimRoot sim)
    {
        int crew = run.Game.HumanPlayersIndexes.Sum(sim.Crew.CountCrewForPlayer);
        int ships = Vaults.HumanShips(sim).Count();
        return Math.Clamp(crew - ships, 0, Math.Max(0, T(run).PunishCrewMax));
    }

    /// <summary>The options of the punishment page. Crew shows only when a crew member can go.</summary>
    public static IReadOnlyList<MultiOption> PunishOptions(Run run, SimRoot sim)
    {
        var options = new List<MultiOption>
        {
            new((int)PunishOption.Talk, 0, 0, 1),
            new((int)PunishOption.Money, 0, PunishMoneySteps, 1),
            new((int)PunishOption.Bribe, 0, PunishMoneySteps, 1),
        };
        int crew = PunishCrewAvailable(run, sim);
        if (crew >= 1) options.Add(new((int)PunishOption.Crew, 1, crew, 1));
        return options;
    }

    /// <summary>Credits that the money or bribe option costs at a slider value. Other options cost no money.</summary>
    public static int PunishCredits(Run run, PunishOption option, int value) => option switch
    {
        PunishOption.Money => Money(run, T(run).PunishMoneyMaxUnits * value / PunishMoneySteps),
        PunishOption.Bribe => Money(run, T(run).PunishBribeMaxUnits * value / PunishMoneySteps),
        _ => 0,
    };

    /// <summary>The chance that one option adds at a slider value.</summary>
    public static float PunishChance(Run run, SimRoot sim, PunishOption option, int value)
    {
        var t = T(run);
        switch (option)
        {
            case PunishOption.Talk: return t.PunishTalkChance;
            case PunishOption.Money: return MultiChoiceModel.Scaled(value, 0, PunishMoneySteps, t.PunishMoneyChance);
            case PunishOption.Bribe:
            {
                int others = Math.Max(0, HunterSpawner.Hunters(sim).Count() - 1);
                int max = Scaling.EnemyScale.Cap(t.PunishBribeMaxOtherHunters, sim);
                float crowd = others > max ? 0f : 1f - others / (float)(max + 1);
                return MultiChoiceModel.Scaled(value, 0, PunishMoneySteps, t.PunishBribeChance) * crowd;
            }
            case PunishOption.Crew: return Math.Max(0, value) * t.PunishCrewChancePer;
            default: throw new ArgumentOutOfRangeException(nameof(option), option, null);
        }
    }

    /// <summary>The combined chance of a draft: the sum of its options, capped at PunishChanceCap.</summary>
    public static float PunishCombined(Run run, SimRoot sim, IReadOnlyList<Pick> picks) =>
        MultiChoiceModel.Combine(picks.Select(p => PunishChance(run, sim, (PunishOption)p.Option, p.Value)), T(run).PunishChanceCap);

    /// <summary>The value text right of an option slider: credits for money and bribe, a count for crew.</summary>
    public static string PunishValueText(Run run, PunishOption option, int value) => option switch
    {
        PunishOption.Crew => DipText.Format("CrewFmt", value),
        _ => Cosmoteer.Extensions.ToXmlCostString(PunishCredits(run, option, value)),
    };
}

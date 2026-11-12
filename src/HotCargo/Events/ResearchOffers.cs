using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Modes.Career.TechTree;
using Halfling.Random;
using HotCargo.Core;

namespace HotCargo.Events;

/// <summary>
/// Research offers (spec 760-762), built from the vanilla tech tree in the game data (CareerModeRules.Techs), so mods that add or
/// reprice techs show up. Research value = what buying the tech costs here: Cost × the players' TechBuyCostFactor with the faction
/// that controls the system (1 without one) × the factions heat markup, the same price as a Tech Provider there. All inputs are sim state.
/// </summary>
public static class ResearchOffers
{
    static TechManager Techs(Run run) => run.Mode.Techs;

    /// <summary>The vanilla price factor for techs in this system (CommTechsTab.GetTechCost uses the provider's faction).</summary>
    /// The factions area's heat markup on station services (its CommTechsTab.GetTechCost patch) applies too.
    static float PriceFactor(Run run) =>
        (run.Game.Sim?.MapNode?.ControllingFaction is { } f ? run.Mode.GetCurrentReputationTitle(f).TechBuyCostFactor : 1f)
        * Factions.FactionsArea.Markup(run);

    static long Price(Run run, TechRules t) => (long)Math.Round(t.Cost * (double)PriceFactor(run));

    /// <summary>Techs that a research trade can offer: not researched, prerequisites met, a cost above 0. Sorted by ID (stable).</summary>
    static List<(string Id, long Cost)> Unresearched(Run run) =>
        GameApp.Rules.CareerMode.Techs
            .Where(t => t.Cost > 0 && !Techs(run).IsTechUnlocked(t.ID) && Techs(run).HasPrerequisites(t))
            .Select(t => (t.ID.ToString(), Price(run, t)))
            .OrderBy(t => t.Item1, StringComparer.Ordinal)
            .ToList();

    /// <summary>Techs that a research sale can buy copies of: researched, a cost above 0. Sorted by ID (stable).</summary>
    static List<(string Id, long Cost)> Researched(Run run) =>
        GameApp.Rules.CareerMode.Techs
            .Where(t => t.Cost > 0 && Techs(run).IsTechUnlocked(t.ID))
            .Select(t => (t.ID.ToString(), Price(run, t)))
            .OrderBy(t => t.Item1, StringComparer.Ordinal)
            .ToList();

    /// <summary>True when the offer can be built now. Uses the stable order, so it is the same on every client.</summary>
    public static bool Possible(Run run, OfferKind kind, int tier, long techValue) =>
        kind == OfferKind.None || Build(run, kind, tier, techValue, seed: null) is not null;

    /// <summary>
    /// Fills the page's offer. A shuffled order (seeded by the page) gives variety; when it yields nothing, the stable order is
    /// used, which Possible checked. Throws when no offer exists (a forced test event in a run that cannot build one).
    /// </summary>
    public static void Fill(Run run, PendingEvent p, OfferKind kind)
    {
        if (kind == OfferKind.None) return;
        var built = Build(run, kind, p.Tier, p.TechValue, p.Seed) ?? Build(run, kind, p.Tier, p.TechValue, seed: null)
            ?? throw new InvalidOperationException($"no {kind} offer can be built now");
        switch (built)
        {
            case ResearchTrade t:
                (p.OfferTechs, p.OfferUnits, p.OfferValue) = ([.. t.Techs], t.Units, t.Value);
                break;
            case ResearchSale s:
                (p.OfferTechs, p.OfferValue, p.OfferPrice) = ([.. s.Techs], s.Worth, s.Price);
                break;
        }
        Log.Info($"research offer {kind}: {string.Join(", ", p.OfferTechs)} value {p.OfferValue} units {p.OfferUnits} price {p.OfferPrice}");
    }

    static object? Build(Run run, OfferKind kind, int tier, long techValue, ulong? seed)
    {
        var t = run.T.Events;
        var list = kind == OfferKind.ResearchTrade ? Unresearched(run) : Researched(run);
        if (seed is { } s)
        {
            var rand = new FastRand(s, 0x5245_5345UL);
            EventModel.Shuffle(list, n => rand.Int32(0, n - 1));
        }
        return kind switch
        {
            OfferKind.ResearchTrade => EventModel.BuildResearchTrade(list, techValue, t.ResearchRate, t.ResearchMinRate, t.ResearchMaxUnits, t.ResearchMaxTechs),
            OfferKind.ResearchSale => EventModel.BuildResearchSale(list, t.ResearchSalePremium, EventRunner.Credits(run, t.ResearchSaleCap, 0f, tier, 0), t.ResearchMaxTechs),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>Unlocks the page's techs, like a vanilla tech purchase without the cost.</summary>
    public static List<string> Unlock(Run run, PendingEvent p)
    {
        var names = new List<string>();
        foreach (string id in p.OfferTechs)
        {
            var tid = new ID<TechRules>(id);
            if (Techs(run).IsTechUnlocked(tid)) continue;
            Techs(run).UnlockTech(tid);
            names.Add(Name(id));
        }
        return names;
    }

    public static string Name(string id) =>
        GameApp.Rules.CareerMode.TechsByID.TryGetValue(new ID<TechRules>(id), out var t) ? t.NameKey.Text : id;

    public static string Names(PendingEvent p) => string.Join(", ", p.OfferTechs.Select(Name));
}

using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Resources;
using Cosmoteer.Ships.Parts;
using Halfling;
using Halfling.ObjectText;
using Halfling.Serialization;

namespace HotCargo.Events;

/// <summary>When an event can open. None: only as a follow-up page (Next) or from a test command.</summary>
public enum EventTrigger
{
    None,
    /// <summary>Shortly after the first arrival in a system.</summary>
    Arrival,
    /// <summary>When a human ship reaches a signal POI (mod/events/sysgen_events.rules).</summary>
    Signal,
    /// <summary>When a human ship first comes near a trade station in a system visit.</summary>
    Station,
    /// <summary>When the players become wanted while heat is at stage 1 or more (spec 471).</summary>
    Wanted,
    /// <summary>When a human ship first comes near a vanilla abandoned ship (spec 764).</summary>
    Abandoned,
    /// <summary>When a human ship comes close to the run's relic ship (spec 765).</summary>
    Relic,
    /// <summary>A line in the comms of a station or faction ship (HailLine, HailTarget). Clicking it opens the page there (spec 769).</summary>
    Hail,
}

/// <summary>Which ships show a Hail event's line in their comms (spec 769).</summary>
public enum HailTarget
{
    /// <summary>Trade stations (not the Buyer).</summary>
    Station,
    /// <summary>Faction ships that are not stations and not hostile.</summary>
    FactionShip,
    /// <summary>The contact of an open hand-over (HotCargoHandOver, spec 1102). The line stays while the hand-over is open.</summary>
    Contact,
}

/// <summary>How a hand-over contact ends a deal (spec 1118).</summary>
public enum ContactReaction
{
    None,
    /// <summary>It turns on the players and fights.</summary>
    Hostile,
    /// <summary>It jumps out of the system.</summary>
    Leaves,
}

/// <summary>A computed offer that an event page carries (spec 760-762). Its contents come from the tech tree when the page opens.</summary>
public enum OfferKind
{
    None,
    /// <summary>Techs that the players have not researched, for tech units. Always worth more than buying them.</summary>
    ResearchTrade,
    /// <summary>Copies of researched techs, for money above their worth.</summary>
    ResearchSale,
}

/// <summary>Event categories with their own per-run limits.</summary>
public enum EventCategory
{
    Normal,
    /// <summary>Every choice costs something, with no way to avoid it. At most EventsTunables.MaxUnavoidablePerRun per run (spec 87).</summary>
    Unavoidable,
}

[ReflectiveSerialization(Explicit = true)]
public sealed class ResourceAmount
{
    [Serialize] public string ID = "";
    [Serialize] public int Amount;
    /// <summary>Added once per sector tier above 1, so amounts keep up with ship growth.</summary>
    [Serialize(Optional = true)] public float PerTier;

    public int At(int tier) => Amount + (int)MathF.Round(PerTier * Math.Max(0, tier - 1));
}

/// <summary>Conditions in rules. Money is in event money units (see EventsArea).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class NeedRules
{
    [Serialize(Optional = true)] public float Money;
    [Serialize(Optional = true)] public int Tech;
    [Serialize(Optional = true)] public int VaultSpace;
    [Serialize(Optional = true)] public int Crew;
    /// <summary>Free crew bunks (spec 1106), for a choice that brings someone aboard.</summary>
    [Serialize(Optional = true)] public int Bunks;
    /// <summary>The page's hand-over is ready (spec 1110), for a choice that hands over a ship.</summary>
    [Serialize(Optional = true)] public bool HandOverReady;
    [Serialize(Optional = true)] public float MinHeat;
    [Serialize(Optional = true)] public float MaxHeat;
    [Serialize(Optional = true)] public int MinSector;
    [Serialize(Optional = true)] public int MaxSector;
    [Serialize(Optional = true)] public ResourceAmount[] Resources = [];
    [Serialize(Optional = true)] public string[] Parts = [];
    [Serialize(Optional = true)] public string[] Flags = [];
    [Serialize(Optional = true)] public string[] NotFlags = [];
    /// <summary>Flags that must be set at least MinSectors sectors ago.</summary>
    [Serialize(Optional = true)] public FlagAge[] FlagAges = [];
    /// <summary>Sectors left before the Buyer's sector. MaxSectorsLeft -1 means no limit.</summary>
    [Serialize(Optional = true)] public int MinSectorsLeft;
    [Serialize(Optional = true)] public int MaxSectorsLeft = -1;
    /// <summary>True: only while a faction in this system wants the players.</summary>
    [Serialize(Optional = true)] public bool Wanted;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class FlagAge
{
    [Serialize] public string Flag = "";
    [Serialize] public int MinSectors;
}

/// <summary>What a choice costs for sure. The dialog shows it on the button, and the choice needs it.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class CostRules
{
    [Serialize(Optional = true)] public float Money;
    /// <summary>Extra money units per heat point, read when the page opens. For costs that grow with the bounty on the players.</summary>
    [Serialize(Optional = true)] public float MoneyPerHeat;
    [Serialize(Optional = true)] public int Tech;
    [Serialize(Optional = true)] public int Crew;
    [Serialize(Optional = true)] public ResourceAmount[] Resources = [];
    /// <summary>True: the choice costs the tech units of the page's research trade.</summary>
    [Serialize(Optional = true)] public bool OfferUnits;
}

/// <summary>One possible result of a choice. The weights pick one outcome when the player chooses.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class OutcomeRules
{
    [Serialize(Optional = true)] public float Weight = 1f;
    [Serialize(Optional = true)] public float WeightPerHeat;
    [Serialize(Optional = true)] public float WeightPerTech;
    [Serialize(Optional = true)] public string Text = "";
    /// <summary>Event money units: × the standard mission pay of the sector tier.</summary>
    [Serialize(Optional = true)] public float Money;
    /// <summary>Money in tech units: × the tech value when the event opened.</summary>
    [Serialize(Optional = true)] public float MoneyTech;
    /// <summary>× the standard mission fame of the sector tier. Fame adds heat through the normal rule (spec 25).</summary>
    [Serialize(Optional = true)] public float Fame;
    [Serialize(Optional = true)] public float Heat;
    /// <summary>× a standard mission's reputation, with the faction that controls the system. Negative loses reputation.</summary>
    [Serialize(Optional = true)] public float Reputation;
    /// <summary>Multiplies the Buyer price for the rest of the run (above 0, at most 1). 0 means no change.</summary>
    [Serialize(Optional = true)] public float BuyerPrice;
    /// <summary>True: the techs of the page's research trade unlock.</summary>
    [Serialize(Optional = true)] public bool UnlockOffer;
    /// <summary>True: the players get the price of the page's research sale.</summary>
    [Serialize(Optional = true)] public bool OfferPrice;
    /// <summary>True: every faction in this system stops wanting the players, like a paid bounty (spec 472).</summary>
    [Serialize(Optional = true)] public bool ClearWanted;
    [Serialize(Optional = true)] public int Tech;
    [Serialize(Optional = true)] public int Crew;
    /// <summary>Crew that leave the ship alive (a passenger handed on, spec 1109): removed without the death path.</summary>
    [Serialize(Optional = true)] public int CrewLeaves;
    [Serialize(Optional = true)] public ResourceAmount[] Resources = [];
    /// <summary>
    /// A ship tag (spec 1141). The Tech and resource gains drift loose as crates beside the ship with this tag that is nearest to the
    /// fleet (live, wreck or in stasis), not into the hold. Empty: gains go into the hold.
    /// </summary>
    [Serialize(Optional = true)] public string DropNear = "";
    /// <summary>Fraction of max health taken from a few random parts of the biggest player ship. Parts keep 1 health. Vaults are skipped.</summary>
    [Serialize(Optional = true)] public float HullDamage;
    /// <summary>A generator name in mod/events/spawns.rules. It spawns around the player fleet.</summary>
    [Serialize(Optional = true)] public string Spawn = "";
    /// <summary>An event ID that opens next, as a follow-up page. This outcome's text leads it.</summary>
    [Serialize(Optional = true)] public string Next = "";
    [Serialize(Optional = true)] public string[] SetFlags = [];
    [Serialize(Optional = true)] public string[] ClearFlags = [];
    /// <summary>Completes the open hand-over whose contact carries this page (spec 1102). Put the item in the choice Cost.</summary>
    [Serialize(Optional = true)] public bool HandOver;
    /// <summary>With HandOver: the job's money reward × this (spec 1118). 0 means no change.</summary>
    [Serialize(Optional = true)] public float HandOverPay;
    /// <summary>The contact takes the item and ends the deal (spec 1118). The hand-over fails. Do not combine with HandOver.</summary>
    [Serialize(Optional = true)] public ContactReaction ContactTurns;
    /// <summary>True: the outcome text also goes to the chat, so every player knows what happened.</summary>
    [Serialize(Optional = true)] public bool Chat;
    /// <summary>On a chain page (mod/chains/pages.rules): the result that this outcome gives the chain stage. Empty elsewhere.</summary>
    [Serialize(Optional = true)] public string ChainResult = "";

    /// <summary>
    /// A copy with Money, MoneyTech and Fame × k (spec 812). With <paramref name="all"/> also Heat, Reputation, Tech, Crew,
    /// resources (at the tier) and hull damage. Flags, text and the other fields stay as they are.
    /// </summary>
    public OutcomeRules Scaled(float k, int tier, bool all)
    {
        if (k < 0 || float.IsNaN(k)) throw new ArgumentOutOfRangeException(nameof(k), k, "scale must not be negative");
        var c = (OutcomeRules)MemberwiseClone();
        c.Money *= k;
        c.MoneyTech *= k;
        c.Fame *= k;
        if (!all) return c;
        c.Heat *= k;
        c.Reputation *= k;
        c.Tech = (int)MathF.Round(Tech * k, MidpointRounding.AwayFromZero);
        c.Crew = (int)MathF.Round(Crew * k, MidpointRounding.AwayFromZero);
        c.HullDamage = Math.Min(1f, HullDamage * k);
        c.Resources = [.. Resources.Select(r => new ResourceAmount { ID = r.ID, Amount = (int)MathF.Round(r.At(tier) * k, MidpointRounding.AwayFromZero) })];
        return c;
    }
}

[ReflectiveSerialization(Explicit = true)]
public sealed class ChoiceRules
{
    [Serialize] public string Text = "";
    [Serialize(Optional = true)] public NeedRules? Need;
    [Serialize(Optional = true)] public CostRules? Cost;
    /// <summary>True: the choice does not show at all when its need is not met (for flag-gated story options).</summary>
    [Serialize(Optional = true)] public bool HideIfUnmet;
    /// <summary>
    /// Above 0: a submit of the page's options (spec 811). It needs at least this many points from the selected options,
    /// plus NeedPointsPerHeat × heat when the page opened. It pays the selected options and applies their effects.
    /// </summary>
    [Serialize(Optional = true)] public float NeedPoints;
    [Serialize(Optional = true)] public float NeedPointsPerHeat;
    /// <summary>True: the outcome's Money, MoneyTech and Fame scale with the points of the selected options (a sale by amount).</summary>
    [Serialize(Optional = true)] public bool ScaleByPoints;
    [Serialize] public OutcomeRules[] Outcomes = [];

    public bool IsSubmit => NeedPoints > 0;
}

/// <summary>What a page option pays with (spec 810). One unit is the option's Unit of it.</summary>
public enum PayKind
{
    /// <summary>Nothing: the option only has effects (refugees taken aboard, forged codes).</summary>
    None,
    /// <summary>The chooser's money. Unit is in event money units.</summary>
    Money,
    /// <summary>Tech from the vaults, in crates.</summary>
    Tech,
    /// <summary>Crew who leave the fleet.</summary>
    Crew,
    /// <summary>Crew on hand who do the work and stay. The option needs them; it pays nothing.</summary>
    CrewWork,
    /// <summary>A resource from the fleet's storage.</summary>
    Resource,
}

/// <summary>
/// One option of an event page's multiselect (spec 810). Players select options and set their amounts, then pick a submit
/// choice. The slider picks steps: units = steps × StepUnits, from MinUnits to MaxUnits. Rules numbers are per unit.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class OptionRules
{
    [Serialize] public string Text = "";
    [Serialize(Optional = true)] public PayKind Pay;
    [Serialize(Optional = true)] public string Resource = "";
    /// <summary>Size of one unit: money units, crates, crew or resource amount. Plus UnitPerTier for each tier above 1, plus UnitPerHeat × heat.</summary>
    [Serialize(Optional = true)] public float Unit = 1f;
    [Serialize(Optional = true)] public float UnitPerTier;
    [Serialize(Optional = true)] public float UnitPerHeat;
    /// <summary>Slider range and step in units. MinUnits = MaxUnits: no slider.</summary>
    [Serialize(Optional = true)] public float MinUnits = 1f;
    [Serialize(Optional = true)] public float MaxUnits = 1f;
    [Serialize(Optional = true)] public float StepUnits = 1f;
    /// <summary>Points per unit toward a submit's NeedPoints.</summary>
    [Serialize(Optional = true)] public float Points = 1f;
    /// <summary>Effects per unit, scaled by the units. Flags apply once. Its Text, when set, shows as a result line.</summary>
    [Serialize(Optional = true)] public OutcomeRules? Effect;
    /// <summary>Effects once when the option is selected, whatever its amount.</summary>
    [Serialize(Optional = true)] public OutcomeRules? Once;
    /// <summary>True: a submit takes its first outcome without a roll while this option is selected.</summary>
    [Serialize(Optional = true)] public bool Sure;
    /// <summary>Chance per unit (capped at 1) of the Risk effects, rolled once on submit.</summary>
    [Serialize(Optional = true)] public float RiskChance;
    [Serialize(Optional = true)] public OutcomeRules? Risk;
    /// <summary>The slider's value text for Pay = None, with {0} = units × unit size, for example "{0} people".</summary>
    [Serialize(Optional = true)] public string ValueText = "";
    [Serialize(Optional = true)] public NeedRules? Need;
    /// <summary>True: the option does not show while its need is not met.</summary>
    [Serialize(Optional = true)] public bool HideIfUnmet;

    public int MinSteps => (int)MathF.Round(MinUnits / StepUnits);
    public int MaxSteps => (int)MathF.Round(MaxUnits / StepUnits);
}

[ReflectiveSerialization(Explicit = true)]
public sealed class EventRules
{
    [Serialize] public string ID = "";
    [Serialize(Optional = true)] public EventTrigger Trigger;
    [Serialize(Optional = true)] public EventCategory Category;
    /// <summary>
    /// A generator in spawns.rules for the ship that carries the page (spec 767). It spawns when the page opens; its ship must have
    /// the tag hc_ev_carrier. The page shows in that ship's comms, not in the event window.
    /// </summary>
    [Serialize(Optional = true)] public string Carrier = "";
    /// <summary>True: the carrier hails the players when the page opens. False: the players must hail it.</summary>
    [Serialize(Optional = true)] public bool CarrierHails = true;
    /// <summary>Hail events: the button text in the target's comms.</summary>
    [Serialize(Optional = true)] public string HailLine = "";
    [Serialize(Optional = true)] public HailTarget HailTarget;
    /// <summary>A computed offer. The event can open only when the offer can be built.</summary>
    [Serialize(Optional = true)] public OfferKind Offer;
    [Serialize(Optional = true)] public float Weight = 10f;
    [Serialize(Optional = true)] public float WeightPerHeat;
    [Serialize(Optional = true)] public float WeightPerTech;
    /// <summary>How often the event can open in one run. 0 means no limit.</summary>
    [Serialize(Optional = true)] public int MaxPerRun = 1;
    [Serialize(Optional = true)] public NeedRules? Need;
    [Serialize] public string Title = "";
    [Serialize] public string Text = "";
    [Serialize] public ChoiceRules[] Choices = [];
    /// <summary>Multiselect options of the page (spec 810). The submit choices (NeedPoints above 0) use them.</summary>
    [Serialize(Optional = true)] public OptionRules[] Options = [];
    /// <summary>A line under the options, with {0} = the offer in percent of the first submit's need. Empty: no line.</summary>
    [Serialize(Optional = true)] public string OfferLine = "";
}

[ReflectiveSerialization(Explicit = true)]
sealed class EventFile
{
    [Serialize] public EventRules[] Events = [];
}

/// <summary>
/// Event content from mod/events/events.rules and spawn generators from mod/events/spawns.rules.
/// They are not part of the game rules tree; C# reads them on first use. A change needs a game restart.
/// Limit: text is English and lives in events.rules, not in a strings file. Revisit if the mod gets translations.
/// </summary>
public static class EventLibrary
{
    public const int MaxChoices = 4;
    /// <summary>Option rows that fit the event window and a hail page with its choices.</summary>
    public const int MaxOptions = 4;

    static Dictionary<string, EventRules>? _events;
    static EventRules[] _ordered = [];
    static readonly Dictionary<string, SimulationGenerator> Spawns = new(StringComparer.Ordinal);

    public static string EventsPath => Path.Combine(Mod.ModRoot, "events", "events.rules");
    /// <summary>More event files in the events.rules format, added by other areas in Register (the Chains area adds its pages).</summary>
    public static readonly List<string> ExtraFiles = [];
    public static string SpawnsPath => Path.Combine(Mod.ModRoot, "events", "spawns.rules");

    /// <summary>Set when loading failed. Events stay off for the rest of the game (spec 78), so the error shows once.</summary>
    public static string? LoadError { get; private set; }

    /// <summary>All events by ID, in file order. Loads and validates on first use; a content error throws.</summary>
    public static IReadOnlyDictionary<string, EventRules> Events
    {
        get
        {
            if (_events is not null) return _events;
            if (LoadError is not null) throw new InvalidDataException(LoadError);
            try { return _events = Load(); }
            catch (Exception ex)
            {
                LoadError = $"{ex.Message} (events are off until the game restarts)";
                throw;
            }
        }
    }

    /// <summary>Events in file order. The order is stable, so weighted picks are deterministic.</summary>
    public static IReadOnlyList<EventRules> InOrder { get { _ = Events; return _ordered; } }

    public static EventRules Get(string id) =>
        Events.TryGetValue(id, out var e) ? e : throw new InvalidDataException($"no event with ID '{id}' in {EventsPath}");

    internal static SimulationGenerator Spawn(string name)
    {
        if (Spawns.TryGetValue(name, out var g)) return g;
        g = App.ObjectTextSerializer.ReadFromPath<SimulationGenerator>(new OTFile(SpawnsPath), name)
            ?? throw new InvalidDataException($"spawn '{name}' missing in {SpawnsPath}");
        if (g.Spawners is not { Length: > 0 }) throw new InvalidDataException($"spawn '{name}' has no Spawners in {SpawnsPath}");
        Spawns[name] = g;
        return g;
    }

    static Dictionary<string, EventRules> Load()
    {
        var map = new Dictionary<string, EventRules>(StringComparer.Ordinal);
        var ordered = new List<EventRules>();
        foreach (string path in ExtraFiles.Prepend(EventsPath))
        {
            var file = App.ObjectTextSerializer.Read<EventFile>(new OTFile(path)) ?? throw new InvalidDataException($"{path} is empty");
            foreach (var e in file.Events)
                if (!map.TryAdd(e.ID, e)) throw new InvalidDataException($"event ID '{e.ID}' in {path} is used twice");
            ordered.AddRange(file.Events);
        }
        foreach (var e in ordered) Validate(e, map);
        Spawn(EventRunner.CourierSpawn);
        Spawn(Abandoned.RelicSpawn);
        _ordered = [.. ordered];
        Log.Info($"events loaded: {map.Count} events, {map.Values.Count(e => e.Trigger != EventTrigger.None)} with a trigger");
        return map;
    }

    /// <summary>Checks references and ranges, so that a content mistake shows at game start and not when a player picks the choice.</summary>
    static void Validate(EventRules e, Dictionary<string, EventRules> all)
    {
        string at = $"event '{e.ID}'";
        if (e.ID.Length == 0) throw new InvalidDataException("an event has no ID");
        if (e.Title.Length == 0 || e.Text.Length == 0) throw new InvalidDataException($"{at} needs a Title and a Text");
        if (e.Trigger == EventTrigger.Hail && e.HailLine.Length == 0) throw new InvalidDataException($"{at}: a Hail event needs a HailLine");
        if (e.Carrier.Length > 0) Spawn(e.Carrier);
        if (e.Choices.Length is < 1 or > MaxChoices) throw new InvalidDataException($"{at} needs 1 to {MaxChoices} choices");
        if (e.Weight < 0) throw new InvalidDataException($"{at}: Weight must not be negative");
        ValidateNeed(e.Need, at);
        if (e.Options.Length > MaxOptions) throw new InvalidDataException($"{at} has more than {MaxOptions} options");
        if (e.Options.Length > 0 && !e.Choices.Any(c => c.IsSubmit)) throw new InvalidDataException($"{at} has options but no choice with NeedPoints");
        for (int o = 0; o < e.Options.Length; o++)
        {
            var opt = e.Options[o];
            string oat = $"{at} option {o + 1}";
            if (opt.Text.Length == 0) throw new InvalidDataException($"{oat} has no Text");
            if (opt.StepUnits <= 0 || opt.MinUnits <= 0 || opt.MaxUnits < opt.MinUnits || opt.MinSteps < 1 || opt.MaxSteps >= MultiChoiceSpan)
                throw new InvalidDataException($"{oat}: needs 0 < MinUnits <= MaxUnits and StepUnits above 0, at most {MultiChoiceSpan - 1} steps");
            if (opt.Unit <= 0 || opt.Points < 0 || opt.RiskChance < 0) throw new InvalidDataException($"{oat}: Unit must be above 0, Points and RiskChance not negative");
            if ((opt.Pay == PayKind.Resource) != (opt.Resource.Length > 0)) throw new InvalidDataException($"{oat}: Resource is set exactly when Pay = Resource");
            if (opt.Pay == PayKind.Resource) ValidateResource(opt.Resource, oat);
            if (opt.Pay == PayKind.None && opt.MaxSteps > opt.MinSteps && opt.ValueText.Length == 0) throw new InvalidDataException($"{oat}: a slider without Pay needs a ValueText");
            if (opt.RiskChance > 0 != opt.Risk is not null) throw new InvalidDataException($"{oat}: RiskChance and Risk go together");
            ValidateNeed(opt.Need, oat);
            foreach (var fx in new[] { opt.Effect, opt.Once, opt.Risk })
                if (fx is not null && (fx.Next.Length > 0 || fx.Spawn.Length > 0 || fx.ChainResult.Length > 0 || fx.BuyerPrice > 0 || fx.UnlockOffer || fx.OfferPrice))
                    throw new InvalidDataException($"{oat}: option effects take numbers, flags and text only (no Next, Spawn, ChainResult, BuyerPrice or offers)");
        }
        for (int c = 0; c < e.Choices.Length; c++)
        {
            var choice = e.Choices[c];
            string cat = $"{at} choice {c + 1}";
            if (choice.Text.Length == 0) throw new InvalidDataException($"{cat} has no Text");
            if (choice.Outcomes.Length == 0) throw new InvalidDataException($"{cat} has no Outcomes");
            if (choice.Outcomes.All(o => o.Weight <= 0)) throw new InvalidDataException($"{cat}: at least one outcome needs a Weight above 0");
            ValidateNeed(choice.Need, cat);
            foreach (var r in choice.Cost?.Resources ?? []) ValidateResource(r.ID, cat);
            if (choice.Cost?.OfferUnits == true && e.Offer != OfferKind.ResearchTrade) throw new InvalidDataException($"{cat}: Cost.OfferUnits needs Offer = ResearchTrade");
            if (choice.IsSubmit && e.Options.Length == 0) throw new InvalidDataException($"{cat}: NeedPoints needs page Options");
            if (choice.NeedPoints < 0 || choice.NeedPointsPerHeat < 0) throw new InvalidDataException($"{cat}: NeedPoints must not be negative");
            if (choice.Cost is { } cost && (cost.Money < 0 || cost.MoneyPerHeat < 0 || cost.Tech < 0 || cost.Crew < 0)) throw new InvalidDataException($"{cat}: Cost values must not be negative");
            for (int o = 0; o < choice.Outcomes.Length; o++)
            {
                var outcome = choice.Outcomes[o];
                string oat = $"{cat} outcome {o + 1}";
                if (outcome.Weight < 0) throw new InvalidDataException($"{oat}: Weight must not be negative");
                if (outcome.Text.Length == 0 && outcome.Next.Length == 0) throw new InvalidDataException($"{oat} needs a Text or a Next");
                if (outcome.Next.Length > 0 && !all.ContainsKey(outcome.Next)) throw new InvalidDataException($"{oat}: Next '{outcome.Next}' is not an event");
                if (outcome.HullDamage is < 0 or > 1) throw new InvalidDataException($"{oat}: HullDamage must be 0 to 1");
                if (outcome.UnlockOffer && e.Offer != OfferKind.ResearchTrade) throw new InvalidDataException($"{oat}: UnlockOffer needs Offer = ResearchTrade");
                if (outcome.OfferPrice && e.Offer != OfferKind.ResearchSale) throw new InvalidDataException($"{oat}: OfferPrice needs Offer = ResearchSale");
                if (outcome.BuyerPrice is < 0 or > 1) throw new InvalidDataException($"{oat}: BuyerPrice must be 0 (no change) or above 0 and at most 1");
                foreach (var r in outcome.Resources) ValidateResource(r.ID, oat);
                if (outcome.Spawn.Length > 0) Spawn(outcome.Spawn);
                if (outcome.HandOverPay < 0 || outcome.HandOverPay > 0 && !outcome.HandOver) throw new InvalidDataException($"{oat}: HandOverPay needs HandOver and must not be negative");
                if (outcome.ContactTurns != ContactReaction.None && (outcome.HandOver || e.HailTarget != HailTarget.Contact))
                    throw new InvalidDataException($"{oat}: ContactTurns needs a Contact page and no HandOver");
            }
        }
    }

    /// <summary>Slider steps of an option stay below this, like the amounts of other multiselect domains.</summary>
    const int MultiChoiceSpan = 1000;

    static void ValidateNeed(NeedRules? need, string at)
    {
        if (need is null) return;
        foreach (var r in need.Resources) ValidateResource(r.ID, at);
        foreach (string part in need.Parts)
            if (!GameApp.Rules.Ships.Any(s => s.PartsByID.ContainsKey(new ID<PartRules>(part))))
                throw new InvalidDataException($"{at}: part '{part}' does not exist");
    }

    static void ValidateResource(string id, string at)
    {
        if (!GameApp.Rules.HasResourceRules(new ID<ResourceRules>(id))) throw new InvalidDataException($"{at}: resource '{id}' does not exist");
    }
}

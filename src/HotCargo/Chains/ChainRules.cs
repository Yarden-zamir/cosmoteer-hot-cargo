using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Resources;
using Halfling;
using Halfling.ObjectText;
using Halfling.Serialization;
using HotCargo.Events;

namespace HotCargo.Chains;

/// <summary>The kind of station that a chain can start at, from the vanilla station tags.</summary>
public enum StationKind { Trade, Military }

/// <summary>
/// Contract terms of a stage mission, as data. Same field names and units as HotCargoContract (Contracts/ContractTerms.cs on feat/economy).
/// ChainMissions.AttachTerms turns them into the economy's contract carrier once that type is in main.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class StageTermsRules
{
    /// <summary>In standard mission pay at the mission tier (the event money unit). 0 = free.</summary>
    [Serialize(Optional = true)] public float? AcceptFee;
    [Serialize(Optional = true)] public float? FlatHeat;
    [Serialize(Optional = true)] public float? TrailWeight;
    [Serialize(Optional = true)] public string? RivalGroup;
    /// <summary>0 to 1.</summary>
    [Serialize(Optional = true)] public float? InformantChance;
}

/// <summary>One row of a stage's result table: which result it handles, what follows and what it changes.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ResultRules
{
    /// <summary>A mission result (success, fail, timeout, lost, abandoned), a page ChainResult, or "any".</summary>
    [Serialize] public string On = "";
    [Serialize(Optional = true)] public string[] IfFlags = [];
    [Serialize(Optional = true)] public string[] NotFlags = [];
    /// <summary>The next stage ID. Empty ends the chain.</summary>
    [Serialize(Optional = true)] public string Next = "";
    /// <summary>One line for the story so far.</summary>
    [Serialize(Optional = true)] public string Story = "";
    /// <summary>Event outcome effects (money, fame, heat, tech, crew, resources, hull damage, spawns, event flags) and their text.</summary>
    [Serialize(Optional = true)] public OutcomeRules? Effects;
    /// <summary>Reputation with the station's faction, in standard mission reputation at the sector tier.</summary>
    [Serialize(Optional = true)] public float Reputation;
    /// <summary>Chain flags. Later rows of this chain read them.</summary>
    [Serialize(Optional = true)] public string[] SetFlags = [];
    [Serialize(Optional = true)] public string[] ClearFlags = [];
}

[ReflectiveSerialization(Explicit = true)]
public sealed class StageRules
{
    [Serialize] public string ID = "";
    /// <summary>Short stage name. The comms label shows it after the chain title.</summary>
    [Serialize(Optional = true)] public string Title = "";
    /// <summary>The mission briefing. The comms description shows it under the story so far.</summary>
    [Serialize(Optional = true)] public string Text = "";
    /// <summary>A generator name in mod/chains/missions.rules. Set this or Page.</summary>
    [Serialize(Optional = true)] public string Mission = "";
    /// <summary>An event ID in mod/chains/pages.rules. Set this or Mission.</summary>
    [Serialize(Optional = true)] public string Page = "";
    [Serialize(Optional = true)] public StageWhere Where;
    /// <summary>Arrival stages: sectors after the stage began. 0 starts the stage in the current system, LaterDelaySeconds after it began.</summary>
    [Serialize(Optional = true)] public int AfterSectors = 1;
    [Serialize(Optional = true)] public StageTermsRules? Terms;
    [Serialize] public ResultRules[] Results = [];

    public StageKind Kind => Mission.Length > 0 ? StageKind.Mission : StageKind.Page;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class ChainRules
{
    [Serialize] public string ID = "";
    [Serialize] public string Title = "";
    [Serialize(Optional = true)] public float Weight = 10f;
    [Serialize(Optional = true)] public int MinSector;
    /// <summary>Zero means no limit.</summary>
    [Serialize(Optional = true)] public int MaxSector;
    /// <summary>Station kinds that can get the chain. Empty means all.</summary>
    [Serialize(Optional = true)] public StationKind[] Stations = [];
    [Serialize] public StageRules[] Stages = [];

    public StageRules Stage(string id) =>
        Stages.FirstOrDefault(s => s.ID == id) ?? throw new InvalidDataException($"chain '{ID}' has no stage '{id}'");

    public IReadOnlyList<ResultRow> Rows(StageRules s) => s.Results.Select(r => new ResultRow(r.On, r.Next, r.IfFlags, r.NotFlags)).ToList();
}

[ReflectiveSerialization(Explicit = true)]
sealed class ChainFile
{
    [Serialize] public ChainRules[] Chains = [];
}

/// <summary>
/// Chain content: mod/chains/chains.rules (chains and stages), missions.rules (stage mission generators), pages.rules (choice pages,
/// loaded by the EventLibrary). C# reads them on first use, so a change needs a game restart.
/// Limit: text is English and lives in the rules files, like the event text. Revisit if the mod gets translations.
/// </summary>
public static class ChainLibrary
{
    public static readonly ID<MissionMetatype> Metatype = new("HotCargoChain");

    static Dictionary<string, ChainRules>? _chains;
    static ChainRules[] _ordered = [];
    static readonly Dictionary<string, SimulationGenerator> Generators = new(StringComparer.Ordinal);

    public static string ChainsPath => Path.Combine(Mod.ModRoot, "chains", "chains.rules");
    public static string MissionsPath => Path.Combine(Mod.ModRoot, "chains", "missions.rules");
    public static string PagesPath => Path.Combine(Mod.ModRoot, "chains", "pages.rules");

    /// <summary>Set when loading failed. Chains stay off for the rest of the game, so the error shows once.</summary>
    public static string? LoadError { get; private set; }

    /// <summary>Chains in file order. The order is stable, so weighted picks are deterministic. Loads and validates on first use.</summary>
    public static IReadOnlyList<ChainRules> InOrder
    {
        get
        {
            if (_chains is not null) return _ordered;
            if (LoadError is not null) throw new InvalidDataException(LoadError);
            try { _chains = Load(); return _ordered; }
            catch (Exception ex)
            {
                LoadError = $"{ex.Message} (chains are off until the game restarts)";
                throw;
            }
        }
    }

    public static ChainRules Get(string id)
    {
        _ = InOrder;
        return _chains!.TryGetValue(id, out var c) ? c : throw new InvalidDataException($"no chain with ID '{id}' in {ChainsPath}");
    }

    public static SimulationGenerator Generator(string name)
    {
        if (Generators.TryGetValue(name, out var g)) return g;
        g = App.ObjectTextSerializer.ReadFromPath<SimulationGenerator>(new OTFile(MissionsPath), name)
            ?? throw new InvalidDataException($"mission '{name}' missing in {MissionsPath}");
        if (g.Spawners is not { Length: > 0 }) throw new InvalidDataException($"mission '{name}' has no Spawners in {MissionsPath}");
        if (!AllSpawners(g.Spawners).OfType<MissionSpawner>().Any(m => m.Metatype == Metatype))
            throw new InvalidDataException($"mission '{name}' in {MissionsPath} has no Mission spawner with Metatype = {Metatype}");
        Generators[name] = g;
        return g;
    }

    internal static IEnumerable<SimSpawner> AllSpawners(IEnumerable<SimSpawner> roots)
    {
        foreach (SimSpawner s in roots)
        {
            yield return s;
            if (s is SimObjectSpawner { SubSpawners: { } subs })
                foreach (SimSpawner sub in AllSpawners(subs)) yield return sub;
        }
    }

    static Dictionary<string, ChainRules> Load()
    {
        var file = App.ObjectTextSerializer.Read<ChainFile>(new OTFile(ChainsPath)) ?? throw new InvalidDataException($"{ChainsPath} is empty");
        var map = new Dictionary<string, ChainRules>(StringComparer.Ordinal);
        foreach (var c in file.Chains)
            if (c.ID.Length == 0 || !map.TryAdd(c.ID, c)) throw new InvalidDataException($"chain ID '{c.ID}' is empty or used twice in {ChainsPath}");
        var errors = file.Chains.SelectMany(Validate).ToList();
        if (errors.Count > 0) throw new InvalidDataException($"{errors.Count} chain content errors, first: {errors[0]}");
        _ordered = file.Chains;
        Log.Info($"chains loaded: {map.Count} chains, {file.Chains.Sum(c => c.Stages.Length)} stages");
        return map;
    }

    /// <summary>Checks the graph, the references to missions and pages, and the effects, so a mistake shows at the first chain and not mid-run.</summary>
    static List<string> Validate(ChainRules c)
    {
        var errors = new List<string>();
        if (c.Title.Length == 0) errors.Add($"chain '{c.ID}' needs a Title");
        var nodes = new List<StageNode>();
        foreach (var s in c.Stages)
        {
            string at = $"chain '{c.ID}' stage '{s.ID}'";
            if ((s.Mission.Length > 0) == (s.Page.Length > 0)) errors.Add($"{at} needs exactly one of Mission and Page");
            var pageResults = new List<string>();
            try
            {
                if (s.Mission.Length > 0) Generator(s.Mission);
                if (s.Page.Length > 0) pageResults = PageResults(s.Page, at);
                if (s.Kind == StageKind.Mission && (s.Title.Length == 0 || s.Text.Length == 0)) errors.Add($"{at}: a mission stage needs a Title and a Text for comms");
                foreach (var r in s.Results) ValidateEffects(r.Effects, $"{at} result '{r.On}'");
            }
            catch (InvalidDataException ex) { errors.Add(ex.Message); }
            nodes.Add(new StageNode(s.ID, s.Kind, s.Where, c.Rows(s), pageResults));
        }
        errors.AddRange(ChainModel.Validate(new ChainNode(c.ID, nodes)));
        return errors;
    }

    /// <summary>Every ChainResult that a page and its follow-up pages give. Each outcome on the way must give a ChainResult or lead on (Next).</summary>
    static List<string> PageResults(string pageId, string at)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var todo = new Stack<string>([pageId]);
        while (todo.TryPop(out string? id))
        {
            if (!seen.Add(id)) continue;
            var e = EventLibrary.Get(id);
            if (e.Trigger != EventTrigger.None) throw new InvalidDataException($"{at}: page '{id}' must have no Trigger");
            foreach (var o in e.Choices.SelectMany(ch => ch.Outcomes))
            {
                if ((o.ChainResult.Length > 0) == (o.Next.Length > 0)) throw new InvalidDataException($"{at}: every outcome of page '{id}' needs exactly one of ChainResult and Next");
                if (o.Next.Length > 0) todo.Push(o.Next);
                else if (!results.Contains(o.ChainResult)) results.Add(o.ChainResult);
            }
        }
        return results;
    }

    static void ValidateEffects(OutcomeRules? o, string at)
    {
        if (o is null) return;
        if (o.Next.Length > 0 || o.ChainResult.Length > 0) throw new InvalidDataException($"{at}: Effects take no Next or ChainResult; use the row's Next");
        if (o.HullDamage is < 0 or > 1) throw new InvalidDataException($"{at}: HullDamage must be 0 to 1");
        foreach (var r in o.Resources)
            if (!GameApp.Rules.HasResourceRules(new ID<ResourceRules>(r.ID))) throw new InvalidDataException($"{at}: resource '{r.ID}' does not exist");
        if (o.Spawn.Length > 0) EventLibrary.Spawn(o.Spawn);
    }
}

namespace HotCargo.Chains;

/// <summary>What a stage is: a mission offer, or a choice page (an event page with ChainResult outcomes).</summary>
public enum StageKind { Mission, Page }

/// <summary>
/// Where a stage starts.
/// Station: the mission is offered in the station's comms, or the page opens when a player ship comes near the station.
/// Return: the next time the players enter the station's system after they left it.
/// Arrival: at the first arrival in a new system, AfterSectors sectors after the stage began.
/// </summary>
public enum StageWhere { Station, Return, Arrival }

/// <summary>One row of a stage's result table. The first row whose On matches and whose flag conditions hold wins.</summary>
public sealed record ResultRow(string On, string Next, IReadOnlyList<string> IfFlags, IReadOnlyList<string> NotFlags)
{
    public bool Unconditional => IfFlags.Count == 0 && NotFlags.Count == 0;
}

/// <summary>A stage as the graph logic sees it. PageResults: every ChainResult that the page can give (empty for missions).</summary>
public sealed record StageNode(string Id, StageKind Kind, StageWhere Where, IReadOnlyList<ResultRow> Results, IReadOnlyList<string> PageResults);

public sealed record ChainNode(string Id, IReadOnlyList<StageNode> Stages);

/// <summary>How a stage mission left the mission log. Built from the mission's objectives.</summary>
public sealed record MissionEnd(bool Completed, bool Failed, bool TimedOut, bool TargetLost);

/// <summary>A chain that a station can get, with how often the run already handed it out.</summary>
public sealed record ChainChoice(string Id, float Weight, int TimesUsed);

/// <summary>Pure chain graph logic on plain data. No game types, so unit tests run on the Mac without the game.</summary>
public static class ChainModel
{
    public const string Success = "success";
    public const string Fail = "fail";
    public const string Timeout = "timeout";
    public const string Lost = "lost";
    public const string Abandoned = "abandoned";
    /// <summary>Matches every result. Use it as the last row.</summary>
    public const string Any = "any";

    /// <summary>The results that a mission stage can give.</summary>
    public static readonly IReadOnlyList<string> MissionResults = [Success, Timeout, Lost, Abandoned, Fail];

    /// <summary>The keys that a result tries in order: itself, then "fail" for a failure kind, then "any".</summary>
    public static IEnumerable<string> Keys(string result)
    {
        yield return result;
        if (result is Timeout or Lost or Abandoned) yield return Fail;
        if (result != Any) yield return Any;
    }

    /// <summary>The index of the row that handles the result with the given chain flags, or -1.</summary>
    public static int Match(IReadOnlyList<ResultRow> rows, string result, IReadOnlySet<string> flags)
    {
        foreach (string key in Keys(result))
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].On == key && rows[i].IfFlags.All(flags.Contains) && !rows[i].NotFlags.Any(flags.Contains)) return i;
        return -1;
    }

    /// <summary>True when the result finds a row whatever the flags are: some key of the result has a row without flag conditions.</summary>
    public static bool Covered(IReadOnlyList<ResultRow> rows, string result) =>
        Keys(result).Any(key => rows.Any(r => r.On == key && r.Unconditional));

    /// <summary>The result of a stage mission that left the log.</summary>
    public static string Classify(MissionEnd end) => end switch
    {
        { Completed: true } => Success,
        { Failed: true, TimedOut: true } => Timeout,
        { Failed: true, TargetLost: true } => Lost,
        { Failed: true } => Fail,
        // Removed while in progress: the players abandoned it.
        _ => Abandoned,
    };

    /// <summary>
    /// Picks a chain for a station: only chains that the run used least often take part, so no chain repeats while unused ones remain.
    /// Among those, each wins in proportion to its weight. Returns the index in choices, or -1 when no chain has a weight above 0.
    /// </summary>
    public static int Pick(IReadOnlyList<ChainChoice> choices, float roll01)
    {
        var live = Enumerable.Range(0, choices.Count).Where(i => choices[i].Weight > 0f).ToList();
        if (live.Count == 0) return -1;
        int least = live.Min(i => choices[i].TimesUsed);
        var pool = live.Where(i => choices[i].TimesUsed == least).ToList();
        int pick = Events.EventModel.PickWeighted(pool.Select(i => choices[i].Weight).ToList(), roll01);
        return pick < 0 ? -1 : pool[pick];
    }

    /// <summary>The story line under a stage: the last maxLines story entries.</summary>
    public static string StorySoFar(IReadOnlyList<string> story, int maxLines) =>
        string.Join(" ", story.Skip(Math.Max(0, story.Count - maxLines)));

    /// <summary>Every content error of a chain graph. Empty means valid.</summary>
    public static List<string> Validate(ChainNode chain)
    {
        var errors = new List<string>();
        string at = $"chain '{chain.Id}'";
        if (chain.Stages.Count == 0) { errors.Add($"{at} has no stages"); return errors; }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in chain.Stages)
            if (s.Id.Length == 0) errors.Add($"{at} has a stage without an ID");
            else if (!ids.Add(s.Id)) errors.Add($"{at}: stage ID '{s.Id}' is used twice");
        var first = chain.Stages[0];
        if (first.Kind != StageKind.Mission || first.Where != StageWhere.Station)
            errors.Add($"{at}: the first stage must be a mission at the station, so the players choose to start the chain");
        bool hasEnding = false;
        foreach (var s in chain.Stages)
        {
            string sat = $"{at} stage '{s.Id}'";
            if (s.Results.Count == 0) errors.Add($"{sat} has no Results");
            foreach (var r in s.Results)
            {
                if (r.On.Length == 0) errors.Add($"{sat}: a result row has no On");
                if (r.Next.Length == 0) hasEnding = true;
                else if (!ids.Contains(r.Next)) errors.Add($"{sat}: Next '{r.Next}' is not a stage of the chain");
            }
            var needed = s.Kind == StageKind.Mission ? MissionResults : s.PageResults;
            if (s.Kind == StageKind.Page && s.PageResults.Count == 0) errors.Add($"{sat}: the page gives no ChainResult");
            foreach (string result in needed)
                if (!Covered(s.Results, result)) errors.Add($"{sat}: no result row without flag conditions handles '{result}'");
            if (s.Kind == StageKind.Page)
                foreach (var r in s.Results)
                    if (r.On != Any && !s.PageResults.Contains(r.On)) errors.Add($"{sat}: result '{r.On}' never comes from the page");
        }
        if (!hasEnding) errors.Add($"{at} has no ending (a result row with an empty Next)");
        foreach (string id in ids.Except(Reachable(chain))) errors.Add($"{at}: stage '{id}' cannot be reached from the first stage");
        return errors;
    }

    static HashSet<string> Reachable(ChainNode chain)
    {
        var byId = chain.Stages.Where(s => s.Id.Length > 0).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var todo = new Stack<string>([chain.Stages[0].Id]);
        while (todo.TryPop(out string? id))
        {
            if (!seen.Add(id) || !byId.TryGetValue(id, out var s)) continue;
            foreach (var r in s.Results) if (r.Next.Length > 0) todo.Push(r.Next);
        }
        return seen;
    }
}

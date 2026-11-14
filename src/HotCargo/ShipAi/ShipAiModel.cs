using System.Globalization;
using System.Text;

namespace HotCargo.ShipAi;

/// <summary>Spec 401, 413. A tactic condition. Each one reads one fact about the ship (ShipFacts).</summary>
public enum Cond
{
    Always, HealthBelow, ShieldsBelow, PartLost, EnemyWithin, EnemyBeyond, TargetedByEnemy, AllyUnderAttack,
    AmmoBelow, BatteryBelow, EnemyClassNear, EnemiesAtLeast, CarryingTech, VaultDamaged,
    // Hot Cargo (spec 413)
    HeatStageAtLeast, HuntersInSystem, StrikeWithin, LossTimerRunning, CratesWithin, EnemyHoldsTech, EnemyCalling,
    // Autonomous ships (spec 1066)
    CargoAbove, CargoToSell, LootWithin, StationInRange, AsteroidWithin, WreckWithin, FleetBeyond,
    // Formations (spec 423, 424)
    AllyWithin, AllyInLineOfFire,
}

/// <summary>Spec 402, 414. A tactic action. ShipAiModel.KindOf gives its kind from its place in this list.</summary>
public enum Act
{
    // Target
    TargetNearest, TargetWeakest, TargetStrongest, TargetAllyAttacker, TargetVaultAttacker, TargetTechHolder, TargetCaller,
    // Move
    Kite, CloseIn, HoldPosition, RetreatToAlly, RetreatRepair, FleeJump, FollowLeader, Guard, CollectTech,
    CollectLoot, GoToStation, Mine, Salvage, ReturnToFleet,
    // Formations (spec 425, 426): the parameter is the spacing.
    HoldFormation, KeepSpacing,
    // Weapons (the parameter is the weapon group: 0 = all weapons, 1 to 10 = part control group)
    FireAtWill, FocusTarget, TargetWeapons, TargetEngines, PointDefenseOnly, HoldFire, FireDisruptors,
    // Facing (the parameter is the side that faces the target)
    Face,
    // Trade (spec 1067, 1073, 1074)
    Sell, Buy, Unload,
}

public enum ActKind { Target, Move, Weapons, Facing, Trade }

/// <summary>What the parameter of a condition or an action means.</summary>
public enum ParamKind { None, Percent, Distance, Count, PartKind, ShipClass, Group, GuardWho, Stage, Seconds, Side, Station }

public enum PartKind { Weapon, Thruster, Shield, Vault }

/// <summary>Small and Large are relative to the ship's own blueprint cost. Station = a ship without thrusters.</summary>
public enum ShipClass { Small, Large, Station }

public enum GuardWho { Flagship, VaultShip, WeakestAlly }

/// <summary>Spec 415. The side of the ship that faces the target.</summary>
public enum Side { Nose, BroadsideLeft, BroadsideRight, Stern }

/// <summary>Spec 1069. Which trading station "go to station" picks.</summary>
public enum StationPick { Nearest, BestPrice, Cheapest }

public enum Preset { Aggressive, Defensive, Sniper, Escort, VaultGuard, Looter, Miner, SupplyRunner, Salvager, EscortDrone, Formation }

/// <summary>
/// One condition term of a row (spec 416). Not inverts it. Or joins it to the previous term with OR, else with AND.
/// Or of the first term means nothing.
/// </summary>
public readonly record struct Term(Cond Cond, int Param, bool Not = false, bool Or = false);

/// <summary>One "conditions → action" row. Params mean what ShipAiModel.ParamOf says. Terms is never empty; nobody changes it.</summary>
public sealed record TacticRow(Term[] Terms, Act Act, int ActParam)
{
    public TacticRow(Cond cond, int condParam, Act act, int actParam) : this([new Term(cond, condParam)], act, actParam) { }

    public bool Equals(TacticRow? other) =>
        other is not null && Act == other.Act && ActParam == other.ActParam && Terms.AsSpan().SequenceEqual(other.Terms);

    public override int GetHashCode() => HashCode.Combine(Act, ActParam, Terms.Length, Terms.Length > 0 ? Terms[0] : default);
}

/// <summary>
/// The facts that conditions read, gathered from the sim each AI tick.
/// Fractions are 0 to 1; null means the ship has none of that thing (no shields, no battery storage).
/// NearestEnemy and NearestCrate are edge distances, or +infinity with none. StrikeIn is the game seconds to the next Strike wave,
/// or null outside the Strike stage. Spec 1066: Cargo is null without storage bays; NearestLoot, NearestAsteroid, NearestWreck and
/// NearestFleet are edge distances, or +infinity with none.
/// </summary>
public sealed record ShipFacts(
    float Health,
    float? Shields,
    IReadOnlySet<PartKind> LostParts,
    float NearestEnemy,
    bool TargetedByEnemy,
    bool AllyUnderAttack,
    float? Ammo,
    float? Battery,
    IReadOnlySet<ShipClass> EnemyClasses,
    int Enemies,
    bool CarryingTech,
    bool VaultDamaged,
    int HeatStage = 0,
    bool HuntersInSystem = false,
    float? StrikeIn = null,
    bool LossTimerRunning = false,
    float NearestCrate = float.PositiveInfinity,
    bool EnemyHoldsTech = false,
    bool EnemyCalling = false,
    float? Cargo = null,
    bool CargoToSell = false,
    float NearestLoot = float.PositiveInfinity,
    bool StationInRange = false,
    float NearestAsteroid = float.PositiveInfinity,
    float NearestWreck = float.PositiveInfinity,
    float NearestFleet = float.PositiveInfinity,
    float NearestAlly = float.PositiveInfinity,
    bool AllyInLineOfFire = false);

/// <summary>The last value of one term and the game second it took that value (spec 418). Rows keep one per term, in term order.</summary>
public readonly record struct TermMemory(bool On, double Since);

/// <summary>
/// Spec 418. Margin: a threshold term that holds keeps holding until its value passes the threshold by this much
/// (percent terms: points × 100, distance and seconds terms: a fraction of the threshold). MinHoldSeconds: a threshold term keeps
/// a new value at least this long.
/// </summary>
public readonly record struct Hysteresis(float Margin, double MinHoldSeconds)
{
    public static readonly Hysteresis None = new(0f, 0);
}

/// <summary>
/// Result of one evaluation (spec 403, 1067). TargetRow, MoveRow, FacingRow and TradeRow are row indexes or -1.
/// Matched holds, for each row, whether its conditions held and its action could run. Weapon rows resolve per weapon (WeaponAct).
/// Memory is the new term memory of all rows, in term order.
/// </summary>
public sealed record Decision(int TargetRow, int MoveRow, int FacingRow, bool[] Matched, TermMemory[] Memory, int TradeRow = -1)
{
    public static readonly Decision None = new(-1, -1, -1, [], []);
}

/// <summary>
/// Player ship tactics, Dragon Age style (spec 400 to 439). Pure: no game types, so the unit tests cover it.
/// </summary>
public static class ShipAiModel
{
    public const int MaxRows = 16;
    public const int MaxTerms = 8;
    public const int MaxGroup = 10;
    /// <summary>
    /// The highest stage that stored tactics text may name. The real limit is the number of heat stages (Heat.Stages), which the
    /// tactics input checks (StageError). Stored text keeps this wider bound, so a save or design still loads after the stage table
    /// gets shorter; such a term never holds.
    /// </summary>
    public const int MaxStage = 10;
    public const string Prefix = "HC1:";

    public static ActKind KindOf(Act act) => act switch
    {
        <= Act.TargetCaller => ActKind.Target,
        <= Act.KeepSpacing => ActKind.Move,
        <= Act.FireDisruptors => ActKind.Weapons,
        Act.Face => ActKind.Facing,
        _ => ActKind.Trade,
    };

    public static ParamKind ParamOf(Cond cond) => cond switch
    {
        Cond.HealthBelow or Cond.ShieldsBelow or Cond.AmmoBelow or Cond.BatteryBelow or Cond.CargoAbove => ParamKind.Percent,
        Cond.EnemyWithin or Cond.EnemyBeyond or Cond.CratesWithin or Cond.LootWithin or Cond.AsteroidWithin or Cond.WreckWithin
            or Cond.FleetBeyond or Cond.AllyWithin => ParamKind.Distance,
        Cond.EnemiesAtLeast => ParamKind.Count,
        Cond.PartLost => ParamKind.PartKind,
        Cond.EnemyClassNear => ParamKind.ShipClass,
        Cond.HeatStageAtLeast => ParamKind.Stage,
        Cond.StrikeWithin => ParamKind.Seconds,
        _ => ParamKind.None,
    };

    public static ParamKind ParamOf(Act act) => act switch
    {
        Act.Guard => ParamKind.GuardWho,
        Act.Face => ParamKind.Side,
        Act.GoToStation => ParamKind.Station,
        Act.HoldFormation or Act.KeepSpacing => ParamKind.Distance,
        _ when KindOf(act) == ActKind.Weapons => ParamKind.Group,
        _ => ParamKind.None,
    };

    /// <summary>Spec 418: terms that compare a measured value with a threshold get hysteresis.</summary>
    public static bool IsThreshold(ParamKind kind) => kind is ParamKind.Percent or ParamKind.Distance or ParamKind.Seconds;

    /// <summary>Allowed parameter range for each kind. A decoded value outside it is an error.</summary>
    public static (int Min, int Max) RangeOf(ParamKind kind) => kind switch
    {
        ParamKind.None => (0, 0),
        ParamKind.Percent => (0, 100),
        ParamKind.Distance => (0, 20000),
        ParamKind.Count => (1, 50),
        ParamKind.PartKind => (0, Enum.GetValues<PartKind>().Length - 1),
        ParamKind.ShipClass => (0, Enum.GetValues<ShipClass>().Length - 1),
        ParamKind.Group => (0, MaxGroup),
        ParamKind.GuardWho => (0, Enum.GetValues<GuardWho>().Length - 1),
        ParamKind.Stage => (1, MaxStage),
        ParamKind.Seconds => (0, 3600),
        ParamKind.Side => (0, Enum.GetValues<Side>().Length - 1),
        ParamKind.Station => (0, Enum.GetValues<StationPick>().Length - 1),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// The values that the editor offers for a parameter. stages: the number of heat stages (Heat.Stages). A row with another value
    /// (pasted text) shows it too.
    /// </summary>
    public static int[] ChoicesOf(ParamKind kind, int stages) => kind switch
    {
        ParamKind.Percent => [5, 10, 20, 25, 30, 40, 50, 60, 75, 90, 95],
        ParamKind.Distance => [100, 150, 200, 300, 500, 800, 1000, 1500, 2000, 3000, 5000, 10000, 20000],
        ParamKind.Count => [1, 2, 3, 4, 5, 6, 8],
        ParamKind.Stage => Enumerable.Range(1, Math.Clamp(stages, 1, MaxStage)).ToArray(),
        ParamKind.Seconds => [10, 20, 30, 60, 90, 120],
        _ => Enumerable.Range(RangeOf(kind).Min, RangeOf(kind).Max - RangeOf(kind).Min + 1).ToArray(),
    };

    /// <summary>Spec 413: an error for a "heat stage at least" term above the number of heat stages, or null when all fit.</summary>
    public static string? StageError(IEnumerable<TacticRow> rows, int stages)
    {
        foreach (var row in rows)
            foreach (var t in row.Terms)
                if (t.Cond == Cond.HeatStageAtLeast && t.Param > stages)
                    return $"HeatStageAtLeast={t.Param}: the run has {stages} heat stages";
        return null;
    }

    /// <summary>The parameter that a new term or action (or a changed condition or action) starts with.</summary>
    public static int DefaultParam(ParamKind kind) => kind switch
    {
        ParamKind.Percent => 30,
        ParamKind.Distance => 1000,
        ParamKind.Count => 2,
        ParamKind.Stage => 1,
        ParamKind.Seconds => 30,
        _ => 0,
    };

    /// <summary>Whether a term's condition holds, without Not and without hysteresis.</summary>
    public static bool Holds(Term term, ShipFacts f) => Holds(term, f, wasOn: false, margin: 0f);

    /// <summary>The condition of a term, without Not. A threshold term that was on gets the margin in its favor (spec 418).</summary>
    static bool Holds(Term term, ShipFacts f, bool wasOn, float margin)
    {
        float m = wasOn ? margin : 0f;
        float p = term.Param;
        bool Below(float? fraction) => fraction is { } v && v * 100f < p + m * 100f;
        bool Above(float? fraction) => fraction is { } v && v * 100f > p - m * 100f;
        return term.Cond switch
        {
            Cond.Always => true,
            Cond.HealthBelow => Below(f.Health),
            Cond.ShieldsBelow => Below(f.Shields),
            Cond.PartLost => f.LostParts.Contains((PartKind)term.Param),
            Cond.EnemyWithin => f.NearestEnemy <= p * (1f + m),
            Cond.EnemyBeyond => f.NearestEnemy > p * (1f - m),
            Cond.TargetedByEnemy => f.TargetedByEnemy,
            Cond.AllyUnderAttack => f.AllyUnderAttack,
            Cond.AmmoBelow => Below(f.Ammo),
            Cond.BatteryBelow => Below(f.Battery),
            Cond.EnemyClassNear => f.EnemyClasses.Contains((ShipClass)term.Param),
            Cond.EnemiesAtLeast => f.Enemies >= term.Param,
            Cond.CarryingTech => f.CarryingTech,
            Cond.VaultDamaged => f.VaultDamaged,
            Cond.HeatStageAtLeast => f.HeatStage >= term.Param,
            Cond.HuntersInSystem => f.HuntersInSystem,
            Cond.StrikeWithin => f.StrikeIn is { } s && s <= p * (1f + m),
            Cond.LossTimerRunning => f.LossTimerRunning,
            Cond.CratesWithin => f.NearestCrate <= p * (1f + m),
            Cond.EnemyHoldsTech => f.EnemyHoldsTech,
            Cond.EnemyCalling => f.EnemyCalling,
            Cond.CargoAbove => Above(f.Cargo),
            Cond.CargoToSell => f.CargoToSell,
            Cond.LootWithin => f.NearestLoot <= p * (1f + m),
            Cond.StationInRange => f.StationInRange,
            Cond.AsteroidWithin => f.NearestAsteroid <= p * (1f + m),
            Cond.WreckWithin => f.NearestWreck <= p * (1f + m),
            Cond.FleetBeyond => f.NearestFleet > p * (1f - m),
            Cond.AllyWithin => f.NearestAlly <= p * (1f + m),
            Cond.AllyInLineOfFire => f.AllyInLineOfFire,
            _ => throw new ArgumentOutOfRangeException(nameof(term), term.Cond, null),
        };
    }

    /// <summary>
    /// Spec 416: the terms of a row joined by AND and OR, AND first: "A AND B OR C AND D" = (A and B) or (C and D).
    /// Every term is evaluated, so each one updates its memory. memory: the terms' old memory (or null), start: its first index.
    /// The new memory goes to output from start on.
    /// </summary>
    static bool RowHolds(TacticRow row, ShipFacts f, IReadOnlyList<TermMemory>? memory, int start, double now, Hysteresis h, TermMemory[] output)
    {
        bool any = false, group = true;
        for (int j = 0; j < row.Terms.Length; j++)
        {
            var term = row.Terms[j];
            TermMemory? old = memory is not null && start + j < memory.Count ? memory[start + j] : null;
            bool threshold = IsThreshold(ParamOf(term.Cond));
            bool raw = Holds(term, f, old?.On == true && threshold, h.Margin);
            TermMemory next;
            if (old is not { } o) next = new TermMemory(raw, now);
            else if (raw == o.On) next = o;
            // Spec 418: a threshold term keeps a new value at least MinHoldSeconds.
            else if (threshold && now - o.Since < h.MinHoldSeconds) next = o;
            else next = new TermMemory(raw, now);
            output[start + j] = next;
            if (j > 0 && term.Or)
            {
                any |= group;
                group = true;
            }
            group &= next.On != term.Not;
        }
        return any | group;
    }

    /// <summary>Evaluation without term memory and without hysteresis.</summary>
    public static Decision Evaluate(IReadOnlyList<TacticRow> rows, ShipFacts facts, Func<TacticRow, bool> canRun) =>
        Evaluate(rows, facts, canRun, null, 0, Hysteresis.None);

    /// <summary>
    /// Spec 403. Rows run top to bottom. A row matches when its conditions hold and canRun says its action can run now
    /// (for example, a target action needs a target). The first matching row of each kind wins: one target row, one move row,
    /// one facing row, and per weapon the first matching weapons row of its group (WeaponAct). canRun is called only for rows whose
    /// conditions hold. memory is the Memory of the last decision for the same rows, or null; a memory of another length is ignored.
    /// </summary>
    public static Decision Evaluate(IReadOnlyList<TacticRow> rows, ShipFacts facts, Func<TacticRow, bool> canRun,
        IReadOnlyList<TermMemory>? memory, double now, Hysteresis h)
    {
        int termCount = rows.Sum(r => r.Terms.Length);
        if (memory is not null && memory.Count != termCount) memory = null;
        var output = new TermMemory[termCount];
        var matched = new bool[rows.Count];
        int target = -1, move = -1, facing = -1, trade = -1, start = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            matched[i] = RowHolds(row, facts, memory, start, now, h, output) && canRun(row);
            start += row.Terms.Length;
            if (!matched[i]) continue;
            switch (KindOf(row.Act))
            {
                case ActKind.Target when target < 0: target = i; break;
                case ActKind.Move when move < 0: move = i; break;
                case ActKind.Facing when facing < 0: facing = i; break;
                case ActKind.Trade when trade < 0: trade = i; break;
            }
        }
        return new Decision(target, move, facing, matched, output, trade);
    }

    /// <summary>
    /// Spec 404, 414. The weapons action for one weapon: the first matched weapons row whose group is 0 (all weapons)
    /// or one of the weapon's part control groups. FireDisruptors applies only to disruptor weapons; other weapons skip it.
    /// FireAtWill when none matches.
    /// </summary>
    public static Act WeaponAct(IReadOnlyList<TacticRow> rows, IReadOnlyList<bool> matched, IReadOnlySet<int> groups, bool disruptor = false)
    {
        for (int i = 0; i < rows.Count && i < matched.Count; i++)
        {
            var row = rows[i];
            if (!matched[i] || KindOf(row.Act) != ActKind.Weapons || (row.Act == Act.FireDisruptors && !disruptor)) continue;
            if (row.ActParam == 0 || groups.Contains(row.ActParam)) return row.Act;
        }
        return Act.FireAtWill;
    }

    /// <summary>Weapons actions that give weapons an explicit part target on the target ship.</summary>
    public static bool AimsAtParts(Act act) => act is Act.FocusTarget or Act.TargetWeapons or Act.TargetEngines or Act.FireDisruptors;

    /// <summary>
    /// Spec 405, 417. Text form: "HC1:" then rows joined by ';', each "terms>Act[=n]". Terms: "[!]Cond[=n]" joined by '&amp;' (AND)
    /// or '|' (OR). A row with one plain term is the old form, so old text reads and writes the same. Empty text = no tactics.
    /// </summary>
    public static string Encode(IReadOnlyList<TacticRow> rows)
    {
        if (rows.Count == 0) return "";
        var sb = new StringBuilder(Prefix);
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (i > 0) sb.Append(';');
            for (int j = 0; j < r.Terms.Length; j++)
            {
                var t = r.Terms[j];
                if (j > 0) sb.Append(t.Or ? '|' : '&');
                if (t.Not) sb.Append('!');
                sb.Append(t.Cond);
                if (ParamOf(t.Cond) != ParamKind.None) sb.Append('=').Append(t.Param.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append('>').Append(r.Act);
            if (ParamOf(r.Act) != ParamKind.None) sb.Append('=').Append(r.ActParam.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>Parses Encode text. Throws FormatException on any unknown name, missing or out-of-range parameter, or too many rows or terms.</summary>
    public static List<TacticRow> Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) return [];
        if (!text.StartsWith(Prefix, StringComparison.Ordinal)) throw new FormatException($"tactics text must start with {Prefix}");
        var rows = new List<TacticRow>();
        foreach (string part in text[Prefix.Length..].Split(';'))
        {
            int arrow = part.IndexOf('>');
            if (arrow < 0) throw new FormatException($"tactic row '{part}' has no '>'");
            var (act, actParam) = ParseSide<Act>(part[(arrow + 1)..], ParamOf);
            rows.Add(new TacticRow(ParseTerms(part[..arrow]), act, actParam));
        }
        if (rows.Count > MaxRows) throw new FormatException($"tactics have {rows.Count} rows; at most {MaxRows}");
        return rows;
    }

    static Term[] ParseTerms(string side)
    {
        var terms = new List<Term>();
        int pos = 0;
        bool or = false;
        while (true)
        {
            int end = side.IndexOfAny(['&', '|'], pos);
            string s = end < 0 ? side[pos..] : side[pos..end];
            bool not = s.StartsWith('!');
            var (cond, param) = ParseSide<Cond>(not ? s[1..] : s, ParamOf);
            terms.Add(new Term(cond, param, not, or));
            if (end < 0) break;
            or = side[end] == '|';
            pos = end + 1;
        }
        if (terms.Count > MaxTerms) throw new FormatException($"a row has {terms.Count} conditions; at most {MaxTerms}");
        return terms.ToArray();
    }

    static (T Value, int Param) ParseSide<T>(string side, Func<T, ParamKind> paramOf) where T : struct, Enum
    {
        int eq = side.IndexOf('=');
        string name = eq < 0 ? side : side[..eq];
        // Enum.TryParse also takes numbers; only names are valid text.
        if (name.Length == 0 || char.IsDigit(name[0]) || !Enum.TryParse(name, ignoreCase: false, out T value) || !Enum.IsDefined(value))
            throw new FormatException($"unknown {typeof(T).Name} '{name}'");
        var kind = paramOf(value);
        if (kind == ParamKind.None)
        {
            if (eq >= 0) throw new FormatException($"{name} takes no parameter");
            return (value, 0);
        }
        if (eq < 0 || !int.TryParse(side[(eq + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int param))
            throw new FormatException($"{name} needs a number parameter");
        var (min, max) = RangeOf(kind);
        if (param < min || param > max) throw new FormatException($"{name}={param} is outside {min} to {max}");
        return (value, param);
    }

    /// <summary>Spec 406. Starting points for the editor. Each one is a plain list of rows that the player can change.</summary>
    public static IReadOnlyList<TacticRow> PresetRows(Preset preset) => preset switch
    {
        Preset.Aggressive =>
        [
            new(Cond.HealthBelow, 20, Act.RetreatRepair, 0),
            new(Cond.Always, 0, Act.TargetWeakest, 0),
            new(Cond.Always, 0, Act.CloseIn, 0),
            new(Cond.Always, 0, Act.FocusTarget, 0),
        ],
        Preset.Defensive =>
        [
            new(Cond.HealthBelow, 40, Act.RetreatToAlly, 0),
            new(Cond.AllyUnderAttack, 0, Act.TargetAllyAttacker, 0),
            new(Cond.EnemyWithin, 1500, Act.TargetNearest, 0),
            new(Cond.EnemyBeyond, 1500, Act.FollowLeader, 0),
        ],
        Preset.Sniper =>
        [
            new(Cond.HealthBelow, 30, Act.RetreatRepair, 0),
            new(Cond.Always, 0, Act.TargetNearest, 0),
            new(Cond.Always, 0, Act.Kite, 0),
            new(Cond.Always, 0, Act.TargetWeapons, 0),
        ],
        Preset.Escort =>
        [
            new(Cond.HealthBelow, 25, Act.RetreatToAlly, 0),
            new(Cond.AllyUnderAttack, 0, Act.TargetAllyAttacker, 0),
            new(Cond.Always, 0, Act.Guard, (int)GuardWho.Flagship),
        ],
        Preset.VaultGuard =>
        [
            new(Cond.HealthBelow, 20, Act.RetreatRepair, 0),
            new(Cond.Always, 0, Act.TargetVaultAttacker, 0),
            new(Cond.EnemyWithin, 1200, Act.TargetNearest, 0),
            new(Cond.Always, 0, Act.Guard, (int)GuardWho.VaultShip),
        ],
        // Spec 1076 to 1080: autonomous ship scenarios.
        Preset.Looter =>
        [
            new(Cond.CargoAbove, 95, Act.GoToStation, (int)StationPick.Nearest),
            new([new Term(Cond.StationInRange, 0), new Term(Cond.CargoToSell, 0)], Act.Sell, 0),
            new(Cond.LootWithin, 3000, Act.CollectLoot, 0),
            new(Cond.Always, 0, Act.ReturnToFleet, 0),
        ],
        Preset.Miner =>
        [
            new(Cond.CargoAbove, 95, Act.GoToStation, (int)StationPick.Nearest),
            new([new Term(Cond.StationInRange, 0), new Term(Cond.CargoToSell, 0)], Act.Sell, 0),
            new(Cond.LootWithin, 600, Act.CollectLoot, 0),
            new(Cond.AsteroidWithin, 20000, Act.Mine, 0),
            new(Cond.Always, 0, Act.ReturnToFleet, 0),
        ],
        // Buys the buy list where it is cheapest, then unloads all cargo into the owner's first crewed ship.
        Preset.SupplyRunner =>
        [
            new(Cond.CargoAbove, 90, Act.ReturnToFleet, 0),
            new(Cond.Always, 0, Act.GoToStation, (int)StationPick.Cheapest),
            new([new Term(Cond.FleetBeyond, 300, Not: true)], Act.Unload, 0),
            new(Cond.StationInRange, 0, Act.Buy, 0),
        ],
        Preset.Salvager =>
        [
            new(Cond.CargoAbove, 95, Act.GoToStation, (int)StationPick.Nearest),
            new([new Term(Cond.StationInRange, 0), new Term(Cond.CargoToSell, 0)], Act.Sell, 0),
            new(Cond.LootWithin, 3000, Act.CollectLoot, 0),
            new(Cond.WreckWithin, 3000, Act.Salvage, 0),
            new(Cond.Always, 0, Act.ReturnToFleet, 0),
        ],
        Preset.EscortDrone =>
        [
            new(Cond.HealthBelow, 40, Act.ReturnToFleet, 0),
            new(Cond.AllyUnderAttack, 0, Act.TargetAllyAttacker, 0),
            new(Cond.EnemyWithin, 1500, Act.TargetNearest, 0),
            new(Cond.Always, 0, Act.Guard, (int)GuardWho.Flagship),
        ],
        // Spec 427: no friendly fire, no ramming, a V behind the first ship.
        Preset.Formation =>
        [
            new(Cond.AllyInLineOfFire, 0, Act.HoldFire, 0),
            new(Cond.HealthBelow, 25, Act.RetreatToAlly, 0),
            new(Cond.EnemyWithin, 1500, Act.TargetNearest, 0),
            new(Cond.Always, 0, Act.KeepSpacing, 150),
            new(Cond.Always, 0, Act.HoldFormation, 300),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
    };

    /// <summary>A term with a new condition keeps Not and its connector. The parameter resets when its kind changes.</summary>
    public static Term WithCond(Term term, Cond cond) =>
        term with { Cond = cond, Param = ParamOf(cond) == ParamOf(term.Cond) ? term.Param : DefaultParam(ParamOf(cond)) };

    public static TacticRow WithAct(TacticRow row, Act act) =>
        row with { Act = act, ActParam = ParamOf(act) == ParamOf(row.Act) ? row.ActParam : DefaultParam(ParamOf(act)) };

    /// <summary>The row with term j replaced.</summary>
    public static TacticRow WithTerm(TacticRow row, int j, Term term)
    {
        var terms = (Term[])row.Terms.Clone();
        terms[j] = term;
        return row with { Terms = terms };
    }

    /// <summary>Spec 419: "+ condition" adds an Always term joined by AND. A row holds at most MaxTerms.</summary>
    public static TacticRow AddTerm(TacticRow row) =>
        row.Terms.Length >= MaxTerms ? row : row with { Terms = [.. row.Terms, new Term(Cond.Always, 0)] };

    /// <summary>Removes term j. The last term stays, so a row always has a condition.</summary>
    public static TacticRow RemoveTerm(TacticRow row, int j) =>
        row.Terms.Length <= 1 ? row : row with { Terms = row.Terms.Where((_, k) => k != j).ToArray() };

    // ---- Personal presets (spec 422): a local file, one "name TAB text" line per preset.

    public const int MaxPresetName = 32;

    /// <summary>A preset name without tabs and line breaks, trimmed and cut to MaxPresetName. Empty when nothing is left.</summary>
    public static string CleanPresetName(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name) sb.Append(char.IsControl(c) ? ' ' : c);
        string clean = sb.ToString().Trim();
        return clean.Length > MaxPresetName ? clean[..MaxPresetName].TrimEnd() : clean;
    }

    public static string FormatPresets(IEnumerable<(string Name, string Text)> presets) =>
        string.Join("\n", presets.Select(p => CleanPresetName(p.Name) + "\t" + p.Text));

    /// <summary>Reads the preset file. A line without a name, without a tab or with bad tactics text is skipped and counted.</summary>
    public static (List<(string Name, string Text)> Presets, int Skipped) ParsePresets(string file)
    {
        var presets = new List<(string Name, string Text)>();
        int skipped = 0;
        foreach (string raw in file.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            int tab = line.IndexOf('\t');
            string name = tab < 0 ? "" : CleanPresetName(line[..tab]);
            string text = tab < 0 ? "" : line[(tab + 1)..].Trim();
            try
            {
                if (name.Length == 0 || text.Length == 0) throw new FormatException("no name or no rows");
                Decode(text);
                presets.Add((name, text));
            }
            catch (FormatException) { skipped++; }
        }
        return (presets, skipped);
    }

    /// <summary>Stable hash of a text (string.GetHashCode differs between processes), for the MP integrity hash and autotests.</summary>
    public static int StableHash(string text)
    {
        int h = 17;
        foreach (char c in text) h = unchecked(h * 31 + c);
        return h;
    }
}

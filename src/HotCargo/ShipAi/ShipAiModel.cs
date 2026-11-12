using System.Globalization;
using System.Text;

namespace HotCargo.ShipAi;

/// <summary>Spec 401. A tactic condition. Each one reads one fact about the ship (ShipFacts).</summary>
public enum Cond
{
    Always, HealthBelow, ShieldsBelow, PartLost, EnemyWithin, EnemyBeyond, TargetedByEnemy, AllyUnderAttack,
    AmmoBelow, BatteryBelow, EnemyClassNear, EnemiesAtLeast, CarryingTech, VaultDamaged,
}

/// <summary>Spec 402. A tactic action. ShipAiModel.KindOf gives its kind.</summary>
public enum Act
{
    // Target
    TargetNearest, TargetWeakest, TargetStrongest, TargetAllyAttacker, TargetVaultAttacker,
    // Move
    Kite, CloseIn, HoldPosition, RetreatToAlly, RetreatRepair, FleeJump, FollowLeader, Guard,
    // Weapons (the parameter is the weapon group: 0 = all weapons, 1 to 10 = part control group)
    FireAtWill, FocusTarget, TargetWeapons, TargetEngines, PointDefenseOnly, HoldFire,
}

public enum ActKind { Target, Move, Weapons }

/// <summary>What the parameter of a condition or an action means.</summary>
public enum ParamKind { None, Percent, Distance, Count, PartKind, ShipClass, Group, GuardWho }

public enum PartKind { Weapon, Thruster, Shield, Vault }

/// <summary>Small and Large are relative to the ship's own blueprint cost. Station = a ship without thrusters.</summary>
public enum ShipClass { Small, Large, Station }

public enum GuardWho { Flagship, VaultShip, WeakestAlly }

public enum Preset { Aggressive, Defensive, Sniper, Escort, VaultGuard }

/// <summary>One "condition → action" row. Params mean what ShipAiModel.ParamOf says.</summary>
public readonly record struct TacticRow(Cond Cond, int CondParam, Act Act, int ActParam);

/// <summary>
/// The facts that conditions read, gathered from the sim each AI tick.
/// Fractions are 0 to 1; null means the ship has none of that thing (no shields, no battery storage).
/// NearestEnemy is the edge distance to the nearest enemy on radar, or +infinity with no enemy.
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
    bool VaultDamaged);

/// <summary>
/// Result of one evaluation (spec 403). TargetRow and MoveRow are row indexes or -1.
/// Matched holds, for each row, whether its condition held and its action could run. Weapon rows resolve per weapon (WeaponAct).
/// </summary>
public sealed record Decision(int TargetRow, int MoveRow, bool[] Matched)
{
    public static readonly Decision None = new(-1, -1, []);
}

/// <summary>
/// Player ship tactics, Dragon Age style (spec 400 to 412). Pure: no game types, so the unit tests cover it.
/// </summary>
public static class ShipAiModel
{
    public const int MaxRows = 16;
    public const int MaxGroup = 10;
    public const string Prefix = "HC1:";

    public static ActKind KindOf(Act act) => act switch
    {
        <= Act.TargetVaultAttacker => ActKind.Target,
        <= Act.Guard => ActKind.Move,
        _ => ActKind.Weapons,
    };

    public static ParamKind ParamOf(Cond cond) => cond switch
    {
        Cond.HealthBelow or Cond.ShieldsBelow or Cond.AmmoBelow or Cond.BatteryBelow => ParamKind.Percent,
        Cond.EnemyWithin or Cond.EnemyBeyond => ParamKind.Distance,
        Cond.EnemiesAtLeast => ParamKind.Count,
        Cond.PartLost => ParamKind.PartKind,
        Cond.EnemyClassNear => ParamKind.ShipClass,
        _ => ParamKind.None,
    };

    public static ParamKind ParamOf(Act act) => act switch
    {
        Act.Guard => ParamKind.GuardWho,
        _ when KindOf(act) == ActKind.Weapons => ParamKind.Group,
        _ => ParamKind.None,
    };

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
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>The values that the editor offers for a parameter. A row with another value (pasted text) shows it too.</summary>
    public static int[] ChoicesOf(ParamKind kind) => kind switch
    {
        ParamKind.Percent => [10, 20, 25, 30, 40, 50, 60, 75, 90],
        ParamKind.Distance => [300, 500, 800, 1000, 1500, 2000, 3000, 5000],
        ParamKind.Count => [1, 2, 3, 4, 5, 6, 8],
        _ => Enumerable.Range(RangeOf(kind).Min, RangeOf(kind).Max - RangeOf(kind).Min + 1).ToArray(),
    };

    /// <summary>The parameter that a new row (or a changed condition or action) starts with.</summary>
    public static int DefaultParam(ParamKind kind) => kind switch
    {
        ParamKind.Percent => 30,
        ParamKind.Distance => 1000,
        ParamKind.Count => 2,
        _ => 0,
    };

    public static bool Holds(TacticRow row, ShipFacts f) => row.Cond switch
    {
        Cond.Always => true,
        Cond.HealthBelow => f.Health * 100f < row.CondParam,
        Cond.ShieldsBelow => f.Shields is { } s && s * 100f < row.CondParam,
        Cond.PartLost => f.LostParts.Contains((PartKind)row.CondParam),
        Cond.EnemyWithin => f.NearestEnemy <= row.CondParam,
        Cond.EnemyBeyond => f.NearestEnemy > row.CondParam,
        Cond.TargetedByEnemy => f.TargetedByEnemy,
        Cond.AllyUnderAttack => f.AllyUnderAttack,
        Cond.AmmoBelow => f.Ammo is { } a && a * 100f < row.CondParam,
        Cond.BatteryBelow => f.Battery is { } b && b * 100f < row.CondParam,
        Cond.EnemyClassNear => f.EnemyClasses.Contains((ShipClass)row.CondParam),
        Cond.EnemiesAtLeast => f.Enemies >= row.CondParam,
        Cond.CarryingTech => f.CarryingTech,
        Cond.VaultDamaged => f.VaultDamaged,
        _ => throw new ArgumentOutOfRangeException(nameof(row), row.Cond, null),
    };

    /// <summary>
    /// Spec 403. Rows run top to bottom. A row matches when its condition holds and canRun says its action can run now
    /// (for example, a target action needs a target). The first matching row of each kind wins: one target row, one move row,
    /// and per weapon the first matching weapons row of its group (WeaponAct). canRun is called only for rows whose condition holds.
    /// </summary>
    public static Decision Evaluate(IReadOnlyList<TacticRow> rows, ShipFacts facts, Func<TacticRow, bool> canRun)
    {
        var matched = new bool[rows.Count];
        int target = -1, move = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            matched[i] = Holds(row, facts) && canRun(row);
            if (!matched[i]) continue;
            switch (KindOf(row.Act))
            {
                case ActKind.Target when target < 0: target = i; break;
                case ActKind.Move when move < 0: move = i; break;
            }
        }
        return new Decision(target, move, matched);
    }

    /// <summary>
    /// Spec 404. The weapons action for one weapon: the first matched weapons row whose group is 0 (all weapons)
    /// or one of the weapon's part control groups. FireAtWill when none matches.
    /// </summary>
    public static Act WeaponAct(IReadOnlyList<TacticRow> rows, IReadOnlyList<bool> matched, IReadOnlySet<int> groups)
    {
        for (int i = 0; i < rows.Count && i < matched.Count; i++)
        {
            var row = rows[i];
            if (matched[i] && KindOf(row.Act) == ActKind.Weapons && (row.ActParam == 0 || groups.Contains(row.ActParam))) return row.Act;
        }
        return Act.FireAtWill;
    }

    /// <summary>Weapons actions that give weapons an explicit part target on the target ship.</summary>
    public static bool AimsAtParts(Act act) => act is Act.FocusTarget or Act.TargetWeapons or Act.TargetEngines;

    /// <summary>Spec 405. Text form: "HC1:" then rows joined by ';', each "Cond[=n]>Act[=n]". Empty text = no tactics.</summary>
    public static string Encode(IReadOnlyList<TacticRow> rows)
    {
        if (rows.Count == 0) return "";
        var sb = new StringBuilder(Prefix);
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (i > 0) sb.Append(';');
            sb.Append(r.Cond);
            if (ParamOf(r.Cond) != ParamKind.None) sb.Append('=').Append(r.CondParam.ToString(CultureInfo.InvariantCulture));
            sb.Append('>').Append(r.Act);
            if (ParamOf(r.Act) != ParamKind.None) sb.Append('=').Append(r.ActParam.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>Parses Encode text. Throws FormatException on any unknown name, missing or out-of-range parameter, or too many rows.</summary>
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
            var (cond, condParam) = ParseSide<Cond>(part[..arrow], ParamOf);
            var (act, actParam) = ParseSide<Act>(part[(arrow + 1)..], ParamOf);
            rows.Add(new TacticRow(cond, condParam, act, actParam));
        }
        if (rows.Count > MaxRows) throw new FormatException($"tactics have {rows.Count} rows; at most {MaxRows}");
        return rows;
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
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
    };

    /// <summary>A row with a new condition keeps its action. The parameter resets when its kind changes.</summary>
    public static TacticRow WithCond(TacticRow row, Cond cond) =>
        row with { Cond = cond, CondParam = ParamOf(cond) == ParamOf(row.Cond) ? row.CondParam : DefaultParam(ParamOf(cond)) };

    public static TacticRow WithAct(TacticRow row, Act act) =>
        row with { Act = act, ActParam = ParamOf(act) == ParamOf(row.Act) ? row.ActParam : DefaultParam(ParamOf(act)) };

    /// <summary>Stable hash of a text (string.GetHashCode differs between processes), for the MP integrity hash and autotests.</summary>
    public static int StableHash(string text)
    {
        int h = 17;
        foreach (char c in text) h = unchecked(h * 31 + c);
        return h;
    }
}

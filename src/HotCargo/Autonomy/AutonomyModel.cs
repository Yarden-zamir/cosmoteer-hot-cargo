using System.Globalization;
using System.Text;

namespace HotCargo.Autonomy;

/// <summary>A tile on a ship grid (x right, y down).</summary>
public readonly record struct Cell(int X, int Y);

/// <summary>A part rectangle on a ship grid: its top left tile and its size after rotation.</summary>
public readonly record struct Box(int X, int Y, int W, int H)
{
    public bool Contains(Cell c) => c.X >= X && c.X < X + W && c.Y >= Y && c.Y < Y + H;
}

/// <summary>Spec 1055. The Funnel part setting, in the order of its vanilla toggle modes.</summary>
public enum FunnelChoice { ShipFilter, All, Value50, Value200, Value1000, Off }

/// <summary>
/// Spec 1062. The cargo settings of one ship. LootTypes not empty: the loot filter takes only these types; else it takes loot of
/// unit value LootMinValue or more. Empty Sell: sell all cargo. Empty Mine: any ore. Empty Buy: buy nothing.
/// </summary>
public sealed record CargoSettings(int LootMinValue, string[] LootTypes, string[] Sell, string[] Mine, string[] Buy)
{
    public static readonly CargoSettings Default = new(0, [], [], [], []);

    public bool Equals(CargoSettings? o) => o is not null && LootMinValue == o.LootMinValue && LootTypes.SequenceEqual(o.LootTypes)
        && Sell.SequenceEqual(o.Sell) && Mine.SequenceEqual(o.Mine) && Buy.SequenceEqual(o.Buy);

    public override int GetHashCode() => HashCode.Combine(LootMinValue, LootTypes.Length, Sell.Length, Mine.Length, Buy.Length);

    public bool LootAccepts(string type, int unitValue) => LootTypes.Length > 0 ? LootTypes.Contains(type) : unitValue >= LootMinValue;

    public bool Sells(string type) => Sell.Length == 0 || Sell.Contains(type);

    public bool Mines(string ore) => Mine.Length == 0 || Mine.Contains(ore);
}

/// <summary>Autonomous ships (spec 1050 to 1099). Pure: no game types, so the unit tests cover it.</summary>
public static class AutonomyModel
{
    public const string CargoPrefix = "HCC1:";

    /// <summary>The value steps that the editor offers for the loot filter. A pasted other value shows too.</summary>
    public static readonly int[] LootValueChoices = [0, 20, 50, 100, 200, 500, 1000, 2000];

    /// <summary>
    /// Spec 1063. "HCC1:loot=v50|sell=iron,copper|mine=|buy=". The loot part is "v" and a value, or resource IDs.
    /// The default settings encode as empty text.
    /// </summary>
    public static string Encode(CargoSettings c)
    {
        if (c.Equals(CargoSettings.Default)) return "";
        var sb = new StringBuilder(CargoPrefix);
        sb.Append("loot=").Append(c.LootTypes.Length > 0 ? string.Join(',', c.LootTypes) : "v" + c.LootMinValue.ToString(CultureInfo.InvariantCulture));
        sb.Append("|sell=").Append(string.Join(',', c.Sell));
        sb.Append("|mine=").Append(string.Join(',', c.Mine));
        sb.Append("|buy=").Append(string.Join(',', c.Buy));
        return sb.ToString();
    }

    /// <summary>
    /// Parses Encode text. isResource says whether an ID names a resource that the list may hold; any other ID, a missing part,
    /// a bad value or a repeated ID throws FormatException. Lists keep their order.
    /// </summary>
    public static CargoSettings Decode(string text, Func<string, bool> isResource)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) return CargoSettings.Default;
        if (!text.StartsWith(CargoPrefix, StringComparison.Ordinal)) throw new FormatException($"cargo text must start with {CargoPrefix}");
        var parts = new Dictionary<string, string>();
        foreach (string part in text[CargoPrefix.Length..].Split('|'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) throw new FormatException($"cargo part '{part}' has no name");
            if (!parts.TryAdd(part[..eq], part[(eq + 1)..])) throw new FormatException($"cargo part '{part[..eq]}' repeats");
        }
        string[] names = ["loot", "sell", "mine", "buy"];
        if (parts.Count != names.Length || names.Any(n => !parts.ContainsKey(n)))
            throw new FormatException($"cargo text needs exactly {string.Join(", ", names)}");
        string[] List(string name)
        {
            string v = parts[name];
            if (v.Length == 0) return [];
            var ids = v.Split(',');
            foreach (string id in ids)
                if (!isResource(id)) throw new FormatException($"cargo {name}: unknown resource '{id}'");
            if (ids.Distinct().Count() != ids.Length) throw new FormatException($"cargo {name}: a resource repeats");
            return ids;
        }
        string loot = parts["loot"];
        if (loot.StartsWith('v'))
        {
            if (!int.TryParse(loot[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int min)) throw new FormatException($"cargo loot value '{loot}' is not a number");
            return new CargoSettings(min, [], List("sell"), List("mine"), List("buy"));
        }
        var types = List("loot");
        if (types.Length == 0) throw new FormatException("cargo loot needs a value (v0) or resources");
        return new CargoSettings(0, types, List("sell"), List("mine"), List("buy"));
    }

    /// <summary>Spec 1055: whether a Funnel with this setting takes a loot item of this type and unit value.</summary>
    public static bool FunnelAccepts(FunnelChoice choice, CargoSettings ship, string type, int unitValue) => choice switch
    {
        FunnelChoice.ShipFilter => ship.LootAccepts(type, unitValue),
        FunnelChoice.All => true,
        FunnelChoice.Value50 => unitValue >= 50,
        FunnelChoice.Value200 => unitValue >= 200,
        FunnelChoice.Value1000 => unitValue >= 1000,
        FunnelChoice.Off => false,
        _ => throw new ArgumentOutOfRangeException(nameof(choice), choice, null),
    };

    /// <summary>
    /// Spec 1051. The tiles in front of a part: the row or column next to the side that its arrow points to, ordered from the left
    /// front tile (seen along the arrow). Rotation: 0 arrow up (−y), 1 right, 2 down, 3 left (the game turns parts clockwise).
    /// box is the rotated part rectangle.
    /// </summary>
    public static Cell[] FrontCells(Box box, int rotation) => (rotation & 3) switch
    {
        0 => Enumerable.Range(0, box.W).Select(i => new Cell(box.X + i, box.Y - 1)).ToArray(),
        1 => Enumerable.Range(0, box.H).Select(i => new Cell(box.X + box.W, box.Y + i)).ToArray(),
        2 => Enumerable.Range(0, box.W).Select(i => new Cell(box.X + box.W - 1 - i, box.Y + box.H)).ToArray(),
        _ => Enumerable.Range(0, box.H).Select(i => new Cell(box.X - 1, box.Y + box.H - 1 - i)).ToArray(),
    };

    /// <summary>Spec 1051: a working Computer counts as this many crew members in the crew slot it serves.</summary>
    public const int ComputerCrew = 1;

    /// <summary>Spec 1051: whether a crew slot is full with the crew that is there and the Computer's crew.</summary>
    public static bool CrewMet(int crew, int needed, int computer) => crew + computer >= needed;

    /// <summary>Spec 1051: the crew that vanilla still sends to a served slot.</summary>
    public static int CrewStillWanted(int desired, int computer) => Math.Max(0, desired - computer);

    /// <summary>
    /// Spec 1051. Which part a Computer mans: the first front tile that holds a part that can be manned. partAt gives the index of
    /// the part on a tile, or null for none or for a part that cannot be manned. Null when no front tile has one.
    /// </summary>
    public static int? MannedPart(Box computer, int rotation, Func<Cell, int?> partAt)
    {
        foreach (var cell in FrontCells(computer, rotation))
            if (partAt(cell) is { } part) return part;
        return null;
    }

    /// <summary>
    /// Spec 1054. The tiles that touch a part from outside, in order: top row, right column, bottom row, left column, each from its
    /// start (left to right, top to bottom). The corners are not included: parts touch on sides.
    /// </summary>
    public static Cell[] EdgeCells(Box b)
    {
        var cells = new List<Cell>();
        for (int x = b.X; x < b.X + b.W; x++) cells.Add(new Cell(x, b.Y - 1));
        for (int y = b.Y; y < b.Y + b.H; y++) cells.Add(new Cell(b.X + b.W, y));
        for (int x = b.X; x < b.X + b.W; x++) cells.Add(new Cell(x, b.Y + b.H));
        for (int y = b.Y; y < b.Y + b.H; y++) cells.Add(new Cell(b.X - 1, y));
        return cells.ToArray();
    }

    /// <summary>Spec 1054: whether a point in ship tile units (x, y) lies in a Funnel's area: its rectangle grown by reach tiles on each side.</summary>
    public static bool InFunnelArea(Box funnel, float x, float y, float reach) =>
        x >= funnel.X - reach && x <= funnel.X + funnel.W + reach && y >= funnel.Y - reach && y <= funnel.Y + funnel.H + reach;

    /// <summary>
    /// Spec 1070. The ore of an asteroid in stasis, from its doodad ID: vanilla names them "author.asteroid_&lt;ore&gt;_&lt;size&gt;"
    /// (doodads/asteroids/&lt;ore&gt;/). Null for any other ID. The caller checks that the ore is a resource ("plain" is not).
    /// </summary>
    public static string? OreOfDoodad(string id)
    {
        int dot = id.IndexOf('.');
        string name = dot >= 0 ? id[(dot + 1)..] : id;
        const string prefix = "asteroid_";
        if (!name.StartsWith(prefix, StringComparison.Ordinal)) return null;
        string rest = name[prefix.Length..];
        int last = rest.LastIndexOf('_');
        return last > 0 ? rest[..last] : null;
    }

    /// <summary>
    /// Spec 1074. Units to buy: limited by room in the cargo, the station stock and the money above the reserve. Never below 0.
    /// </summary>
    public static int BuyAmount(long money, long reserve, int unitCost, int room, int stock)
    {
        if (unitCost <= 0) throw new ArgumentOutOfRangeException(nameof(unitCost), unitCost, "a buy needs a price above 0");
        long affordable = Math.Max(0, money - reserve) / unitCost;
        return (int)Math.Max(0, Math.Min(affordable, Math.Min(room, stock)));
    }

    /// <summary>
    /// Spec 1069, 1075. The index of the best item: highest score first (null scores never win), then the nearest, then the lowest ID.
    /// Pass score null to pick the nearest. Null with no items or with no scored item.
    /// </summary>
    public static int? Pick<TId>(IReadOnlyList<(float Distance, TId Id, double? Score)> items, bool useScore) where TId : IComparable<TId>
    {
        int? best = null;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (useScore && it.Score is null) continue;
            if (best is not { } b) { best = i; continue; }
            var cur = items[b];
            int byScore = useScore ? it.Score!.Value.CompareTo(cur.Score!.Value) : 0;
            if (byScore > 0 || (byScore == 0 && (it.Distance < cur.Distance || (it.Distance == cur.Distance && it.Id.CompareTo(cur.Id) < 0))))
                best = i;
        }
        return best;
    }
}

using System.Collections;
using System.Reflection;
using Halfling.Serialization;

namespace HotCargo.TunablesMenu;

/// <summary>
/// Paths to tunable values, for example "Heat.HeatPerGameMinute", "Hunters.Stages[0].ShipCount" or "Mechanics.Siege.Seconds".
/// The menu and the test commands use the same paths.
/// </summary>
public static class TunablePath
{
    /// <summary>Public instance fields with [Serialize], in declaration order: the fields that tunables.rules can set.</summary>
    public static IEnumerable<FieldInfo> Fields(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.GetCustomAttribute<SerializeAttribute>() is not null)
            .OrderBy(f => f.MetadataToken);

    /// <summary>The element type of an array or list field, or null for other types.</summary>
    public static Type? ElementType(Type type) =>
        type.IsArray ? type.GetElementType() : type.IsGenericType && typeof(IList).IsAssignableFrom(type) ? type.GetGenericArguments()[0] : null;

    /// <summary>A value that the menu edits as one text: a primitive, a string or an enum.</summary>
    public static bool IsScalar(Type type) => type.IsPrimitive || type == typeof(string) || type.IsEnum;

    public static string Child(string path, string field) => path.Length == 0 ? field : $"{path}.{field}";

    public static string Index(string path, int index) => $"{path}[{index}]";

    public static object? Get(object root, string path)
    {
        object? current = root;
        foreach (var (field, index) in Steps(path))
        {
            if (current is null) throw new InvalidOperationException($"tunable path '{path}' passes a null value");
            current = FieldOf(current.GetType(), field, path).GetValue(current);
            if (index is { } i) current = ItemAt(current, i, path);
        }
        return current;
    }

    /// <summary>Sets one value. An index replaces one list item; a field name replaces the whole field.</summary>
    public static void Set(object root, string path, object? value)
    {
        var steps = Steps(path);
        object current = root;
        for (int s = 0; s < steps.Count; s++)
        {
            var (name, index) = steps[s];
            var field = FieldOf(current.GetType(), name, path);
            bool last = s == steps.Count - 1;
            if (last && index is null)
            {
                field.SetValue(current, value);
                return;
            }
            object next = field.GetValue(current) ?? throw new InvalidOperationException($"tunable path '{path}' passes a null value");
            if (index is { } i)
            {
                if (last)
                {
                    if (next is not IList list || i < 0 || i >= list.Count) throw new InvalidOperationException($"tunable path '{path}': no item {i}");
                    list[i] = value;
                    return;
                }
                next = ItemAt(next, i, path) ?? throw new InvalidOperationException($"tunable path '{path}' passes a null value");
            }
            current = next;
        }
    }

    /// <summary>The field that the last step of the path names, for the type and the description.</summary>
    public static FieldInfo LastField(Type rootType, string path)
    {
        Type type = rootType;
        FieldInfo? field = null;
        foreach (var (name, index) in Steps(path))
        {
            field = FieldOf(type, name, path);
            type = index is null ? field.FieldType : ElementType(field.FieldType) ?? throw new InvalidOperationException($"tunable path '{path}': {name} is not a list");
        }
        return field ?? throw new InvalidOperationException("empty tunable path");
    }

    static FieldInfo FieldOf(Type type, string name, string path) =>
        Fields(type).FirstOrDefault(f => f.Name == name) ?? throw new InvalidOperationException($"tunable path '{path}': {type.Name} has no tunable '{name}'");

    static object? ItemAt(object? list, int i, string path) =>
        list is IList l && i >= 0 && i < l.Count ? l[i] : throw new InvalidOperationException($"tunable path '{path}': no item {i}");

    /// <summary>Splits "A.B[2].C" into (A, null), (B, 2), (C, null).</summary>
    static List<(string Field, int? Index)> Steps(string path)
    {
        var steps = new List<(string, int?)>();
        foreach (string part in path.Split('.'))
        {
            int open = part.IndexOf('[');
            if (open < 0) { steps.Add((part, null)); continue; }
            if (!part.EndsWith(']') || !int.TryParse(part[(open + 1)..^1], out int i))
                throw new InvalidOperationException($"bad tunable path '{path}'");
            steps.Add((part[..open], i));
        }
        if (steps.Any(s => s.Item1.Length == 0)) throw new InvalidOperationException($"bad tunable path '{path}'");
        return steps;
    }
}

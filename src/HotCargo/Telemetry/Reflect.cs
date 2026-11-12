using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Halfling.Serialization;

namespace HotCargo.Telemetry;

/// <summary>
/// Writes the [Serialize] fields of rules and state objects as JSON, so telemetry records whole tunables and outcomes
/// without a hand-written list of fields. Also makes short labels for trace arguments.
/// </summary>
static class Reflect
{
    const int MaxDepth = 8;
    static readonly ConcurrentDictionary<Type, FieldInfo[]> Fields = new();
    static readonly ConcurrentDictionary<Type, MemberInfo?> IdMembers = new();

    static FieldInfo[] SerializedFields(Type t) => Fields.GetOrAdd(t, static t =>
        t.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => f.GetCustomAttribute<SerializeAttribute>() is not null).ToArray());

    public static void WriteObject(Utf8JsonWriter w, object obj) => WriteValue(w, obj, 0);

    static void WriteValue(Utf8JsonWriter w, object? v, int depth)
    {
        switch (v)
        {
            case null: w.WriteNullValue(); return;
            case string s: w.WriteStringValue(s); return;
            case bool b: w.WriteBooleanValue(b); return;
            case float f: if (float.IsFinite(f)) w.WriteNumberValue(f); else w.WriteNullValue(); return;
            case double d: if (double.IsFinite(d)) w.WriteNumberValue(d); else w.WriteNullValue(); return;
            case int i: w.WriteNumberValue(i); return;
            case long l: w.WriteNumberValue(l); return;
            case Enum e: w.WriteStringValue(e.ToString()); return;
            case IFormattable when v.GetType().IsPrimitive: w.WriteStringValue(v.ToString()); return;
        }
        if (depth >= MaxDepth) { w.WriteStringValue("..."); return; }
        switch (v)
        {
            case IDictionary dict:
                w.WriteStartObject();
                foreach (DictionaryEntry kv in dict)
                {
                    w.WritePropertyName(kv.Key.ToString() ?? "");
                    WriteValue(w, kv.Value, depth + 1);
                }
                w.WriteEndObject();
                return;
            case IEnumerable list:
                w.WriteStartArray();
                foreach (var item in list) WriteValue(w, item, depth + 1);
                w.WriteEndArray();
                return;
        }
        var fields = SerializedFields(v.GetType());
        if (fields.Length == 0) { w.WriteStringValue(v.ToString()); return; }
        w.WriteStartObject();
        foreach (var f in fields)
        {
            w.WritePropertyName(f.Name);
            WriteValue(w, f.GetValue(v), depth + 1);
        }
        w.WriteEndObject();
    }

    /// <summary>A short label for a trace argument: the value, or the ID of a game or mod object. At most 160 characters.</summary>
    public static string Label(object? v)
    {
        string s = v switch
        {
            null => "null",
            string str => str,
            Cosmoteer.Modes.Career.Missions.Mission m => $"{m.MetatypeID} {m.MissionID}",
            Cosmoteer.Ships.Ship ship => $"{ship.Metadata.PlainShipName} {ship.UniqueID}",
            Core.Run or Cosmoteer.Simulation.SimRoot or Cosmoteer.Game.GameRoot => "-",
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            System.Collections.ICollection c => $"[{c.Count} items]",
            _ => IdOf(v) ?? v.ToString() ?? "",
        };
        return s.Length <= 160 ? s : s[..160];
    }

    /// <summary>The value of a member named ID, Id, ChainId, EventId, MissionID or Name, joined with Stage when there is one.</summary>
    static string? IdOf(object v)
    {
        var type = v.GetType();
        var member = IdMembers.GetOrAdd(type, static t =>
            new[] { "ID", "Id", "ChainId", "EventId", "MissionID", "StationID", "Name" }
                .Select(n => (MemberInfo?)t.GetField(n, BindingFlags.Public | BindingFlags.Instance) ?? t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance))
                .FirstOrDefault(m => m is not null));
        object? id = member switch { FieldInfo f => f.GetValue(v), PropertyInfo p => p.GetValue(v), _ => null };
        if (id is null) return null;
        object? stage = type.GetField("Stage", BindingFlags.Public | BindingFlags.Instance)?.GetValue(v);
        return stage is null ? $"{type.Name}:{id}" : $"{type.Name}:{id}/{stage}";
    }
}

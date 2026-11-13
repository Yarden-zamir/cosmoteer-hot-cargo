using System.Collections;
using System.Globalization;
using System.Text;
using Cosmoteer.Localization;
using Halfling.Serialization;
using HotCargo.Core;

namespace HotCargo.TunablesMenu;

/// <summary>
/// The write path of the settings menu (spec 501, 502). Each change makes a new Tunables from the values in effect,
/// validates it like a file (TunablesFile.Parse), writes tunables.rules with the changed values only, and applies it:
/// at once in single player, through the host's TunablesInput in multiplayer (every machine applies it at the same tick).
/// Multiplayer clients cannot change values (spec 504).
/// </summary>
public static class TunablesEditor
{
    const string FileHeader =
        "// Hot Cargo tunables: only the values that differ from the defaults.\n" +
        "// The in-game menu (Esc, Hot Cargo settings) writes this file. You can also edit it; the game reloads it while it runs (single player, and the host in co-op).\n" +
        "// All values and their defaults are in tunables.defaults.rules.\n";

    public static bool CanEdit(Run run) => !run.IsMultiplayer || run.Game.NetManager.IsHost;

    /// <summary>The values that the menu shows and edits: the values in effect, or the host's last change while its input travels.</summary>
    public static Tunables Current(Run run) => run.LatestTunables.Values;

    public static string CurrentHash(Run run) => run.LatestTunables.Hash;

    /// <summary>A deep copy, made through the same text that the file holds.</summary>
    public static Tunables Copy(Tunables t) => TunablesFile.Parse(Encoding.UTF8.GetBytes(TunablesDefaults.Write(t))).Values;

    /// <summary>Applies one edit. Returns null on success or when nothing changed, else the reason for the rejection.</summary>
    public static string? Change(Run run, Action<Tunables> edit)
    {
        if (!CanEdit(run)) return Text("ClientReadOnly");
        if (run.HostTunablesPending) return Text("HostFileInFlight");
        var before = Current(run);
        var candidate = Copy(before);
        edit(candidate);
        string changed = TunablesDefaults.WriteChanged(candidate);
        if (changed == TunablesDefaults.WriteChanged(before)) return null;
        byte[] bytes = Encoding.UTF8.GetBytes(FileHeader + changed);
        TunablesFile.Loaded loaded;
        try { loaded = TunablesFile.Parse(bytes); }
        catch (Exception ex) when (ex is InvalidDataException or DeserializeException) { return ex.Message; }
        TunablesFile.Write(bytes);
        if (run.IsMultiplayer) TunablesInput.Send(run, loaded);
        else
        {
            run.ApplyTunables(loaded);
            Log.Info($"tunables menu: applied {loaded.Hash}");
        }
        return null;
    }

    /// <summary>Sets the value at the path from text, as the menu controls and the test commands do.</summary>
    public static string? Set(Run run, string path, string text)
    {
        var field = TunablePath.LastField(typeof(Tunables), path);
        Type type = path.EndsWith(']') ? TunablePath.ElementType(field.FieldType)! : field.FieldType;
        object? value;
        try { value = Parse(type, text); }
        catch (FormatException ex) { return ex.Message; }
        return Change(run, t => TunablePath.Set(t, path, value));
    }

    /// <summary>
    /// Applies several "set PATH VALUE" and "reset PATH" lines as one change (scripts/tune.sh, spec 853): all or none.
    /// Returns null on success or when nothing changed, else the reason for the rejection.
    /// </summary>
    public static string? Apply(Run run, IReadOnlyList<string> lines)
    {
        var edits = new List<(string Path, object? Value)>();
        var defaults = new Tunables();
        foreach (string line in lines)
        {
            try
            {
                switch (line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries))
                {
                    case ["set", var path, var text]:
                        var field = TunablePath.LastField(typeof(Tunables), path);
                        edits.Add((path, Parse(path.EndsWith(']') ? TunablePath.ElementType(field.FieldType)! : field.FieldType, text)));
                        break;
                    case ["reset", var path]:
                        edits.Add((path, TunablePath.Get(defaults, path)));
                        break;
                    default:
                        return $"expected 'set PATH VALUE' or 'reset PATH', got '{line}'";
                }
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException) { return $"{line}: {ex.Message}"; }
        }
        try { return Change(run, t => { foreach (var (path, value) in edits) TunablePath.Set(t, path, value); }); }
        catch (InvalidOperationException ex) { return ex.Message; }
    }

    /// <summary>Sets the value at the path to its default. Returns an error when the path has no default (a list item that the defaults do not have).</summary>
    public static string? Reset(Run run, string path)
    {
        var defaults = new Tunables();
        object? value;
        try { value = TunablePath.Get(defaults, path); }
        catch (InvalidOperationException) { return Text("NoDefault"); }
        return Change(run, t => TunablePath.Set(t, path, value));
    }

    /// <summary>Adds a copy of the last item to a list of objects, or a new item to an empty list.</summary>
    public static string? AddItem(Run run, string path) => Change(run, t =>
    {
        var list = (IList)TunablePath.Get(t, path)!;
        Type element = TunablePath.ElementType(list.GetType())!;
        object item = list.Count > 0 ? Copy(list[list.Count - 1]!) : Activator.CreateInstance(element)!;
        TunablePath.Set(t, path, ToArray(element, [.. list.Cast<object>(), item]));
    });

    public static string? RemoveItem(Run run, string path, int index) => Change(run, t =>
    {
        var list = (IList)TunablePath.Get(t, path)!;
        TunablePath.Set(t, path, ToArray(TunablePath.ElementType(list.GetType())!, list.Cast<object>().Where((_, i) => i != index).ToList()));
    });

    static Array ToArray(Type element, List<object> items)
    {
        var result = Array.CreateInstance(element, items.Count);
        for (int i = 0; i < items.Count; i++) result.SetValue(items[i], i);
        return result;
    }

    /// <summary>A deep copy of one list item: a fresh object with the same field values.</summary>
    static object Copy(object item)
    {
        object copy = Activator.CreateInstance(item.GetType())!;
        foreach (var f in TunablePath.Fields(item.GetType()))
        {
            object? v = f.GetValue(item);
            f.SetValue(copy, v switch
            {
                Array a => a.Clone(),
                null or string => v,
                _ when TunablePath.IsScalar(f.FieldType) => v,
                _ => Copy(v),
            });
        }
        return copy;
    }

    /// <summary>The text that the menu shows for a value. Lists are comma separated.</summary>
    public static string Format(object? value) => value switch
    {
        null => "",
        string s => s,
        Enum e => e.ToString(),
        IList list => string.Join(", ", list.Cast<object>().Select(Format)),
        _ => TunablesDefaults.Scalar(value),
    };

    /// <summary>Parses menu text for a scalar or a list of scalars. Throws FormatException with a message for the player.</summary>
    public static object Parse(Type type, string text)
    {
        if (TunablePath.ElementType(type) is { } element)
        {
            var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var array = Array.CreateInstance(element, parts.Length);
            for (int i = 0; i < parts.Length; i++) array.SetValue(Parse(element, parts[i]), i);
            return array;
        }
        text = text.Trim();
        if (type == typeof(string)) return text;
        if (type == typeof(bool))
            return text.ToLowerInvariant() switch
            {
                "true" or "on" or "1" => true,
                "false" or "off" or "0" => false,
                _ => throw new FormatException(Strings.FormatText("HotCargoTunablesMenu/NotAValueFmt", text, "on/off")),
            };
        if (type.IsEnum)
            return Enum.TryParse(type, text, ignoreCase: true, out object? e) ? e!
                : throw new FormatException(Strings.FormatText("HotCargoTunablesMenu/NotAValueFmt", text, string.Join("/", Enum.GetNames(type))));
        try
        {
            object value = Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
            if (value is float f && !float.IsFinite(f) || value is double d && !double.IsFinite(d)) throw new FormatException();
            return value;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
        {
            throw new FormatException(Strings.FormatText("HotCargoTunablesMenu/NotAValueFmt", text, type == typeof(int) || type == typeof(long) ? "whole number" : "number"));
        }
    }

    static string Text(string key) => Strings.GetText("HotCargoTunablesMenu/" + key);
}

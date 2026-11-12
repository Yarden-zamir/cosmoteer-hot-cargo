using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace HotCargo.TunablesMenu;

/// <summary>
/// Labels and descriptions for the menu, made from the code: the field names and the XML doc comments (HotCargo.xml next to the DLL).
/// A field without a doc comment shows only its label. Add a summary comment to a tunable to give it a description.
/// </summary>
public static class TunableDocs
{
    static Dictionary<string, string>? _summaries;

    /// <summary>The doc summary of a field, or of the matching "Min" field for a "Max" field without one. Empty when there is none.</summary>
    public static string Describe(FieldInfo field)
    {
        string own = Summary("F:" + DocName(field.DeclaringType!) + "." + field.Name);
        if (own.Length > 0 || !field.Name.EndsWith("Max")) return own;
        return Summary("F:" + DocName(field.DeclaringType!) + "." + field.Name[..^3] + "Min");
    }

    public static string Describe(Type type) => Summary("T:" + DocName(type));

    /// <summary>"HeatPerGameMinute" becomes "Heat per game minute", "TechShipFractionPer100Notoriety" becomes "Tech ship fraction per 100 notoriety".</summary>
    public static string Label(string name)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            bool wordStart = i > 0 && ((char.IsUpper(c) && !char.IsUpper(name[i - 1])) || (char.IsDigit(c) && !char.IsDigit(name[i - 1])));
            if (wordStart) sb.Append(' ');
            sb.Append(i == 0 ? c : char.IsUpper(c) && (i + 1 >= name.Length || !char.IsUpper(name[i + 1])) ? char.ToLowerInvariant(c) : c);
        }
        return sb.ToString();
    }

    static string DocName(Type type) => (type.FullName ?? type.Name).Replace('+', '.');

    static string Summary(string member)
    {
        _summaries ??= Load();
        return _summaries.TryGetValue(member, out string? s) ? s : "";
    }

    static Dictionary<string, string> Load()
    {
        string path = Path.ChangeExtension(typeof(TunableDocs).Assembly.Location, ".xml");
        if (!File.Exists(path))
        {
            // A packaging error, not a game error: the menu still works without descriptions.
            Log.Error($"tunables menu: {path} is missing, so the settings show no descriptions");
            return [];
        }
        var result = new Dictionary<string, string>();
        foreach (var member in XDocument.Load(path).Descendants("member"))
        {
            if (member.Attribute("name")?.Value is not { } name || member.Element("summary") is not { } summary) continue;
            result[name] = Clean(PlainText(summary));
        }
        return result;
    }

    /// <summary>The text of a summary. A see or paramref tag becomes its name.</summary>
    static string PlainText(XElement e)
    {
        var sb = new StringBuilder();
        foreach (var node in e.Nodes())
        {
            switch (node)
            {
                case XText t: sb.Append(t.Value); break;
                case XElement { Name.LocalName: "see" or "seealso" } see:
                    string target = see.Attribute("cref")?.Value ?? see.Attribute("langword")?.Value ?? "";
                    sb.Append(target[(target.LastIndexOf('.') + 1)..]);
                    break;
                case XElement { Name.LocalName: "paramref" or "typeparamref" } p: sb.Append(p.Attribute("name")?.Value); break;
                case XElement inner: sb.Append(PlainText(inner)); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>One line, without the "Spec 101:" reference at the start that only developers need.</summary>
    static string Clean(string text)
    {
        string line = string.Join(' ', text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));
        if (line.StartsWith("Spec ") && line.IndexOf(':') is var colon and > 0 and < 20) line = line[(colon + 1)..].TrimStart();
        return line.Length > 0 ? char.ToUpperInvariant(line[0]) + line[1..] : line;
    }
}

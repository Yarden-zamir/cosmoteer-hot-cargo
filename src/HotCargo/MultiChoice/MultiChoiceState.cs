using Halfling.Serialization;

namespace HotCargo.MultiChoice;

/// <summary>Shared drafts of open multiselect pages (spec 592). Saved inside RunState, so a resync and a save carry them.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class MultiChoiceState
{
    [Serialize(Optional = true)] public List<Draft> Drafts = new();

    public Draft? Find(string domain, int serial) => Drafts.FirstOrDefault(d => d.Domain == domain && d.Serial == serial);
}

/// <summary>The options that the players selected on one page, each with its value. Not selected = not in the list.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class Draft
{
    [Serialize] public string Domain = "";
    [Serialize] public int Serial;
    [Serialize] public List<Pick> Picks = new();

    public Pick? Of(int option) => Picks.FirstOrDefault(p => p.Option == option);
}

[ReflectiveSerialization(Explicit = true)]
public sealed class Pick
{
    [Serialize] public int Option;
    [Serialize] public int Value;
}

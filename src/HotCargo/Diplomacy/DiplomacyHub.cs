using Halfling.Serialization;
using HotCargo.Diplomacy;

namespace HotCargo.Core;

// Diplomacy's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public Diplomacy.DiplomacyTunables Diplomacy = new();
}

public sealed partial class RunState
{
    /// <summary>Diplomacy area state: once-per-ship offers, held tech, hires, mercenaries (spec 586).</summary>
    [Serialize(Optional = true)] public Diplomacy.DiplomacyState Diplomacy = new();
}

using Halfling.Serialization;
using HotCargo.SharedChoice;

namespace HotCargo.Core;

// SharedChoice's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public SharedChoiceTunables SharedChoice = new();
}

public sealed partial class RunState
{
    /// <summary>Co-op shared choices: the vote mode and the votes on open pages (spec 603, 607).</summary>
    [Serialize(Optional = true)] public SharedChoice.SharedChoiceState SharedChoice = new();
}

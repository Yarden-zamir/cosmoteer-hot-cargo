using Halfling.Serialization;
using HotCargo.MultiChoice;

namespace HotCargo.Core;

// MultiChoice's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class RunState
{
    /// <summary>Spec 592: the shared drafts of open multiselect pages.</summary>
    [Serialize(Optional = true)] public MultiChoice.MultiChoiceState MultiChoice = new();
}

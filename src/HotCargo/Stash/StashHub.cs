using Halfling.Serialization;
using HotCargo.Stash;

namespace HotCargo.Core;

// Stash's part of the shared Tunables. The stash keeps its state in its objective (in the mission save), so it adds no run state.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public StashTunables Stash = new();
}

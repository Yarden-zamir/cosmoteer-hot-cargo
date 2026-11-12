using Halfling.Serialization;
using HotCargo.Onboarding;

namespace HotCargo.Core;

// Onboarding's parts of the shared Tunables and RunState. An area adds its fields here, so it never edits Core files.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public Onboarding.OnboardingTunables Onboarding = new();
}

public sealed partial class RunState
{
    /// <summary>Spec 730: the run log that the career log window shows, oldest first.</summary>
    [Serialize(Optional = true)] public List<Onboarding.RunLogEntry> Log = new();
}

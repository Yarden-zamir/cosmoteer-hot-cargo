using Halfling.Serialization;

namespace HotCargo.Onboarding;

/// <summary>How to Play pages and the order of Hot Cargo and vanilla tutorials (spec 720 to 734).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class OnboardingTunables
{
    /// <summary>
    /// Vanilla tutorials do not show in the first seconds of a run, so the Hot Cargo pages come first.
    /// They also wait while a Hot Cargo page is new. 0: they wait only for new Hot Cargo pages.
    /// </summary>
    [Serialize(Optional = true)] public float VanillaTutorialDelaySeconds = 300f;
}

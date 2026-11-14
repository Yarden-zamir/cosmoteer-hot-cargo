using Halfling.Serialization;

namespace HotCargo.Debris;

/// <summary>Debris decay (spec 1240 to 1246): old loose junk glows, then burns up.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class DebrisTunables
{
    /// <summary>Spec 1240: old loose junk glows and then burns up. Off: junk stays, as in vanilla.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;
    /// <summary>Spec 1242: game seconds from the time a junk piece can decay until it burns up.</summary>
    [Serialize(Optional = true)] public float DecaySeconds = 240f;
    /// <summary>Spec 1243: the last game seconds before the burn-up, in which the piece glows.</summary>
    [Serialize(Optional = true)] public float GlowSeconds = 60f;
    /// <summary>Spec 1241: a junk piece with more parts than this is a wreck worth salvage, and it stays.</summary>
    [Serialize(Optional = true)] public int MaxParts = 20;
    /// <summary>Spec 1244: game seconds between two checks of the junk in the system.</summary>
    [Serialize(Optional = true)] public float CheckSeconds = 2f;
    /// <summary>Spec 1241: junk this far beyond the reach of a player's tractor beam also stays, so a pull that starts never loses its target.</summary>
    [Serialize(Optional = true)] public float TractorMargin = 100f;

    public void Validate(Action<bool, string> require)
    {
        require(DecaySeconds > 0, "Debris.DecaySeconds must be above 0");
        require(GlowSeconds >= 0 && GlowSeconds <= DecaySeconds, "Debris.GlowSeconds must be 0 to DecaySeconds");
        require(MaxParts >= 1, "Debris.MaxParts must be 1 or more");
        require(CheckSeconds > 0, "Debris.CheckSeconds must be above 0");
        require(TractorMargin >= 0, "Debris.TractorMargin must not be negative");
    }
}

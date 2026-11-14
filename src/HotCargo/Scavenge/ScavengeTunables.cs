using Halfling.Serialization;

namespace HotCargo.Scavenge;

/// <summary>Scavenging (spec 1330 to 1359): ships that collect old wrecks and loose resources.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ScavengeTunables
{
    /// <summary>Spec 1330: scavenging can happen. Off: no ship scavenges and no ages are kept.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;
    /// <summary>Spec 1331: game seconds after the first check that saw a wreck or a loose resource before a scavenger takes it. At least 60.</summary>
    [Serialize(Optional = true)] public float CooldownSeconds = 90f;
    /// <summary>Spec 1332: game seconds between two age checks.</summary>
    [Serialize(Optional = true)] public float CheckSeconds = 2f;
    /// <summary>Spec 1333: a scavenger takes targets up to this edge distance from its hull.</summary>
    [Serialize(Optional = true)] public float SearchRange = 4000f;
    /// <summary>Spec 1334: the gap that the scavenger's mining laser (or, without one, its hull) keeps outside the wreck's bounding circle.</summary>
    [Serialize(Optional = true)] public float WorkDistance = 80f;
    /// <summary>Spec 1334: at most this many open salvage orders on the wreck at a time.</summary>
    [Serialize(Optional = true)] public int SalvageJobs = 4;
    /// <summary>Spec 1336: a scavenger with this storage bay fill or more stops collecting and goes to sell.</summary>
    [Serialize(Optional = true)] public float FullFraction = 0.9f;

    public void Validate(Action<bool, string> require)
    {
        require(CooldownSeconds >= 60f, "Scavenge.CooldownSeconds must be 60 or more, so scavengers never take fresh loot (spec 1331)");
        require(CheckSeconds > 0f, "Scavenge.CheckSeconds must be above 0");
        require(SearchRange > 0f && WorkDistance >= 0f, "Scavenge.SearchRange must be above 0 and WorkDistance not negative");
        require(SalvageJobs >= 1, "Scavenge.SalvageJobs must be 1 or more");
        require(FullFraction is > 0f and <= 1f, "Scavenge.FullFraction must be above 0 and at most 1");
    }
}

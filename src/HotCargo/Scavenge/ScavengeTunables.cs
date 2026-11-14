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

    /// <summary>Spec 1343: scavenger ships arrive in systems. Off: no new scavenger arrives; the ones in the system stay.</summary>
    [Serialize(Optional = true)] public bool FactionEnabled = true;
    /// <summary>Spec 1343: at most this many live scavengers in a system.</summary>
    [Serialize(Optional = true)] public int MaxPerSystem = 2;
    /// <summary>Spec 1343: game seconds between two arrival rolls.</summary>
    [Serialize(Optional = true)] public float SpawnCheckSeconds = 60f;
    /// <summary>Spec 1343: chance that a scavenger arrives at an arrival roll, while the system has room for one.</summary>
    [Serialize(Optional = true)] public float SpawnChance = 0.35f;
    /// <summary>Spec 1343: a scavenger arrives this far from the players' first ship (center distance), and at least SpawnDistanceMin from every station.</summary>
    [Serialize(Optional = true)] public float SpawnDistanceMin = 3000f;
    [Serialize(Optional = true)] public float SpawnDistanceMax = 7000f;
    /// <summary>Spec 1341: game seconds that the scavengers stay hostile to the players after a player hit a scavenger. Each hit starts it again.</summary>
    [Serialize(Optional = true)] public float ProvokedSeconds = 300f;
    /// <summary>Spec 1344: enemies within this edge distance count as threats.</summary>
    [Serialize(Optional = true)] public float FleeRange = 3000f;
    /// <summary>Spec 1344: a threat that flies toward the scavenger at this speed or faster (world units per second) comes its way.</summary>
    [Serialize(Optional = true)] public float ApproachSpeed = 15f;
    /// <summary>Spec 1344: a fleeing scavenger stops when no threat comes its way and the nearest threat is this far (edge distance).</summary>
    [Serialize(Optional = true)] public float SafeDistance = 2500f;
    /// <summary>Spec 1344: a scavenger with this many times the firepower of the threats, or more, is clearly stronger and does not flee.</summary>
    [Serialize(Optional = true)] public float StrongerRatio = 2f;

    public void Validate(Action<bool, string> require)
    {
        require(MaxPerSystem >= 0 && SpawnCheckSeconds > 0f && SpawnChance is >= 0f and <= 1f, "Scavenge.MaxPerSystem must not be negative, SpawnCheckSeconds above 0, SpawnChance 0 to 1");
        require(SpawnDistanceMin >= 0f && SpawnDistanceMax >= SpawnDistanceMin, "Scavenge.SpawnDistanceMin must not be negative and at most SpawnDistanceMax");
        require(ProvokedSeconds >= 0f && FleeRange > 0f && ApproachSpeed >= 0f && SafeDistance > 0f && StrongerRatio > 0f,
            "Scavenge.ProvokedSeconds and ApproachSpeed must not be negative; FleeRange, SafeDistance and StrongerRatio above 0");
        require(CooldownSeconds >= 60f, "Scavenge.CooldownSeconds must be 60 or more, so scavengers never take fresh loot (spec 1331)");
        require(CheckSeconds > 0f, "Scavenge.CheckSeconds must be above 0");
        require(SearchRange > 0f && WorkDistance >= 0f, "Scavenge.SearchRange must be above 0 and WorkDistance not negative");
        require(SalvageJobs >= 1, "Scavenge.SalvageJobs must be 1 or more");
        require(FullFraction is > 0f and <= 1f, "Scavenge.FullFraction must be above 0 and at most 1");
    }
}

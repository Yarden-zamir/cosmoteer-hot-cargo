using Halfling.Serialization;

namespace HotCargo.Pursuit;

/// <summary>The Pursuit (spec 900 to 949): the director that paces hunter waves and big events.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class PursuitTunables
{
    /// <summary>Off: no ramp, no fleet factor, no scout reports, blockades, outages or hacker. Other areas may still ask the pacer.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;

    // ---- Director (spec 900 to 904)

    /// <summary>Spec 901: hunter ships of every stage response × this in sector 1. It rises in a straight line to LateShipFactor in the last sector.</summary>
    [Serialize(Optional = true)] public float EarlyShipFactor = 0.75f;
    /// <summary>Spec 901: hunter ships × this in the last sector of the run (the Buyer).</summary>
    [Serialize(Optional = true)] public float LateShipFactor = 1f;
    /// <summary>Spec 901: the repeat Strike wave interval × this in sector 1, falling in a straight line to LateIntervalFactor in the last sector.</summary>
    [Serialize(Optional = true)] public float EarlyIntervalFactor = 1.5f;
    /// <summary>Spec 901: the repeat Strike wave interval × this in the last sector.</summary>
    [Serialize(Optional = true)] public float LateIntervalFactor = 1f;
    /// <summary>Spec 902: fleet value per player that counts as even, as a share of the vanilla ship value maximum of the hunter tier.</summary>
    [Serialize(Optional = true)] public float FleetReferenceShare = 1f;
    /// <summary>Spec 902: hunter ships change by this share for each doubling (or halving) of the fleet value against the reference.</summary>
    [Serialize(Optional = true)] public float FleetFactorPerDoubling = 0.15f;
    /// <summary>Spec 902: the fleet factor stays within 1 ± this.</summary>
    [Serialize(Optional = true)] public float FleetFactorMax = 0.15f;

    // ---- Pacer (spec 905 to 909)

    /// <summary>Spec 905: game seconds between the starts of any two big events.</summary>
    [Serialize(Optional = true)] public float MinEventGapSeconds = 300f;
    /// <summary>Spec 905: the big event kinds, each with a run limit and a gap. A kind that is not in this list is an error.</summary>
    [Serialize(Optional = true)] public PacedEvent[] Events =
    [
        new() { Kind = PacedKinds.Blockade, MaxPerRun = 2, MinGapSeconds = 1200f },
        new() { Kind = PacedKinds.Outage, MaxPerRun = 2, MinGapSeconds = 1200f },
        new() { Kind = PacedKinds.Hacker, MaxPerRun = 1, MinGapSeconds = 0f },
        new() { Kind = PacedKinds.RelayLockdown, MaxPerRun = 2, MinGapSeconds = 1200f },
    ];

    // ---- Reporting scout (spec 910 to 914)

    /// <summary>Spec 910: the heat stage whose first ship can be a reporting scout.</summary>
    [Serialize(Optional = true)] public string ScoutStage = "Warning";
    /// <summary>Spec 910: chance that the first ship of the scout stage flies to report instead of hunting.</summary>
    [Serialize(Optional = true)] public float ScoutChance = 0.5f;
    /// <summary>Spec 912: the scout reports when it is this close to its relay (center to center).</summary>
    [Serialize(Optional = true)] public float ScoutRelayRadius = 400f;
    /// <summary>Spec 912: heat that an escaped scout report adds.</summary>
    [Serialize(Optional = true)] public float ScoutReportHeat = 15f;

    // ---- Relay blockade (spec 915 to 919)

    /// <summary>Spec 915: heat stages whose hunters can park at a relay instead of hunting.</summary>
    [Serialize(Optional = true)] public string[] BlockadeStages = ["Hunt", "Strike"];
    /// <summary>Spec 915: chance of a blockade when such a stage starts and the pacer allows it.</summary>
    [Serialize(Optional = true)] public float BlockadeChance = 0.5f;
    /// <summary>Spec 916: blockade ships park within this distance of the relay.</summary>
    [Serialize(Optional = true)] public float BlockadeParkRadius = 600f;
    /// <summary>Spec 917: a blockade ship chases when a human ship comes this close (edge to edge), or when it takes damage.</summary>
    [Serialize(Optional = true)] public float BlockadeAggroRange = 2200f;

    // ---- Network outage (spec 920 to 926)

    /// <summary>Spec 920: first sector that rolls a natural outage.</summary>
    [Serialize(Optional = true)] public int OutageMinSector = 2;
    /// <summary>Spec 920: chance of a natural outage in each new system from OutageMinSector to the sector before the last one.</summary>
    [Serialize(Optional = true)] public float OutageChance = 0.5f;
    /// <summary>Spec 920: a natural outage starts this many game seconds after arrival, at the earliest.</summary>
    [Serialize(Optional = true)] public float OutageDelayMin = 90f;
    /// <summary>Spec 920: a natural outage starts this many game seconds after arrival, at the latest.</summary>
    [Serialize(Optional = true)] public float OutageDelayMax = 480f;
    /// <summary>Spec 921: game seconds of a natural outage.</summary>
    [Serialize(Optional = true)] public float OutageSeconds = 120f;

    // ---- Hacker (spec 930 to 936)

    /// <summary>Spec 930: first sector where the hacker can come.</summary>
    [Serialize(Optional = true)] public int HackerMinSector = 2;
    /// <summary>Spec 930: chance that the hacker comes in a new system (the pacer limits it too).</summary>
    [Serialize(Optional = true)] public float HackerChance = 0.25f;
    /// <summary>Spec 930: the hacker comes this many game seconds after arrival, at the earliest.</summary>
    [Serialize(Optional = true)] public float HackerDelayMin = 60f;
    /// <summary>Spec 930: the hacker comes this many game seconds after arrival, at the latest.</summary>
    [Serialize(Optional = true)] public float HackerDelayMax = 300f;
    /// <summary>Spec 932: the hacker leaves this many game seconds after his hail (or after his arrival, if he cannot hail).</summary>
    [Serialize(Optional = true)] public float HackerStaySeconds = 180f;
    /// <summary>Spec 931: the outage that the hacker sells, in game seconds.</summary>
    [Serialize(Optional = true)] public float HackerOutageSeconds = 240f;
    /// <summary>Spec 931: the hacker price as a share of the vanilla ship value maximum of the hunter tier.</summary>
    [Serialize(Optional = true)] public float HackerPriceShare = 0.5f;
    /// <summary>Spec 931: the hacker price is rounded to this many credits.</summary>
    [Serialize(Optional = true)] public int HackerPriceRoundTo = 1000;

    /// <summary>Called from TunablesFile.Validate (spec 53).</summary>
    public void Validate(Action<bool, string> require, IReadOnlyCollection<string> stages)
    {
        require(ScoutStage.Length == 0 || stages.Contains(ScoutStage), $"Pursuit.ScoutStage '{ScoutStage}' is not a Heat.Stages name");
        foreach (string b in BlockadeStages) require(stages.Contains(b), $"Pursuit.BlockadeStages '{b}' is not a Heat.Stages name");
        require(EarlyShipFactor > 0 && LateShipFactor > 0, "Pursuit.EarlyShipFactor and LateShipFactor must be above 0");
        require(EarlyIntervalFactor > 0 && LateIntervalFactor > 0, "Pursuit.EarlyIntervalFactor and LateIntervalFactor must be above 0");
        require(FleetReferenceShare > 0, "Pursuit.FleetReferenceShare must be above 0");
        require(FleetFactorMax is >= 0 and < 1, "Pursuit.FleetFactorMax must be 0 or more and below 1");
        require(MinEventGapSeconds >= 0, "Pursuit.MinEventGapSeconds must not be negative");
        foreach (var e in Events)
            require(e.Kind.Length > 0 && e.MaxPerRun >= 0 && e.MinGapSeconds >= 0, $"Pursuit.Events {e.Kind}: needs a Kind, MaxPerRun ≥ 0 and MinGapSeconds ≥ 0");
        require(Events.Select(e => e.Kind).Distinct().Count() == Events.Length, "Pursuit.Events: each Kind only once");
        require(ScoutChance is >= 0 and <= 1 && BlockadeChance is >= 0 and <= 1 && OutageChance is >= 0 and <= 1 && HackerChance is >= 0 and <= 1,
            "Pursuit chances must be 0 to 1");
        require(OutageDelayMin >= 0 && OutageDelayMax >= OutageDelayMin, "Pursuit.OutageDelayMax must be at least OutageDelayMin");
        require(HackerDelayMin >= 0 && HackerDelayMax >= HackerDelayMin, "Pursuit.HackerDelayMax must be at least HackerDelayMin");
        require(OutageSeconds > 0 && HackerOutageSeconds > 0 && HackerStaySeconds > 0, "Pursuit outage and stay seconds must be above 0");
        require(HackerPriceShare >= 0 && HackerPriceRoundTo >= 1, "Pursuit.HackerPriceShare must not be negative and HackerPriceRoundTo at least 1");
    }
}

[ReflectiveSerialization(Explicit = true)]
public sealed class PacedEvent
{
    [Serialize] public string Kind = "";
    /// <summary>Starts of this kind in one run, at most.</summary>
    [Serialize(Optional = true)] public int MaxPerRun = 1;
    /// <summary>Game seconds between two starts of this kind.</summary>
    [Serialize(Optional = true)] public float MinGapSeconds;
}

/// <summary>The big event kinds of the default pacer list. Other areas pass their kind as a string; these names match the defaults.</summary>
public static class PacedKinds
{
    public const string Blockade = "Blockade";
    public const string Outage = "Outage";
    public const string Hacker = "Hacker";
    public const string RelayLockdown = "RelayLockdown";
}

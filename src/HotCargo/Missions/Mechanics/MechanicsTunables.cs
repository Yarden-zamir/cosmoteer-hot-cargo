using Halfling.Serialization;

namespace HotCargo.Missions.Mechanics;

/// <summary>Numbers of the mechanics missions (spec 130 to 139). Rules values (part health, asteroid counts, rewards) are in mod/missions/mechanics.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class MechanicsTunables
{
    /// <summary>Spec 131: chance that a system offers each mission. MechanicsArea applies them to the spawner groups before a system generates.</summary>
    [Serialize(Optional = true)] public float DroneSiegeChance = 0.2f;
    [Serialize(Optional = true)] public float PrototypeChance = 0.2f;
    [Serialize(Optional = true)] public float SunMiningChance = 0.25f;
    [Serialize(Optional = true)] public float TowChance = 0.15f;
    [Serialize(Optional = true)] public float CaptureChance = 0.15f;
    [Serialize(Optional = true)] public float SurveyChance = 0.25f;
    [Serialize(Optional = true)] public float MiningGuardChance = 0.15f;

    /// <summary>Spec 132: Drone Siege. Tier-1 to tier-3 drones, many of them, for five minutes.</summary>
    [Serialize(Optional = true)] public HoldOutTunables Siege = new()
    {
        Seconds = 300f, GuardRadius = 1500f, WaveSeconds = 35f, FirstWaveSeconds = 8f,
        ShipsBase = 2f, ShipsPerTier = 0.25f, MaxLiveShips = 10,
        ShipTierBase = 1f, ShipTierPerTier = 0.2f, ShipTierMax = 3, ShipTags = ["combat"],
    };

    /// <summary>Spec 137: Claim Jumpers. Few raiders one tier below the sector, for four minutes.</summary>
    [Serialize(Optional = true)] public HoldOutTunables Guard = new()
    {
        Seconds = 240f, GuardRadius = 1500f, WaveSeconds = 60f, FirstWaveSeconds = 15f,
        ShipsBase = 1f, ShipsPerTier = 0.1f, MaxLiveShips = 4,
        ShipTierBase = -1f, ShipTierPerTier = 1f, ShipTierMax = 99, ShipTags = ["combat"],
    };

    /// <summary>Spec 133, 135: a hulk counts as delivered when its hull is this close to the station hull.</summary>
    [Serialize(Optional = true)] public float DeliveryRadius = 800f;

    /// <summary>Spec 136: seconds that a ship with a working sensor must stay at each storm marker.</summary>
    [Serialize(Optional = true)] public float SurveyScanSeconds = 15f;

    /// <summary>Called by TunablesFile.Validate, so a bad file keeps the last valid values (spec 53).</summary>
    public static void Validate(MechanicsTunables t)
    {
        static void Require(bool ok, string what) { if (!ok) throw new InvalidDataException($"Mechanics.{what}"); }
        foreach (var (name, chance) in MechanicsArea.Kinds.Select(k => (k.Name, k.Chance(t))))
            Require(chance is >= 0f and <= 1f, $"chance of '{name}' must be 0 to 1");
        foreach (var (name, h) in new[] { ("Siege", t.Siege), ("Guard", t.Guard) })
        {
            Require(h.Seconds > 0f && h.GuardRadius > 0f && h.WaveSeconds > 0f && h.FirstWaveSeconds >= 0f, $"{name}: Seconds, GuardRadius and WaveSeconds must be above 0");
            Require(h.ShipsBase >= 0f && h.ShipsPerTier >= 0f && h.MaxLiveShips >= 0 && h.ShipTierMax >= 1, $"{name}: ship counts must not be negative and ShipTierMax must be 1 or more");
            Require(h.ShipTags.Length > 0, $"{name}.ShipTags must not be empty");
        }
        Require(t.DeliveryRadius > 0f, "DeliveryRadius must be above 0");
        Require(t.SurveyScanSeconds > 0f, "SurveyScanSeconds must be above 0");
    }
}

/// <summary>One hold-out mission profile (spec 132, 137). The clock runs only while a human ship is within GuardRadius of the target.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class HoldOutTunables
{
    [Serialize(Optional = true)] public float Seconds = 300f;
    [Serialize(Optional = true)] public float GuardRadius = 1500f;
    [Serialize(Optional = true)] public float WaveSeconds = 40f;
    /// <summary>Guarded seconds before the first wave.</summary>
    [Serialize(Optional = true)] public float FirstWaveSeconds = 10f;
    /// <summary>Ships per wave = ShipsBase + ShipsPerTier × mission tier, rounded, capped so that live wave ships stay at MaxLiveShips or below.</summary>
    [Serialize(Optional = true)] public float ShipsBase = 2f;
    [Serialize(Optional = true)] public float ShipsPerTier;
    [Serialize(Optional = true)] public int MaxLiveShips = 8;
    /// <summary>Tier of each wave ship = ShipTierBase + ShipTierPerTier × mission tier, rounded, in 1..ShipTierMax.</summary>
    [Serialize(Optional = true)] public float ShipTierBase = 1f;
    [Serialize(Optional = true)] public float ShipTierPerTier;
    [Serialize(Optional = true)] public int ShipTierMax = 99;
    /// <summary>Built-in ship tags of the wave pool.</summary>
    [Serialize(Optional = true)] public string[] ShipTags = ["combat"];
}

using Halfling.Serialization;

namespace HotCargo.RunModes;

/// <summary>Run modes: difficulty pressure (spec 680 to 684), the fame title prototype (686) and the Strike music (688).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class RunModesTunables
{
    /// <summary>
    /// Spec 680: pressure factors for each vanilla combat difficulty level. The rows of the career's combat, economy and crew levels
    /// multiply (HunterTiers add). A level without a row counts as 1. The vanilla defaults (Easy, Fast, Standard) stay at 1,
    /// because the balance numbers use them.
    /// </summary>
    [Serialize(Optional = true)] public DifficultyRow[] Combat =
    [
        new() { Level = "no_death", HeatRate = 0.8f, HunterShips = 0.67f, HunterTiers = -1 },
        new() { Level = "easy" },
        new() { Level = "normal", HeatRate = 1.1f },
        new() { Level = "hard", HeatRate = 1.2f, HunterShips = 1.34f },
        new() { Level = "very_hard", HeatRate = 1.3f, HunterShips = 1.34f, HunterTiers = 1 },
    ];
    /// <summary>Spec 680: pressure factors for each vanilla economic difficulty level.</summary>
    [Serialize(Optional = true)] public DifficultyRow[] Economy =
    [
        new() { Level = "free_build" },
        new() { Level = "fast" },
        new() { Level = "normal", ContractFees = 1.2f, TechValue = 0.9f },
        new() { Level = "slow", ContractFees = 1.4f, TechValue = 0.8f },
    ];
    /// <summary>Spec 680: pressure factors for each vanilla crew scarcity level.</summary>
    [Serialize(Optional = true)] public DifficultyRow[] Crew =
    [
        new() { Level = "max", HeatRate = 0.9f },
        new() { Level = "plentiful", HeatRate = 0.95f },
        new() { Level = "standard" },
        new() { Level = "scarce", HeatRate = 1.1f },
    ];

    /// <summary>
    /// Spec 686, prototype: hunters get one more tier for each vanilla fame title tier above the system tier, up to FameTierCap.
    /// Off by default. SPEC.md section 30 has the measured effect.
    /// </summary>
    [Serialize(Optional = true)] public bool FameTitleTiers;
    /// <summary>Spec 686: most extra hunter tiers that the fame title gives.</summary>
    [Serialize(Optional = true)] public int FameTierCap = 2;
    /// <summary>Spec 688: during the last heat stage (Strike), the music plays its battle layers. Off: vanilla music.</summary>
    [Serialize(Optional = true)] public bool StrikeMusic = true;

    /// <summary>The rows of one difficulty setting.</summary>
    public DifficultyRow[] Rows(DifficultySetting setting) => setting switch
    {
        DifficultySetting.Combat => Combat,
        DifficultySetting.Economy => Economy,
        DifficultySetting.Crew => Crew,
        _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, null),
    };

    /// <summary>Called from TunablesFile.Validate (spec 53). knownLevel is null before the game rules load.</summary>
    public void Validate(Action<bool, string> require, Func<DifficultySetting, string, bool>? knownLevel)
    {
        foreach (var setting in Enum.GetValues<DifficultySetting>())
        {
            var rows = Rows(setting);
            foreach (var r in rows)
            {
                string name = $"RunModes.{setting} '{r.Level}'";
                require(r.HeatRate >= 0 && r.HunterShips > 0 && r.ContractFees >= 0 && r.TechValue > 0,
                    $"{name}: HeatRate and ContractFees must be 0 or more, HunterShips and TechValue above 0");
                require(knownLevel?.Invoke(setting, r.Level) ?? true, $"{name}: no vanilla {setting} level has this ID");
            }
            require(rows.Select(r => r.Level).Distinct().Count() == rows.Length, $"RunModes.{setting} has a level twice");
        }
        require(FameTierCap >= 0, "RunModes.FameTierCap must not be negative");
    }
}

/// <summary>The three vanilla difficulty settings of a career.</summary>
public enum DifficultySetting { Combat, Economy, Crew }

/// <summary>Spec 680: pressure factors for one vanilla difficulty level. 1 (and 0 tiers) means no change.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class DifficultyRow
{
    /// <summary>Vanilla level ID (career.rules: CombatDifficultyLevels, EconDifficultyLevels, CrewScarcityLevels).</summary>
    [Serialize] public string Level = "";
    /// <summary>Times the heat from time and from fame (spec 24, 25).</summary>
    [Serialize(Optional = true)] public float HeatRate = 1f;
    /// <summary>Times the ships of each hunter stage response (spec 190), rounded, at least 1.</summary>
    [Serialize(Optional = true)] public float HunterShips = 1f;
    /// <summary>Added to the tier of each hunter stage response (spec 190).</summary>
    [Serialize(Optional = true)] public int HunterTiers;
    /// <summary>Times the contract accept fee (spec 102). The completion bonus follows the fee.</summary>
    [Serialize(Optional = true)] public float ContractFees = 1f;
    /// <summary>Times the tech value (spec 19), so also the sale price and the Buyer price.</summary>
    [Serialize(Optional = true)] public float TechValue = 1f;

    public Pressure ToPressure() => new(HeatRate, HunterShips, HunterTiers, ContractFees, TechValue);
}

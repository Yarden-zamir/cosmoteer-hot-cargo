using System.Globalization;
using Cosmoteer;
using Cosmoteer.Modes.Career;
using HotCargo.Core;

namespace HotCargo.RunModes;

/// <summary>
/// Spec 680 to 684: effective pressure values. The tunables keep their values; the code multiplies at each read with the factors of the
/// career's current difficulty levels. The levels are confirmed MP values and the table is in the run tunables, so all machines agree.
/// </summary>
public static class Difficulty
{
    /// <summary>The combined factors of the career's current combat, economy and crew levels, and of the run's start job (spec 1201).</summary>
    public static Pressure Pressure(this Run run) => For(run.T.RunModes, Levels(run.Mode)).Combine(StartJobs.StartJobs.Pressure(run));

    /// <summary>Spec 682: tiers that the difficulty adds to each hunter stage response.</summary>
    public static int ExtraHunterTiers(Run run) => run.Pressure().HunterTiers;

    /// <summary>The vanilla level ID of each setting, in setting order.</summary>
    public static (DifficultySetting Setting, string Level)[] Levels(CareerGameModeManager mode) =>
    [
        (DifficultySetting.Combat, mode.CombatDifficulty.ID.ToString()),
        (DifficultySetting.Economy, mode.EconDifficulty.ID.ToString()),
        (DifficultySetting.Crew, mode.CrewScarcity.ID.ToString()),
    ];

    /// <summary>A level without a row counts as Neutral, so a difficulty level that another mod adds plays like the vanilla default.</summary>
    public static Pressure For(RunModesTunables t, IEnumerable<(DifficultySetting Setting, string Level)> levels) =>
        PressureModel.Combine(levels.Select(l => t.Rows(l.Setting).FirstOrDefault(r => r.Level == l.Level)?.ToPressure() ?? RunModes.Pressure.Neutral));

    /// <summary>True when the vanilla rules have a level with this ID. For tunables validation.</summary>
    public static bool IsVanillaLevel(DifficultySetting setting, string level)
    {
        var rules = GameApp.Rules.CareerMode;
        return setting switch
        {
            DifficultySetting.Combat => rules.CombatDifficultyLevels.Any(l => l.ID.ToString() == level),
            DifficultySetting.Economy => rules.EconDifficultyLevels.Any(l => l.ID.ToString() == level),
            DifficultySetting.Crew => rules.CrewScarcityLevels.Any(l => l.ID.ToString() == level),
            _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, null),
        };
    }

    /// <summary>Spec 684: the intro line with the level names and the factors that differ from 1. Null when every factor is 1.</summary>
    public static string? IntroLine(Run run)
    {
        var parts = FactorTexts(run.Pressure());
        return parts.Count == 0 ? null : Fmt("DifficultyFmt", LevelNames(run.Mode), string.Join(", ", parts));
    }

    /// <summary>Spec 684: the heat panel tooltip. The level names, then one line for each factor that differs from 1, or a "no change" line.</summary>
    public static string Tooltip(Run run)
    {
        var parts = FactorTexts(run.Pressure());
        string tip = Fmt("TipFmt", LevelNames(run.Mode), parts.Count == 0 ? Text("TipNeutral") : string.Join("\n", parts));
        // Spec 1205: the factors above include the job's; this line names it.
        return StartJobs.StartJobs.Job(run) is { } job ? $"{tip}\n{StartJobs.StartJobs.Format("TipFmt", StartJobs.StartJobs.Name(job))}" : tip;
    }

    static string LevelNames(CareerGameModeManager m) =>
        $"{m.CombatDifficulty.NameKey.Text} / {m.EconDifficulty.NameKey.Text} / {m.CrewScarcity.NameKey.Text}";

    /// <summary>One text for each factor that differs from 1.</summary>
    internal static List<string> FactorTexts(Pressure p)
    {
        var parts = new List<string>();
        if (p.HeatRate != 1f) parts.Add(Fmt("HeatFmt", p.HeatRate));
        if (p.HunterShips != 1f) parts.Add(Fmt("ShipsFmt", p.HunterShips));
        if (p.HunterTiers != 0) parts.Add(Fmt("TiersFmt", p.HunterTiers.ToString("+0;-0", CultureInfo.InvariantCulture)));
        if (p.ContractFees != 1f) parts.Add(Fmt("FeesFmt", p.ContractFees));
        if (p.TechValue != 1f) parts.Add(Fmt("TechFmt", p.TechValue));
        return parts;
    }

    internal static string Fmt(string key, params object?[] args) => Cosmoteer.Localization.Strings.FormatText("HotCargoRunModes/" + key, args);
    internal static string Text(string key) => Cosmoteer.Localization.Strings.GetText("HotCargoRunModes/" + key);
}

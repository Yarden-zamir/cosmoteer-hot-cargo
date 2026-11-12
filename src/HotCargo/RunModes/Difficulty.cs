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
    /// <summary>The combined factors of the career's current combat, economy and crew levels.</summary>
    public static Pressure Pressure(this Run run) => For(run.T.RunModes, Levels(run.Mode));

    /// <summary>Spec 682 and 686: tiers added to each hunter stage response: the difficulty tiers, plus the fame title tiers when that prototype is on.</summary>
    public static int ExtraHunterTiers(Run run) =>
        run.Pressure().HunterTiers + (run.T.RunModes.FameTitleTiers ? FameTierBonus(run) : 0);

    /// <summary>Spec 686: the tier of the current vanilla fame title (its WantedPoliceTier) above the system tier, up to FameTierCap.</summary>
    public static int FameTierBonus(Run run) =>
        PressureModel.FameTierBonus(run.Mode.GetCurrentFameTitle().WantedPoliceTier, run.CurrentTier, run.T.RunModes.FameTierCap);

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
        var p = run.Pressure();
        if (p.IsNeutral) return null;
        var m = run.Mode;
        string levels = $"{m.CombatDifficulty.NameKey.Text} / {m.EconDifficulty.NameKey.Text} / {m.CrewScarcity.NameKey.Text}";
        var parts = new List<string>();
        if (p.HeatRate != 1f) parts.Add(Fmt("HeatFmt", p.HeatRate));
        if (p.HunterShips != 1f) parts.Add(Fmt("ShipsFmt", p.HunterShips));
        if (p.HunterTiers != 0) parts.Add(Fmt("TiersFmt", p.HunterTiers.ToString("+0;-0", CultureInfo.InvariantCulture)));
        if (p.ContractFees != 1f) parts.Add(Fmt("FeesFmt", p.ContractFees));
        if (p.TechValue != 1f) parts.Add(Fmt("TechFmt", p.TechValue));
        return Fmt("DifficultyFmt", levels, string.Join(", ", parts));
    }

    internal static string Fmt(string key, params object?[] args) => Cosmoteer.Localization.Strings.FormatText("HotCargoRunModes/" + key, args);
    internal static string Text(string key) => Cosmoteer.Localization.Strings.GetText("HotCargoRunModes/" + key);
}

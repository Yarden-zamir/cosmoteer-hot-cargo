using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Cosmoteer.Ships;
using Halfling;
using HotCargo.Contracts;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Hunters;
using HotCargo.Tech;

namespace HotCargo.RunModes;

/// <summary>
/// Entry point of the RunModes area (spec 680 to 689): difficulty pressure, Ironman losses, Strike music and the fame title prototype.
/// Mod.Boot calls Register once at startup.
/// </summary>
public static class RunModesArea
{
    public static void Register() => RegisterAutotest();

    /// <summary>Lines for the run intro: the difficulty factors (spec 684) and the Ironman rule (spec 685). Empty when neither applies.</summary>
    public static string IntroLines(Run run)
    {
        string lines = Difficulty.IntroLine(run) is { } d ? $"\n\n{d}" : "";
        return run.Mode.IsIronmanModeEnabled ? $"{lines}\n\n<bad>{Difficulty.Text("IronmanIntro")}</bad>" : lines;
    }

    /// <summary>
    /// Spec 684: the difficulty tooltip on the heat panel, for new and loaded runs, live after a level change.
    /// The panel takes hover input for it (Auto); its labels and bar let input pass to it. The game reads a tooltip's TextProvider
    /// once and then waits for change events, and it opens a tooltip only when that text is not empty. So the text is set each frame.
    /// </summary>
    internal static void AttachHeatPanelTip(Halfling.Gui.Widget panel, CareerGameModeManager mode)
    {
        panel.InputBlockMode = Halfling.Gui.InputBlockMode.Auto;
        panel.BeforeFrameDraw += (_, _) => Errors.Guard("heat panel tooltip", () =>
            panel.ToolTips.Text = Run.Current is { } run && run.Mode == mode ? Difficulty.Tooltip(run) : "");
    }

    /// <summary>Lines for the win screen (spec 685).</summary>
    public static string WinLines(Run run)
    {
        string line = Ironman.WinLine(run);
        if (line.Length > 0) _winLinesShown++;
        return line;
    }

    static int _winLinesShown;
    static double _feeRatio = double.NaN;
    static double _markFeeScale = double.NaN;
    static float _markHeat;
    static double _markSeconds;

    static Run R() => Autotest.RequireRun();

    static void RegisterAutotest()
    {
        Autotest.Fields["rm_heatrate"] = () => R().Pressure().HeatRate;
        Autotest.Fields["rm_ships"] = () => R().Pressure().HunterShips;
        Autotest.Fields["rm_tiers"] = () => Difficulty.ExtraHunterTiers(R());
        Autotest.Fields["rm_fees"] = () => R().Pressure().ContractFees;
        Autotest.Fields["rm_tech"] = () => R().Pressure().TechValue;
        // Spec 683: |effective tech value ÷ the value without the difficulty factor − the factor|. Rounding to 100 keeps it near 0.
        Autotest.Fields["rm_techgap"] = () => Math.Abs(R().TechValue() / (double)BaseTechValue(R()) - R().Pressure().TechValue);
        // Spec 682: |sum of the re-rolled fees ÷ sum of the stored fees ÷ the expected ratio − 1|, after rm_feemark and rm_feecheck.
        // The expected ratio is the change of the vanilla money factor (fees follow the pay, spec 102) x the change of the fee factor.
        Autotest.Fields["rm_feegap"] = () => Math.Abs(_feeRatio / (FeeScale(R()) / _markFeeScale) - 1);
        // Spec 681: |heat per minute that the sim tick added since rm_ratemark ÷ the effective rate − 1|. No fame and no contracts in between.
        Autotest.Fields["rm_rategap"] = () =>
        {
            var run = R();
            double minutes = (run.State.GameSeconds - _markSeconds) / 60.0;
            if (minutes <= 0) throw new InvalidOperationException("no game time since rm_ratemark");
            double expected = run.HeatMultiplier(run.State.FleetTech) * run.T.Heat.HeatPerGameMinute;
            return Math.Abs((run.State.Heat - _markHeat) / minutes / expected - 1);
        };
        // Vanilla difficulty levels without a row in the tables. 0 means the tables cover every vanilla level.
        Autotest.Fields["rm_levels_without_row"] = () => LevelsWithoutRow(R().T.RunModes);
        Autotest.Fields["rm_fametier"] = () => R().Mode.GetCurrentFameTitle().WantedPoliceTier;
        Autotest.Fields["rm_famebonus"] = () => Difficulty.FameTierBonus(R());
        Autotest.Fields["ironman"] = () => R().Mode.IsIronmanModeEnabled ? 1 : 0;
        Autotest.Fields["ironmansave"] = () => File.Exists(R().Mode.IronmanSavePath) ? 1 : 0;
        Autotest.Fields["lastsave"] = () => R().Game.LastSaveFile is { } f && File.Exists(f) ? 1 : 0;
        Autotest.Fields["ironmandeleted"] = () => Ironman.Deleted;
        Autotest.Fields["ironmanlosslines"] = () => Ironman.LossLinesShown;
        Autotest.Fields["ironmanwinlines"] = () => _winLinesShown;
        Autotest.Fields["strikemusic"] = () => StrikeMusic.Active ? 1 : 0;
        // The vanilla battle flag of the music. -1 when the game has no music stream.
        Autotest.Fields["musicbattle"] = () => GameApp.Music is { } m ? m.Parameters.Battle ? 1 : 0 : -1;

        // rm_set <field> <value>: sets one RunModes tunable for this run. Test only; use "mp rm_set" in co-op.
        Autotest.Commands["rm_set"] = arg => Autotest.SetField(R().T.RunModes, arg);
        // rm_difficulty <combat> <economy> <crew>: sets the career's levels through the vanilla settings inputs (host only), like the settings tab.
        Autotest.Commands["rm_difficulty"] = arg =>
        {
            var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (a.Length != 3) throw new InvalidOperationException("rm_difficulty needs: combat economy crew");
            var net = R().Game.NetManager;
            net.EnqueueInput(new CareerModeSettingsTab.MPSetCombatDifficultyInput(new ID<CombatDifficultyRules>(a[0])));
            net.EnqueueInput(new CareerModeSettingsTab.MPSetEconDifficultyInput(new ID<EconDifficultyRules>(a[1])));
            net.EnqueueInput(new CareerModeSettingsTab.MPSetCrewScarcityInput(new ID<CrewScarcityRules>(a[2])));
            Autotest.WaitFor(() => Difficulty.Levels(R().Mode).Select(l => l.Level).SequenceEqual(a), 10, $"difficulty {arg}");
        };
        // rm_feecheck: drops the stored terms of every paid offer, rolls them again at the current difficulty and stores the ratio of the fee sums.
        Autotest.Commands["rm_feecheck"] = _ => FeeCheck(R());
        // rm_feemark: remembers the fee scale of the levels at which the stored offers rolled their terms. Run it before a level change.
        Autotest.Commands["rm_feemark"] = _ => _markFeeScale = FeeScale(R());
        Autotest.Commands["rm_ratemark"] = _ => (_markHeat, _markSeconds) = (R().State.Heat, R().State.GameSeconds);
        // rm_check_levels: sets each vanilla level in turn (the other settings at their defaults) and checks that every effective value uses
        // the factor of that level's row. Single player only: it sets the levels from the frame, not through the settings input.
        Autotest.Commands["rm_check_levels"] = _ => CheckLevels(R());
        // rm_expect_stage <stage>: the live hunters match one response of the stage at the current difficulty: the ship count, and the
        // tier (a ship pool without that tier gives a lower one). Start with no hunters.
        Autotest.Commands["rm_expect_stage"] = arg =>
        {
            var run = R();
            var (ships, tier) = ExpectedStage(run, arg);
            var sim = run.Game.Sim;
            var career = (CareerSimModeManager)sim.Mode;
            var tiers = HunterSpawner.Hunters(sim).Select(h => career.GetShipTier(h) ?? -1).ToList();
            Autotest.Check(tiers.Count == ships, $"stage {arg}: {ships} hunters (actual {tiers.Count})");
            Autotest.Check(tiers.All(t => t <= tier && t >= Math.Max(1, tier - 2)), $"stage {arg}: hunter tiers near {tier} (actual [{string.Join(", ", tiers)}])");
        };
        Autotest.Commands["rm_log"] = arg =>
        {
            var run = R();
            var p = run.Pressure();
            Log.Info($"AUTOTEST RUNMODES {arg}: levels=[{string.Join(", ", Difficulty.Levels(run.Mode).Select(l => l.Level))}] heatRate={p.HeatRate:0.###} " +
                     $"ships={p.HunterShips:0.###} tiers={p.HunterTiers} fees={p.ContractFees:0.###} tech={p.TechValue:0.###} heatMult={run.HeatMultiplier(run.State.FleetTech):0.###} " +
                     $"techValue={run.TechValue()} baseTech={BaseTechValue(run)} intro=\"{Difficulty.IntroLine(run)}\"");
        };
        // rm_check_paneltip: the heat panel tooltip names the three current levels and lists exactly the factors that are not 1 (spec 684).
        Autotest.Commands["rm_check_paneltip"] = _ => CheckPanelTip(R());
        // rm_showpaneltip: shows the heat panel tooltip as if the mouse were over the panel. For screenshots.
        Autotest.Commands["rm_showpaneltip"] = _ =>
        {
            var panel = HeatPanelOf(R());
            (panel.GetToolTipDisplay() ?? throw new InvalidOperationException("the heat panel has no tooltip display")).SetToolTipSource(panel.ToolTips);
            Autotest.Wait(1.5);
        };
        // 1 when the game's tooltip box is open on the heat panel and its label holds the difficulty text.
        Autotest.Fields["rm_tipshown"] = () =>
        {
            var box = R().Game.Gui.ToolTipRight;
            return box.ToolTipDisplayController.CurrentSource == HeatPanelOf(R()).ToolTips && box.IsActive
                && box.PrimaryToolTip.TooltipLabel.Text == Difficulty.Tooltip(R()) ? 1 : 0;
        };
        // rm_tipshot <name>: a screenshot that keeps the open tooltips (the screenshot command hides them).
        Autotest.Commands["rm_tipshot"] = arg => { Ux.Screenshot.Capture(arg, keepToolTips: true); Autotest.Wait(1.5); };
        // rm_ironman_newgame <name>: a new Ironman career on the run map with the save HotCargoTest_<name>. Test only.
        Autotest.Commands["rm_ironman_newgame"] = arg =>
        {
            var rules = GameApp.Rules.CareerMode;
            ShipFile ship = rules.StarterShips[0].Ship;
            int Default<T>(T[] levels, Func<T, bool> isDefault) => Array.FindIndex(levels, l => isDefault(l)) is var i and >= 0 ? i
                : throw new InvalidOperationException("no default difficulty level in career rules");
            var path = new Halfling.IO.AbsolutePath((string)(Paths.SavedGamesFolder / $"HotCargoTest_{arg}.savedgame"));
            var init = new CareerInit(null, Journey.JourneyArea.RunMapSizeIndex(), Default(rules.CombatDifficultyLevels, l => l.IsDefault),
                Default(rules.EconDifficultyLevels, l => l.IsDefault), Default(rules.CrewScarcityLevels, l => l.IsDefault),
                false, false, false, false, enableIronmanMode: true, path, 12345UL, (ship.ShipName, Ship.LoadBytes(ship.FilePath)));
            if (Autotest.Game is { } old) Halfling.App.Director.PopState(old);
            Halfling.App.Director.PushState(init.CreateGame(Cosmoteer.Game.Multiplayer.SPManager.Create));
            Autotest.Wait(5);
        };
        // rm_ironman_save: saves the career to its Ironman save, like an Ironman autosave. Test only.
        Autotest.Commands["rm_ironman_save"] = _ => { var run = R(); run.Game.SaveAs(run.Mode.IronmanSavePath); Autotest.Wait(2); };
        RegisterFameProbe();
    }

    static long BaseTechValue(Run run)
    {
        var (t, s) = (run.T.Tech, run.State);
        return TechModel.Value(GameData.TierValueMaximum(s.PeakTier), t.TechShipFraction, t.TechShipFractionPerGameHour,
            t.TechShipFractionPer100Notoriety, t.PriceStepMinutes, t.NotorietyStep, s.GameSeconds, s.Notoriety);
    }

    /// <summary>The rows of the career's current levels, looked up straight in the tunables. A level without a row counts as all 1.</summary>
    static DifficultyRow[] RowsNow(Run run) => Difficulty.Levels(run.Mode)
        .Select(l => run.T.RunModes.Rows(l.Setting).FirstOrDefault(r => r.Level == l.Level) ?? new DifficultyRow { Level = l.Level }).ToArray();

    /// <summary>Ships and tier of one response of a stage, from the stage scaling (spec 190) and the rows of the current levels.</summary>
    static (int Ships, int Tier) ExpectedStage(Run run, string stage)
    {
        var r = run.T.Hunters.Stages.Single(s => s.Name == stage);
        var rows = RowsNow(run);
        float progress = WaveModel.Progress(run.State.SectorIndex, run.T.Journey.RunSectorCount);
        float shipFactor = rows.Aggregate(1f, (f, row) => f * row.HunterShips);
        int ships = Math.Max(1, (int)MathF.Round(WaveModel.ShipCount(r.ShipCount, r.EarlyScale, progress) * shipFactor, MidpointRounding.AwayFromZero));
        int fame = run.T.RunModes.FameTitleTiers ? Difficulty.FameTierBonus(run) : 0;
        int tier = run.CurrentTier + WaveModel.TierOffset(r.TierOffset, r.EarlyTierReduction, progress) + rows.Sum(row => row.HunterTiers) + fame;
        return (ships, tier);
    }

    static void CheckLevels(Run run)
    {
        var mode = run.Mode;
        var rules = mode.Rules;
        var (combat, econ, crew) = (mode.CombatDifficulty.ID, mode.EconDifficulty.ID, mode.CrewScarcity.ID);
        foreach (var l in rules.CombatDifficultyLevels) { mode.SetCombatDifficulty(l.ID, confirmed: true); CheckLevel(run, $"combat {l.ID}"); }
        mode.SetCombatDifficulty(combat, confirmed: true);
        foreach (var l in rules.EconDifficultyLevels) { mode.SetEconDifficulty(l.ID, confirmed: true); CheckLevel(run, $"economy {l.ID}"); }
        mode.SetEconDifficulty(econ, confirmed: true);
        foreach (var l in rules.CrewScarcityLevels) { mode.SetCrewScarcity(l.ID, confirmed: true); CheckLevel(run, $"crew {l.ID}"); }
        mode.SetCrewScarcity(crew, confirmed: true);
    }

    static void CheckLevel(Run run, string label)
    {
        var rows = RowsNow(run);
        float heatRate = rows.Aggregate(1f, (f, r) => f * r.HeatRate);
        float techFactor = rows.Aggregate(1f, (f, r) => f * r.TechValue);
        float fees = rows.Aggregate(1f, (f, r) => f * r.ContractFees);
        int units = run.State.FleetTech;
        double heatRatio = run.HeatMultiplier(units) / HeatModel.Multiplier(run.T.Heat.HeatMultiplierByUnits, units);
        Autotest.Check(Math.Abs(heatRatio - heatRate) < 1e-4, $"{label}: heat multiplier x {heatRate:0.###} (actual x {heatRatio:0.###})");
        long baseTech = BaseTechValue(run), tech = run.TechValue();
        long expectedTech = (long)Math.Round(baseTech * (double)techFactor / 100.0, MidpointRounding.AwayFromZero) * 100;
        Autotest.Check(tech == expectedTech, $"{label}: tech value {baseTech} x {techFactor:0.###} = {expectedTech} (actual {tech})");
        int sell = run.Game.Sim.Mode.GetResourceSellRefund(Vaults.TechId, null);
        Autotest.Check(sell == tech, $"{label}: station tech price {tech} (actual {sell})");
        Autotest.Check(Math.Abs(run.Pressure().ContractFees - fees) < 1e-4, $"{label}: contract fee factor {fees:0.###}");
        var stages = run.T.Hunters.Stages.Select(s =>
        {
            var (ships, tier) = ExpectedStage(run, s.Name);
            var r = s;
            float progress = WaveModel.Progress(run.State.SectorIndex, run.T.Journey.RunSectorCount);
            int actualShips = run.Pressure().Ships(WaveModel.ShipCount(r.ShipCount, r.EarlyScale, progress));
            int actualTier = run.CurrentTier + WaveModel.TierOffset(r.TierOffset, r.EarlyTierReduction, progress) + Difficulty.ExtraHunterTiers(run);
            Autotest.Check(actualShips == ships && actualTier == tier, $"{label}: {s.Name} {ships} ships of tier {tier} (actual {actualShips} of tier {actualTier})");
            return $"{s.Name} {ships}xt{tier}";
        });
        Log.Info($"AUTOTEST RUNMODES level {label}: heat x{heatRate:0.###}, fees x{fees:0.###}, tech x{techFactor:0.###} ({tech}), {string.Join(", ", stages)}");
    }

    static Ui.HeatPanel HeatPanelOf(Run run) =>
        run.Game.Gui.TopLeftHudBox.Children.OfType<Ui.HeatPanel>().SingleOrDefault() ?? throw new InvalidOperationException("no heat panel in the HUD");

    static void CheckPanelTip(Run run)
    {
        // The text that the game's tooltip box shows, not our own source.
        string tip = ((Halfling.Gui.Components.ToolTips.IToolTipSource)HeatPanelOf(run).ToolTips).TextProvider.Text;
        Log.Info($"AUTOTEST RUNMODES panel tip: {tip.Replace("\n", " | ")}");
        var m = run.Mode;
        foreach (string level in new[] { m.CombatDifficulty.NameKey.Text, m.EconDifficulty.NameKey.Text, m.CrewScarcity.NameKey.Text })
            Autotest.Check(tip.Contains(level), $"panel tip names the level {level}");
        var p = run.Pressure();
        var factors = new (string Key, bool Changed, object Value)[]
        {
            ("HeatFmt", p.HeatRate != 1f, p.HeatRate), ("ShipsFmt", p.HunterShips != 1f, p.HunterShips),
            ("TiersFmt", p.HunterTiers != 0, p.HunterTiers.ToString("+0;-0", CultureInfo.InvariantCulture)),
            ("FeesFmt", p.ContractFees != 1f, p.ContractFees), ("TechFmt", p.TechValue != 1f, p.TechValue),
        };
        foreach (var (key, changed, value) in factors)
            Autotest.Check(tip.Contains(Difficulty.Fmt(key, value)) == changed, $"panel tip {(changed ? "lists" : "leaves out")} {key} {value}");
        Autotest.Check(tip.Contains(Difficulty.Text("TipNeutral")) == factors.All(f => !f.Changed), "panel tip says 'no change' only when every factor is 1");
    }

    static int LevelsWithoutRow(RunModesTunables t)
    {
        var rules = GameApp.Rules.CareerMode;
        var vanilla = rules.CombatDifficultyLevels.Select(l => (DifficultySetting.Combat, l.ID.ToString()))
            .Concat(rules.EconDifficultyLevels.Select(l => (DifficultySetting.Economy, l.ID.ToString())))
            .Concat(rules.CrewScarcityLevels.Select(l => (DifficultySetting.Crew, l.ID.ToString())));
        var missing = vanilla.Where(v => t.Rows(v.Item1).All(r => r.Level != v.Item2)).ToList();
        foreach (var (setting, level) in missing) Log.Info($"AUTOTEST RUNMODES no row for {setting} level '{level}'");
        return missing.Count;
    }

    /// <summary>The factors of a fee besides the roll and the tier: the vanilla money factor, RewardScale and the difficulty fee factor.</summary>
    static double FeeScale(Run run) => run.Mode.EconDifficulty.MoneyRewardFactor * run.T.Journey.RewardScale * run.Pressure().ContractFees;

    static void FeeCheck(Run run)
    {
        long before = 0, after = 0;
        int offers = 0;
        foreach (var (m, _) in ContractBook.Offers(run.Game.Sim))
        {
            if (ContractBook.Find(run, m) is not { Fee: > 0 } stored) continue;
            run.State.Contracts.Terms.Remove(m.MissionID);
            var rolled = ContractBook.Preview(run, m) ?? throw new InvalidOperationException($"offer {m.MissionID} is no contract after the re-roll");
            before += stored.Fee;
            after += rolled.Fee;
            offers++;
        }
        if (offers == 0) throw new InvalidOperationException("no paid offer in this system");
        _feeRatio = after / (double)before;
        Log.Info($"AUTOTEST RUNMODES fees: {offers} paid offers, stored {before}, re-rolled {after}, ratio {_feeRatio:0.###}");
    }

    /// <summary>
    /// Spec 687: the fame title probe. rm_missions gives the fame of typical missions with the vanilla fame cap of the system tier;
    /// rm_fame logs the fame title tier and the Strike tier with the prototype off and on. rm_route jumps along the main route.
    /// </summary>
    static void RegisterFameProbe()
    {
        // rm_missions <count> [std fame per mission, default 3.4 (BALANCE.md median)]: one vanilla FameReward per mission, with the cap
        // of the system tier. The cap of tier T is the fame of fame title T (FameTitles[T].Fame = TIER_TABLES MaxFame of tier T).
        Autotest.Commands["rm_missions"] = arg =>
        {
            var run = R();
            var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int count = int.Parse(a[0], CultureInfo.InvariantCulture);
            float perMission = a.Length > 1 ? float.Parse(a[1], CultureInfo.InvariantCulture) : 3.4f;
            int tier = run.CurrentTier;
            var titles = run.Mode.Rules.FameTitles;
            int cap = titles[Math.Clamp(tier, 0, titles.Length - 1)].Fame;
            int fame = (int)MathF.Round(perMission * GameData.StandardMissionFame(tier));
            for (int i = 0; i < count; i++) new FameReward(fame, cap).GiveReward(run.Game);
        };
        // rm_sector <missions>: logs the FAME line on arrival and after each of the missions (3.4 standard mission fame each).
        Autotest.Commands["rm_sector"] = arg =>
        {
            int missions = int.Parse(arg, CultureInfo.InvariantCulture);
            int sector = R().State.SectorIndex;
            Autotest.Commands["rm_fame"]($"s{sector} m0");
            for (int i = 1; i <= missions; i++)
            {
                Autotest.Commands["rm_missions"]("1");
                Autotest.Commands["rm_fame"]($"s{sector} m{i}");
            }
        };
        Autotest.Commands["rm_fame"] = arg =>
        {
            var run = R();
            var strike = run.T.Hunters.Stages[^1];
            float progress = WaveModel.Progress(run.State.SectorIndex, run.T.Journey.RunSectorCount);
            int baseOffset = WaveModel.TierOffset(strike.TierOffset, strike.EarlyTierReduction, progress);
            int bonus = Difficulty.FameTierBonus(run);
            Log.Info($"AUTOTEST FAME {arg}: sector={run.State.SectorIndex} tier={run.CurrentTier} fame={run.Mode.PlayerFame} " +
                     $"titleTier={run.Mode.GetCurrentFameTitle().WantedPoliceTier} bonus={bonus} strikeTierOff={run.CurrentTier + baseOffset} " +
                     $"strikeTierOn={run.CurrentTier + baseOffset + bonus}");
        };
        // rm_route [low]: jumps to the unvisited neighbor with the highest low tier (the main route), or the lowest with "low". No fuel.
        Autotest.Commands["rm_route"] = arg =>
        {
            var run = R();
            int Tier(Cosmoteer.GalaxyMap.MapNode n) => run.Mode.GetMapNodeData(n).TierRange?.Min ?? 0;
            var options = run.Game.CurrentMapNode.Connections.Where(n => !run.State.VisitedNodes.Contains(n.UniqueID)).OrderBy(n => n.UniqueID).ToList();
            // Faction home systems (low tier above what HotCargoTierCap may change) are not on the route.
            int maxTier = GameApp.Rules.CareerMode.MapSizes[Journey.JourneyArea.RunMapSizeIndex()].MapGenerator.Spawners
                .OfType<Journey.TierCapSpawner>().Single().MaxChangeableLowTier;
            var next = (arg == "low" ? options.OrderBy(Tier) : options.Where(n => Tier(n) <= maxTier).OrderByDescending(Tier)).FirstOrDefault()
                ?? throw new InvalidOperationException("no unvisited neighbor system");
            run.Game.JumpToNode(next, doFtlAnimation: false, run.Game.Sim.Ships.HumanPlayerShips.ToList())
                .ContinueWith(t => Log.Error("AUTOTEST rm_route failed", t.Exception), TaskContinuationOptions.OnlyOnFaulted);
        };
    }
}

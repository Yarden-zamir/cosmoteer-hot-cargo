using Cosmoteer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Tech;

/// <summary>
/// Spec 1016-1018: builds Tech Vaults into saved ship designs with the game's own load, build and save code.
/// Autotest command: vaultdesigns &lt;source folder&gt;|&lt;output folder&gt;. It needs a running game (the build steps need a sim)
/// and graphics (the game saves a design as a picture of the ship), so it runs in the main slot.
/// Each *.ship.png in the source folder (and its subfolders) gets vaults for the starting tech, and the design goes to the same
/// relative path in the output folder. The output folder may be the source folder. The report goes to vault-report.txt in the mod folder.
/// </summary>
static class VaultDesigns
{
    internal static void Register()
    {
        Autotest.Commands["vaultdesigns"] = Run;
        Autotest.Commands["newgamecheck"] = arg => NewGameCheck.Off = arg == "off";
        // shipcheck preset|vanilla N: the career new-game check on starter ship N, from the mod (preset) or the vanilla Data folder.
        // A refused ship shows the vanilla "invalid ship" dialog, like the Load Ship menu of the new-game screen.
        Autotest.Commands["shipcheck"] = arg =>
        {
            var file = StarterFile(arg);
            using var ship = file.LoadShipBlueprints();
            _checkPassed = GameApp.Rules.CareerMode.IsShipDesignLegal(ship);
            _checkVaults = NewGameCheck.VaultCapacity(ship);
            Log.Info($"AUTOTEST shipcheck {file.FilePath}: legal={_checkPassed} vault capacity={_checkVaults}");
            if (!_checkPassed) _ = Halfling.Gui.Dialogs.OneButtonDialog.Show(Cosmoteer.Localization.Strings.GetText("GameSetupScreen/InvalidShip"));
        };
        // vaultbuild preset|vanilla N: loads starter ship N into the sim next to the player ship and builds vaults (spec 441).
        // Then it runs PowerUpSeconds like vaultdesigns, records whether the ship still matches its blueprint, and removes it. Headless, no save.
        Autotest.Commands["vaultbuild"] = arg =>
        {
            var game = Autotest.Game ?? throw new InvalidOperationException("vaultbuild needs a running game");
            var file = StarterFile(arg);
            var (ship, line) = Build(new Batch(game, "", "", NewGameCheck.RequiredUnits(coop: false), new(), []), (string)file.FilePath);
            Log.Info($"AUTOTEST vaultbuild {file.FilePath}: {line}");
            _built = ship is null ? 0 : ship.BlueprintParts.Count(p => p.Rules.ID == Vaults.VaultId);
            _matches = false;
            if (ship is null) return;
            double until = Halfling.App.Clock.Time.Seconds + PowerUpSeconds;
            Autotest.WaitFor(() => Halfling.App.Clock.Time.Seconds >= until, PowerUpSeconds + 30, "vaultbuild power-up", () =>
            {
                _matches = ship.PhysicalMatchesBlueprints && ship.OptionalConstructionManager?.HasAnyNonRepairsQueued != true;
                if (!_matches) Log.Info($"AUTOTEST vaultbuild mismatch: physical [{string.Join(" ", ship.Parts.UnmatchedParts.Select(p => $"{p.Rules.ID}@{p.Location}"))}] " +
                    $"blueprint [{string.Join(" ", ship.BlueprintParts.UnmatchedParts.Select(p => $"{p.Rules.ID}@{p.Location}"))}] doors={ship.Doors.MatchesBlueprints}");
                game.Sim.Ships.Remove(ship, dispose: true, handleLostShip: false);
            });
        };
        Autotest.Fields["designmatches"] = () => _matches ? 1 : 0;
        Autotest.Fields["vaultsbuiltindesign"] = () => _built;
        Autotest.Fields["shiplegal"] = () => _checkPassed ? 1 : 0;
        Autotest.Fields["shipvaulttech"] = () => _checkVaults;
        // 1 when the top dialog explains the missing vaults (spec 1012). Closes the dialog.
        Autotest.Fields["vaultdialog"] = () =>
        {
            // Test slots run without Steam, so a Steam notice dialog can sit below ours.
            var dialog = Halfling.App.Director.States.OfType<Halfling.Gui.Dialogs.OneButtonDialog>()
                .FirstOrDefault(d => d.MessageLabel.Text?.Contains("Tech Vaults for") == true);
            if (dialog is null) return 0;
            Halfling.App.Director.PopState(dialog);
            return 1;
        };
        // Spec 1015: StartingResearch techs that the run has unlocked.
        Autotest.Fields["startresearch"] = () =>
        {
            var run = Autotest.RequireRun();
            return run.T.Tech.StartingResearch.Count(id => run.Mode.Techs.IsTechUnlocked(new Cosmoteer.Data.ID<Cosmoteer.Modes.Career.TechTree.TechRules>(id)));
        };
    }

    /// <summary>"preset N" or "vanilla N": starter ship N from the mod, or the same file from the vanilla Data folder.</summary>
    static ShipFile StarterFile(string arg)
    {
        var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0] is not ("preset" or "vanilla")) throw new InvalidOperationException("needs: preset|vanilla <index>");
        var preset = GameApp.Rules.CareerMode.StarterShips[int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)].Ship;
        return parts[0] == "preset" ? preset : new ShipFile(Paths.DataFolder / "builtin_ships" / "Starter" / Path.GetFileName((string)preset.FilePath));
    }

    static int _built;
    static bool _matches;
    static bool _checkPassed;
    static int _checkVaults;

    /// <summary>
    /// Real seconds that a new design runs in the sim before the save: its crew walk to their posts and the manned parts light up in the picture.
    /// The design file keeps no crew. Revisit if large designs still show dark parts.
    /// </summary>
    const double PowerUpSeconds = 20;

    sealed record Batch(Cosmoteer.Game.GameRoot Game, string Src, string Dst, int Units, Queue<string> Files, List<string> Report);

    static void Run(string arg)
    {
        var dirs = arg.Split('|', StringSplitOptions.TrimEntries);
        if (dirs.Length != 2 || dirs.Any(string.IsNullOrEmpty)) throw new InvalidOperationException("vaultdesigns needs: <source folder>|<output folder>");
        var (src, dst) = (dirs[0], dirs[1]);
        if (!Directory.Exists(src)) throw new DirectoryNotFoundException(src);
        var game = Autotest.Game ?? throw new InvalidOperationException("vaultdesigns needs a running game");
        int units = NewGameCheck.RequiredUnits(coop: false);
        var files = new Queue<string>(Directory.EnumerateFiles(src, "*.ship.png", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        Next(new Batch(game, src, dst, units, files, [$"Tech Vaults for {units} starting tech. Source {src}, output {dst}."]));
    }

    /// <summary>Starts the next design. A changed design waits PowerUpSeconds in the sim (Autotest.WaitFor), then Finish saves it and calls Next again.</summary>
    static void Next(Batch b)
    {
        while (b.Files.TryDequeue(out var file))
        {
            string rel = Path.GetRelativePath(b.Src, file), dest = Path.Combine(b.Dst, rel);
            try
            {
                var (ship, line) = Build(b, file);
                if (ship is null) { Add(b, rel, line); continue; }
                double until = Halfling.App.Clock.Time.Seconds + PowerUpSeconds;
                Autotest.WaitFor(() => Halfling.App.Clock.Time.Seconds >= until, PowerUpSeconds + 30, $"power-up of {rel}", () =>
                {
                    Add(b, rel, Finish(b, ship, file, dest, line));
                    Next(b);
                });
                return;
            }
            catch (Exception ex) { Add(b, rel, Failed(file, dest, ex)); }
        }
        int added = b.Report.Count(l => l.Contains(": added ")), skipped = b.Report.Count(l => l.Contains(": skipped ")), failed = b.Report.Count(l => l.Contains(": UNCHANGED"));
        b.Report.Add($"Total {added + skipped + failed}: {added} changed, {skipped} skipped, {failed} unchanged.");
        File.WriteAllLines(Path.Combine(Mod.ModRoot, "vault-report.txt"), b.Report);
        Log.Info($"AUTOTEST vaultdesigns done: {b.Report[^1]}");
    }

    static void Add(Batch b, string rel, string line)
    {
        b.Report.Add($"{rel}: {line}");
        Log.Info($"vaultdesigns {rel}: {line}");
    }

    /// <summary>
    /// A design that fails is a report line, not a test failure. The output keeps the source version:
    /// with an output folder other than the source folder, the source file is copied over the output file.
    /// </summary>
    static string Failed(string file, string dest, Exception ex)
    {
        Log.Info($"vaultdesigns {file}: {ex}");
        if (Path.GetFullPath(file) != Path.GetFullPath(dest))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
        return $"UNCHANGED (place vaults by hand): {ex.Message}";
    }

    /// <summary>Loads the design into the sim near the player ship (out of stasis, so it runs) and builds the vaults. Null ship: skipped, with the reason.</summary>
    static (Ship? Ship, string Line) Build(Batch b, string file)
    {
        var sim = b.Game.Sim;
        string name = Ship.GetNameFromFilename(file) ?? throw new InvalidOperationException($"no ship name in {file}");
        byte[] bytes = Ship.LoadBytes(file);
        using (var check = Ship.Load(bytes, name, blueprintsOnly: true))
        {
            if (!GameApp.Rules.CareerMode.AllowedShipClasses.Contains(check.Rules.ID)) return (null, $"skipped: ship class {check.Rules.ID} is not a career class");
            int vaults = check.BlueprintParts.Count(p => p.Rules.ID == Vaults.VaultId);
            if (vaults > 0) return (null, $"skipped: has {vaults} vault(s), they hold {NewGameCheck.VaultCapacity(check)} tech");
        }
        // The design's techs, like a new career ship (CareerGameModeManager.FinishInitNewGame), so no part counts as locked.
        using (var check = Ship.Load(bytes, name, blueprintsOnly: true))
        {
            var techs = new HashSet<Cosmoteer.Modes.Career.TechTree.TechRules>();
            GameApp.Rules.CareerMode.GetTechsRequired(check, techs);
            var mode = (Cosmoteer.Modes.Career.CareerGameModeManager)b.Game.Mode;
            foreach (var t in techs) mode.Techs.UnlockTech(t.ID);
        }
        var near = Autotest.ActingShip(sim) ?? throw new InvalidOperationException("no player ship to build next to");
        var ship = Ship.Load(bytes, name, b.Game.LocalPlayerIndex, spawnWithCrew: true, builtByPlayer: true, randSeed: 1UL);
        ship.WorldCenter = Autotest.ClearSpotNear(sim, near.DetWorldCenter, near.WorldBoundingRadius + ship.WorldBoundingRadius + 500f, ship.WorldBoundingRadius);
        sim.Ships.Add(ship);
        try
        {
            int storageBefore = StorageParts(ship);
            var cells = StartingVault.BuildVaults(ship, b.Units);
            if (!ship.BlueprintParts.IsValid || !ship.PhysicalMatchesBlueprints) throw new InvalidOperationException("the design is not valid after the vaults");
            if (!GameApp.Rules.CareerMode.IsShipDesignLegal(ship) || NewGameCheck.VaultCapacity(ship) < b.Units) throw new InvalidOperationException("the career new-game check refuses the design");
            // Crew, full power and supplies, like a new career ship (CareerGameModeManager.FinishInitNewGame). Vaults start empty (InitToMaxResources = 0).
            ship.InitializeToMax();
            return (ship, $"added {cells.Count} vault(s) at {string.Join(" ", cells)}; {storageBefore - StorageParts(ship)} storage part(s) removed");
        }
        catch { sim.Ships.Remove(ship, dispose: true, handleLostShip: false); throw; }
    }

    /// <summary>Saves the design and removes the ship. Returns the report line.</summary>
    static string Finish(Batch b, Ship ship, string file, string dest, string line)
    {
        try
        {
            // The game draws a blueprint in place of the ship when the two differ (Ship.SaveDesignToTextureData). That picture is no use.
            if (!ship.PhysicalMatchesBlueprints || ship.OptionalConstructionManager?.HasAnyNonRepairsQueued == true)
            {
                var physical = ship.Parts.UnmatchedParts.Select(p => $"{p.Rules.ID}@{p.Location}");
                var blueprint = ship.BlueprintParts.UnmatchedParts.Select(p => $"{p.Rules.ID}@{p.Location}");
                throw new InvalidOperationException($"the ship no longer matches its blueprint after {PowerUpSeconds} s: physical [{string.Join(" ", physical)}], " +
                    $"blueprint [{string.Join(" ", blueprint)}], doors match={ship.Doors.MatchesBlueprints}, queued={ship.OptionalConstructionManager?.HasAnyNonRepairsQueued}");
            }
            // A temporary file first, so a failed save never leaves half a design in place of the old one.
            string tmp = dest + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            ship.SaveDesignAs(tmp, dialogOnError: false, out bool error);
            if (error) throw new InvalidOperationException($"the game could not save {tmp}");
            File.Move(tmp, dest, overwrite: true);
            return line;
        }
        catch (Exception ex) { return Failed(file, dest, ex); }
        finally { b.Game.Sim.Ships.Remove(ship, dispose: true, handleLostShip: false); }
    }

    static int StorageParts(Ship ship) => ship.BlueprintParts.Count(p => p.Rules.Components.OfType<FlexResourceGridRules>().Any());
}

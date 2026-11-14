using Cosmoteer;
using Cosmoteer.Gui;
using Cosmoteer.Gui.Multiplayer;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Halfling;
using Halfling.Gui.Dialogs;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Tech;

/// <summary>
/// Spec 1470 to 1476: a ship that the career new-game screens pick from the ship library and that lacks Tech Vaults.
/// A vanilla starter becomes its vault preset; any other ship gets the "Fit Tech Vaults" offer, which saves a copy with vaults.
/// The hook is the result of the ship library dialog, so the single-player and the co-op screen both keep their own selection code.
/// UI only: no game runs yet.
/// </summary>
[HarmonyPatch]
static class FitVaults
{
    /// <summary>Folder in the player's Saved Ships for the fitted copies.</summary>
    internal static string FittedFolder => (string)(Paths.SavedShipsFolder / "Hot Cargo");

    /// <summary>The career new-game screen on top: false for single player, true for co-op, null for none.</summary>
    internal static bool? CareerSetup()
    {
        foreach (var state in App.Director.States.Reverse())
        {
            if (state is NewGameConfigDialog d) return d._configGui is CareerSPModeConfigGui ? false : null;
            if (state is GameSetupScreen s) return s._curModeConfigGui is CareerMPModeConfigGui ? true : null;
        }
        return null;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ShipLibraryDialog), nameof(ShipLibraryDialog.Show))]
    static void Offer(ref Task<string?> __result)
    {
        if (!Activation.IsModEnabled() || NewGameCheck.Off || CareerSetup() is not { } coop) return;
        __result = Picked(__result, coop);
    }

    /// <summary>The picked file, its vault preset, the fitted copy, or null when the player cancels.</summary>
    internal static async Task<string?> Picked(Task<string?> pick, bool coop)
    {
        string? file = await pick;
        if (file is null) return null;
        try
        {
            if (Preset(file) is { } preset)
            {
                Log.Info($"fit vaults: {file} is a vanilla starter; the vault preset {preset} takes its place (spec 1471)");
                return preset;
            }
            int need = NewGameCheck.RequiredUnits(coop);
            string name = Ship.GetNameFromFilename(file) ?? throw new InvalidDataException($"no ship name in {file}");
            int have;
            using (var ship = Ship.Load(file, blueprintsOnly: true))
            {
                if (!GameApp.Rules.CareerMode.AllowedShipClasses.Contains(ship.Rules.ID)) return file;
                have = NewGameCheck.VaultCapacity(ship);
            }
            if (have >= need) return file;
            Shown = true;
            var answer = await TwoButtonDialog.Show(Strings.FormatText("HotCargoTech/FitAskFmt", StringTools.EscapeForXml(name), need, have),
                Strings.GetText("HotCargoTech/FitButton"), Strings.GetText("HotCargoTech/FitCancel"));
            Shown = false;
            if (answer != TwoButtonDialog.DialogResponse.Option1) return null;
            return Fit(file, need);
        }
        catch (Exception ex) when (ex is StartingVault.NoRoomException or InvalidOperationException or InvalidDataException or IOException)
        {
            Log.Info($"fit vaults: {file}: {ex.Message}");
            _ = OneButtonDialog.Show(Strings.FormatText("HotCargoTech/FitFailedFmt", StringTools.EscapeForXml(ex.Message)));
            return null;
        }
    }

    /// <summary>Spec 1471: the vault preset of a vanilla starter file (the same file name in a career StarterShips entry), or null.</summary>
    internal static string? Preset(string file)
    {
        string starters = Path.GetFullPath((string)(GameApp.Rules.BuiltInShipsFolder / "Starter"));
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(file)), starters, StringComparison.OrdinalIgnoreCase)) return null;
        return GameApp.Rules.CareerMode.StarterShips.Select(s => (string)s.Ship.FilePath)
            .FirstOrDefault(p => string.Equals(Path.GetFileName(p), Path.GetFileName(file), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Spec 1472, 1473: builds the vaults into a copy of the design and returns the copy. The source file does not change.</summary>
    internal static string Fit(string file, int units)
    {
        using var ship = FitShip(file, units, out string name);
        string dest = Path.Combine(FittedFolder, name + ".ship.png");
        ship.SaveDesignAs(dest, dialogOnError: true, out bool error);
        if (error) throw new IOException($"the game could not save {dest}");
        Log.Info($"fit vaults: {file} -> {dest}, vaults for {units} tech");
        return dest;
    }

    /// <summary>Loads the design without a game and builds vaults for the units (spec 441). The caller disposes the ship.</summary>
    internal static Ship FitShip(string file, int units, out string name)
    {
        name = Ship.GetNameFromFilename(file) ?? throw new InvalidDataException($"no ship name in {file}");
        var ship = Ship.Load(file, blueprintsOnly: true);
        try
        {
            StartingVault.BuildVaults(ship, units);
            if (!ship.BlueprintParts.IsValid) throw new InvalidOperationException("the design is not valid after the vaults");
            if (NewGameCheck.VaultCapacity(ship) < units) throw new InvalidOperationException($"the vaults hold {NewGameCheck.VaultCapacity(ship)} of {units} tech");
            return ship;
        }
        catch { ship.Dispose(); throw; }
    }

    /// <summary>True while the Fit offer shows. For the test commands.</summary>
    internal static bool Shown;
}

/// <summary>
/// Spec 1474: every career starter must be a vault preset from mod/tech/starters. Rules actions cannot loop over the StarterShips list,
/// so mod/tech/actions.rules replaces each entry by index. This check fails loudly when the game has a starter without a preset,
/// or a preset that no entry uses. It runs once, when the main menu first shows.
/// </summary>
[HarmonyPatch(typeof(TitleScreen), MethodType.Constructor)]
static class StarterCheck
{
    static bool _done;

    /// <summary>The problems of the last check, for the test commands. Empty when every starter has its preset.</summary>
    internal static List<string> Problems = [];

    internal static List<string> Find()
    {
        string folder = Path.GetFullPath(Path.Combine(Mod.ModRoot, "tech", "starters"));
        var presets = Directory.EnumerateFiles(folder, "*.ship.png").Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var starters = GameApp.Rules.CareerMode.StarterShips.Select(s => Path.GetFullPath((string)s.Ship.FilePath)).ToList();
        var problems = starters.Where(p => !string.Equals(Path.GetDirectoryName(p), folder, StringComparison.OrdinalIgnoreCase))
            .Select(p => $"career starter {p} has no vault preset; add it to mod/tech/starters and mod/tech/actions.rules").ToList();
        problems.AddRange(presets.Except(starters.Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase)
            .Select(f => $"vault preset {f} replaces no career starter"));
        return problems;
    }

    static void Postfix() => Errors.Guard("starter check", () =>
    {
        if (_done || !Activation.IsModEnabled()) return;
        _done = true;
        Problems = Find();
        if (Problems.Count == 0) return;
        Log.Error($"starter check: {string.Join("; ", Problems)}");
        _ = OneButtonDialog.Show(Strings.FormatText("HotCargoTech/StarterCheckFmt", StringTools.EscapeForXml(string.Join("\n", Problems))));
    });
}

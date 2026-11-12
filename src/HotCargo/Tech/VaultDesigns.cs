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
    }

    static void Run(string arg)
    {
        var dirs = arg.Split('|', StringSplitOptions.TrimEntries);
        if (dirs.Length != 2 || dirs.Any(string.IsNullOrEmpty)) throw new InvalidOperationException("vaultdesigns needs: <source folder>|<output folder>");
        var (src, dst) = (dirs[0], dirs[1]);
        if (!Directory.Exists(src)) throw new DirectoryNotFoundException(src);
        var game = Autotest.Game ?? throw new InvalidOperationException("vaultdesigns needs a running game");
        int units = NewGameCheck.RequiredUnits(coop: false);
        var report = new List<string> { $"Tech Vaults for {units} starting tech. Source {src}, output {dst}." };
        var files = Directory.EnumerateFiles(src, "*.ship.png", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        foreach (var file in files)
        {
            string rel = Path.GetRelativePath(src, file);
            string line;
            try { line = Design(game, file, Path.Combine(dst, rel), units); }
            // A design that fails is a report line, not a test failure: the source file stays as it was.
            catch (Exception ex) { line = $"FAILED: {ex.Message}"; Log.Info($"vaultdesigns {rel}: {ex}"); }
            report.Add($"{rel}: {line}");
            Log.Info($"vaultdesigns {rel}: {line}");
        }
        int added = report.Count(l => l.Contains(": added ")), skipped = report.Count(l => l.Contains(": skipped ")), failed = report.Count(l => l.Contains(": FAILED"));
        report.Add($"Total {files.Count}: {added} changed, {skipped} skipped, {failed} failed.");
        File.WriteAllLines(Path.Combine(Mod.ModRoot, "vault-report.txt"), report);
        Log.Info($"AUTOTEST vaultdesigns done: {report[^1]}");
    }

    /// <summary>Adds vaults to one design and saves it. Returns the report line. Throws when the result is not a valid career starter.</summary>
    static string Design(Cosmoteer.Game.GameRoot game, string file, string dest, int units)
    {
        var sim = game.Sim;
        string name = Ship.GetNameFromFilename(file) ?? throw new InvalidOperationException($"no ship name in {file}");
        byte[] bytes = Ship.LoadBytes(file);
        using (var check = Ship.Load(bytes, name, blueprintsOnly: true))
        {
            if (!GameApp.Rules.CareerMode.AllowedShipClasses.Contains(check.Rules.ID)) return $"skipped: ship class {check.Rules.ID} is not a career class";
            int vaults = check.BlueprintParts.Count(p => p.Rules.ID == Vaults.VaultId);
            if (vaults > 0) return $"skipped: has {vaults} vault(s), they hold {NewGameCheck.VaultCapacity(check)} tech";
        }
        var ship = Ship.Load(bytes, name, game.LocalPlayerIndex, spawnWithCrew: false, builtByPlayer: true, randSeed: 1UL);
        // Far from everything, so that the build steps never touch another ship.
        ship.WorldCenter = Autotest.ClearSpotNear(sim, new Halfling.Geometry.Vector2(50000f, 50000f), 0f, ship.WorldBoundingRadius);
        sim.Ships.Add(ship);
        try
        {
            int storageBefore = StorageParts(ship);
            var (cells, outside) = StartingVault.BuildVaults(ship, units);
            var finder = new BlueprintPathfinder(ship);
            int noPath = ship.BlueprintParts.Count(p => p.Rules.ID == Vaults.VaultId && !StartingVault.HasCrewPath(finder, new PartInfo(p.Rules, p.Location, p.Rotation, p.FlipX)));
            if (!ship.BlueprintParts.IsValid || !ship.PhysicalMatchesBlueprints) throw new InvalidOperationException("the design is not valid after the vaults");
            if (!GameApp.Rules.CareerMode.IsShipDesignLegal(ship) || NewGameCheck.VaultCapacity(ship) < units) throw new InvalidOperationException("the career new-game check refuses the design");
            // A temporary file first, so a failed save never leaves half a design in place of the old one.
            string tmp = dest + ".tmp";
            ship.SaveDesignAs(tmp, dialogOnError: false, out bool error);
            if (error) throw new InvalidOperationException($"the game could not save {tmp}");
            File.Move(tmp, dest, overwrite: true);
            return $"added {cells.Count} vault(s) at {string.Join(" ", cells)}; {storageBefore - StorageParts(ship)} storage part(s) removed; " +
                   $"{outside} outside the hull; {noPath} without crew path";
        }
        finally { sim.Ships.Remove(ship, dispose: true, handleLostShip: false); }
    }

    static int StorageParts(Ship ship) => ship.BlueprintParts.Count(p => p.Rules.Components.OfType<FlexResourceGridRules>().Any());
}

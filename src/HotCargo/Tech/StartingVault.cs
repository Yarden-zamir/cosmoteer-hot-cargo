using Cosmoteer.Data;
using Cosmoteer.Localization;
using Cosmoteer.Modes;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HotCargo.Core;

namespace HotCargo.Tech;

/// <summary>
/// Spec 17, 441: a new run starts with StartingTechUnits in 1-tile Tech Vaults on the starting ship.
/// In co-op each player commands only their own ships, so the first ship of each player gets vaults for a share of the units.
/// The starter ships have no vault, so this adds vaults to the ship's blueprints and builds them at once, with the build tool's own steps.
/// </summary>
internal static class StartingVault
{
    internal static void OnNewRun(Run run)
    {
        var sim = run.Game.Sim;
        int units = run.T.Tech.StartingTechUnits;
        var ships = sim.Ships.HumanPlayerShips.GroupBy(s => s.Metadata.PlayerIndex).OrderBy(g => g.Key)
            .Select(g => g.OrderBy(s => s.UniqueID).First()).ToList();
        if (ships.Count == 0) throw new InvalidOperationException("new run has no human player ship");
        bool unreachable = false;
        for (int i = 0; i < ships.Count; i++)
            unreachable |= Stock(ships[i], units / ships.Count + (i < units % ships.Count ? 1 : 0));
        // A co-op launch runs this on a worker thread, so the notice waits for the next frame.
        if (unreachable) Run.OnNextFrame(() => Ux.UxKit.CenterText(run.Game, Strings.GetText("HotCargoTech/VaultOutside")));
    }

    /// <summary>Tech units that one vault holds, from its rules.</summary>
    internal static int Capacity(PartRules vault) =>
        vault.Components.OfType<TypedResourceGridRules>().Where(g => g.ResourceType == Vaults.TechId).Sum(g => g.MaxResources) is > 0 and var n
            ? n : throw new InvalidOperationException($"{vault.ID} has no grid for {Vaults.TechId}");

    /// <summary>Places enough vaults on the ship for the units, and fills them. Returns true when crew cannot walk to a new vault.</summary>
    static bool Stock(Ship ship, int units)
    {
        PartRules vault = ship.Rules.PartsByID[Vaults.VaultId];
        int perVault = Capacity(vault);
        int count = (units + perVault - 1) / perVault;
        var placed = new List<IntVector2>();
        while (placed.Count < count)
        {
            if (ReplaceStorage(ship, vault, count - placed.Count) is { } inside) placed.AddRange(inside);
            else placed.Add(PlaceOutside(ship, vault, requireCrewPath: true) ?? PlaceOutside(ship, vault, requireCrewPath: false)!.Value);
        }
        int added = Vaults.Add(Vaults.OnShip(ship), units);
        if (added != units) throw new InvalidOperationException($"starting tech: added {added} of {units} on {ship.Name}");
        var finder = new BlueprintPathfinder(ship);
        var unreachable = placed.Where(c => !HasCrewPath(finder, new PartInfo(vault, c, 0, flipX: false))).ToList();
        Log.Info($"starting tech: {added} units in {placed.Count} vault(s) on {ship.Name} (player {ship.Metadata.PlayerIndex}) at {string.Join(" ", placed)}; no crew path: {unreachable.Count}");
        return unreachable.Count > 0;
    }

    /// <summary>True when crew can walk from crew quarters to the part, through doors, like the build tool's path check.</summary>
    internal static bool HasCrewPath(BlueprintPathfinder finder, PartInfo part) => float.IsFinite(finder.GetPathToNearestCrewSource(part, new List<IntVector2>()));

    /// <summary>
    /// Swaps the smallest vanilla storage part (nearest to the ship center first) for vaults on its tiles.
    /// The storage part goes like a part that the player grabs: corridor tiles take its place and keep its crew paths.
    /// Then a vault replaces one corridor tile at a time, and the game adds its doors like the build tool.
    /// Tiles with a door of the storage part come last: they lead into the room, so as corridors they keep a walkway through it.
    /// Among the others, tiles nearest to the ship center come first.
    /// Returns the vault cells, or null when the ship has no storage part.
    /// </summary>
    static List<IntVector2>? ReplaceStorage(Ship ship, PartRules vault, int count)
    {
        var center = Center2(ship);
        var bay = ship.BlueprintParts
            .Where(p => p.Rules.ID != Vaults.VaultId && p.Rules.Components.OfType<FlexResourceGridRules>().Any())
            .OrderBy(p => p.Rect.Area).ThenBy(p => Distance2(p.Rect, center)).ThenBy(p => p.Location.Y).ThenBy(p => p.Location.X)
            .FirstOrDefault();
        if (bay is null) return null;
        IntRect rect = bay.Rect;
        var doorTiles = bay.GetBlueprintDoors().Select(d => rect.Contains(d.Location.Cell) ? d.Location.Cell : d.Location.OppositeCell).ToHashSet();
        using (var sui = ShipUpdateInfo.Alloc(ship))
        {
            if (!sui.TryRemoveBlueprintPart(bay, mirrorPhysical: true, out var reasons, grabbed: true))
                throw new InvalidOperationException($"cannot remove {bay.Rules.ID} at {bay.Location} for the starting vaults: {reasons}");
            sui.Apply(ConstructionMode.InstantFree, builtByPlayer: true);
        }
        var cells = rect.OrderBy(doorTiles.Contains).ThenBy(c => Distance2(new IntRect(c.X, c.Y, 1, 1), center)).ThenBy(c => c.Y).ThenBy(c => c.X).Take(count).ToList();
        foreach (var cell in cells)
        {
            using var sui = ShipUpdateInfo.Alloc(ship);
            if (!sui.TryAddBlueprintPart(vault, cell, 0, flipX: false, mirrorPhysical: true, allowOverwrite: true, addDoors: true, hireCrew: false, out var reasons))
                throw new InvalidOperationException($"cannot place a starting vault at {cell} in place of {bay.Rules.ID}: {reasons}");
            sui.Apply(ConstructionMode.InstantFree, builtByPlayer: true);
        }
        Log.Info($"starting vaults replace {bay.Rules.ID} at {rect.Location}: {cells.Count} vault(s), {rect.Area - cells.Count} corridor tile(s)");
        return cells;
    }

    /// <summary>
    /// Puts a part on a free spot next to the hull: the most sheltered spot first (most hull tiles next to it), then the one nearest to the ship center.
    /// The game adds its doors like the build tool. With requireCrewPath, a spot counts only when crew can walk to the part.
    /// The mode says how the part gets built: at once (default), or like a player's blueprint (the game's construction mode).
    /// With apply, the caller applies the change instead (for example as the build tool's input).
    /// Returns the part location, or null when requireCrewPath finds no spot. Throws when no spot is free at all.
    /// </summary>
    internal static IntVector2? PlaceOutside(Ship ship, PartRules rules, bool requireCrewPath, ConstructionMode mode = ConstructionMode.InstantFree,
        Action<ShipUpdateInfo>? apply = null)
    {
        var parts = ship.BlueprintParts;
        var cells = parts.SelectMany(p => p.Rect).ToHashSet();
        var center = Center2(ship);
        IntVector2 size = rules.Size;
        int minX = cells.Min(c => c.X), maxX = cells.Max(c => c.X), minY = cells.Min(c => c.Y), maxY = cells.Max(c => c.Y);
        var spots = new List<IntRect>();
        for (int y = minY - size.Y; y <= maxY + 1; y++)
            for (int x = minX - size.X; x <= maxX + 1; x++)
            {
                var rect = new IntRect(x, y, size.X, size.Y);
                if (!parts.HasPartAt(rect) && rect.GetAdjacentCells(includeCorners: false).Any(cells.Contains)) spots.Add(rect);
            }
        var ordered = spots.OrderByDescending(r => r.GetAdjacentCells(includeCorners: false).Count(cells.Contains))
            .ThenBy(r => Distance2(r, center)).ThenBy(r => r.Y).ThenBy(r => r.X);
        foreach (var rect in ordered)
        {
            using var sui = ShipUpdateInfo.Alloc(ship);
            if (!sui.TryAddBlueprintPart(rules, rect.Location, 0, flipX: false, mirrorPhysical: true, allowOverwrite: false, addDoors: true, hireCrew: false, out _)) continue;
            if (requireCrewPath && !HasCrewPath(new BlueprintPathfinder(ship, sui), new PartInfo(rules, rect.Location, 0, flipX: false))) continue;
            if (apply is null) sui.Apply(mode, builtByPlayer: true);
            else apply(sui);
            Log.Info($"{rules.ID} placed outside the hull of {ship.Name} at {rect.Location}, crew path required={requireCrewPath}");
            return rect.Location;
        }
        if (requireCrewPath) return null;
        throw new InvalidOperationException($"no free spot for {rules.ID} on {ship.Name}");
    }

    /// <summary>Center of the blueprint bounding box, in doubled coordinates so that the distance math stays in integers.</summary>
    static (int X, int Y) Center2(Ship ship)
    {
        var cells = ship.BlueprintParts.SelectMany(p => p.Rect).ToList();
        return (cells.Min(c => c.X) + cells.Max(c => c.X) + 1, cells.Min(c => c.Y) + cells.Max(c => c.Y) + 1);
    }

    static int Distance2(IntRect r, (int X, int Y) center2)
    {
        int dx = 2 * r.Left + r.Width - center2.X, dy = 2 * r.Top + r.Height - center2.Y;
        return dx * dx + dy * dy;
    }
}

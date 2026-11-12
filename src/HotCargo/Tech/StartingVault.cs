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
/// Spec 17, 1014: a new run fills the Tech Vaults of the starting ships with StartingTechUnits.
/// In co-op each player commands only their own ships, so the first ship of each player gets a share of the units.
/// The ships have vaults by design (spec 1010), so this only fills them.
/// The placement helpers below build vaults into designs (VaultDesigns, spec 1016) and place parts for other areas.
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
        int missing = 0;
        bool unreachable = false;
        for (int i = 0; i < ships.Count; i++)
        {
            var ship = ships[i];
            int share = units / ships.Count + (i < units % ships.Count ? 1 : 0);
            int added = Vaults.Add(Vaults.OnShip(ship), share);
            missing += share - added;
            var finder = new BlueprintPathfinder(ship);
            int cut = ship.BlueprintParts.Count(p => p.Rules.ID == Vaults.VaultId && !HasCrewPath(finder, new PartInfo(p.Rules, p.Location, p.Rotation, p.FlipX)));
            unreachable |= cut > 0;
            Log.Info($"starting tech: {added} of {share} units in the vaults of {ship.Name} (player {ship.Metadata.PlayerIndex}); vaults without crew path: {cut}");
        }
        // The new-game check (spec 1010) makes room for all units. Only a career save from before the mod has ships without vaults.
        string? notice = missing > 0 ? Strings.FormatText("HotCargoTech/NoVaultRoomFmt", missing)
            : unreachable ? Strings.GetText("HotCargoTech/VaultOutside") : null;
        // A co-op launch runs this on a worker thread, so the notice waits for the next frame.
        if (notice is not null) Run.OnNextFrame(() => Ux.UxKit.CenterText(run.Game, notice));
    }

    /// <summary>Tech units that one vault holds, from its rules.</summary>
    internal static int Capacity(PartRules vault) =>
        vault.Components.OfType<TypedResourceGridRules>().Where(g => g.ResourceType == Vaults.TechId).Sum(g => g.MaxResources) is > 0 and var n
            ? n : throw new InvalidOperationException($"{vault.ID} has no grid for {Vaults.TechId}");

    /// <summary>
    /// Builds enough empty vaults into the ship for the units (spec 441), only inside the hull and only where crew can walk to them.
    /// The tiles of the smallest storage bay come first; when none of them gets a vault, the bay goes back.
    /// Then come corridor tiles and enclosed empty tiles, nearest to the ship center first. allowCell (tests) can rule out cells.
    /// Each vault must keep a crew path to every part that had one. Returns the vault cells.
    /// Throws NoRoomException when the hull has too few such tiles; the caller then keeps the old design.
    /// </summary>
    internal static List<IntVector2> BuildVaults(Ship ship, int units, Func<IntVector2, bool>? allowCell = null)
    {
        PartRules vault = ship.Rules.PartsByID[Vaults.VaultId];
        int perVault = Capacity(vault);
        int count = (units + perVault - 1) / perVault;
        // The game's grab rule names the corridor part: the part that a grabbed part leaves on each tile.
        // Rules may name it by an alias (base_part.rules: "corridor"), so the ID resolves through PartsByID like the grab does (ShipUpdateInfo.TryRemoveBlueprintParts).
        var corridors = ship.Rules.Parts.Select(p => p.CreatePartPerTileWhenGrabbed).Where(id => id != default).Distinct()
            .Select(id => ship.Rules.PartsByID.TryGetValue(id, out var r) ? r.ID : throw new InvalidOperationException($"no part {id} (CreatePartPerTileWhenGrabbed)")).ToHashSet();
        if (corridors.Count == 0) throw new InvalidOperationException("no part rules name a corridor part (CreatePartPerTileWhenGrabbed)");
        var placed = new List<IntVector2>();
        if (GrabStorage(ship) is { } bay)
        {
            Place(ship, vault, bay.Cells, corridors, count, placed, allowCell);
            // No vault on the bay's tiles: the bay goes back, so the design keeps its storage.
            if (placed.Count == 0) PutBack(ship, bay.Part, bay.HadCrewPath);
        }
        var center = Center2(ship);
        var interior = InteriorCells(ship, corridors).OrderBy(c => Distance2(new IntRect(c.X, c.Y, 1, 1), center)).ThenBy(c => c.Y).ThenBy(c => c.X).Distinct().ToList();
        Place(ship, vault, interior, corridors, count, placed, allowCell);
        if (placed.Count < count) throw new NoRoomException($"room inside the hull for {placed.Count} of {count} vaults with a crew path");
        return placed;
    }

    /// <summary>Puts vaults on the candidate cells (corridor or empty only) until placed holds count.</summary>
    static void Place(Ship ship, PartRules vault, List<IntVector2> candidates, HashSet<ID<PartRules>> corridors, int count, List<IntVector2> placed, Func<IntVector2, bool>? allowCell)
    {
        // Parts that crew can reach now must stay reachable. Built here, after any grab or put-back, so it matches the ship.
        var finder = new BlueprintPathfinder(ship);
        var reachable = ship.BlueprintParts.Where(p => NeedsCrewAccess(p.Rules)).Select(Info).Where(p => HasCrewPath(finder, p)).ToList();
        foreach (var cell in candidates)
        {
            if (placed.Count == count) return;
            if (allowCell?.Invoke(cell) == false || ship.BlueprintParts.HasPartAt(cell, condition: p => !corridors.Contains(p.Rules.ID))) continue;
            if (TryPlace(ship, vault, cell, reachable) is { } why) { Log.Info($"vaults: no vault at {cell}: {why}"); continue; }
            placed.Add(cell);
            reachable.RemoveAll(p => p.Location == cell);
            reachable.Add(new PartInfo(vault, cell, 0, flipX: false));
        }
    }

    /// <summary>Puts a grabbed storage part back on its corridor tiles. The game adds its doors like the build tool. A part that crew could reach before must stay reachable.</summary>
    static void PutBack(Ship ship, PartInfo part, bool hadCrewPath)
    {
        using var sui = ShipUpdateInfo.Alloc(ship);
        if (!sui.TryAddBlueprintPart(part.Rules, part.Location, part.Rotation, part.FlipX, mirrorPhysical: true, allowOverwrite: true, addDoors: true, hireCrew: false, out var reasons))
            throw new InvalidOperationException($"cannot put {part.Rules.ID} back at {part.Location}: {reasons}");
        sui.Apply(ConstructionMode.InstantFree, builtByPlayer: true);
        if (hadCrewPath && !HasCrewPath(new BlueprintPathfinder(ship), part)) throw new InvalidOperationException($"{part.Rules.ID} at {part.Location} has no crew path after the put-back");
        Log.Info($"vaults: no vault on the tiles of {part.Rules.ID} at {part.Location}; it went back");
    }

    internal sealed class NoRoomException(string message) : Exception(message);

    /// <summary>Parts that the build tool warns about without a crew path (crew quarters excluded: they are the path sources).</summary>
    internal static bool NeedsCrewAccess(PartRules r) =>
        r.HasDoorLocations && !r.NoAutoDoors && r.DoorAccessWarningLevel != AccessWarningLevel.None && r.CrewCapacity == 0;

    static PartInfo Info(BlueprintPart p) => new(p.Rules, p.Location, p.Rotation, p.FlipX);

    /// <summary>
    /// Puts a vault on the cell when crew can walk to it and every part in reachable keeps its crew path. The game adds doors like the build tool.
    /// Returns null on success, else the reason.
    /// </summary>
    static string? TryPlace(Ship ship, PartRules vault, IntVector2 cell, List<PartInfo> reachable)
    {
        using var sui = ShipUpdateInfo.Alloc(ship);
        if (!sui.TryAddBlueprintPart(vault, cell, 0, flipX: false, mirrorPhysical: true, allowOverwrite: true, addDoors: true, hireCrew: false, out var reasons)) return $"build tool refuses: {reasons}";
        var finder = new BlueprintPathfinder(ship, sui);
        if (!HasCrewPath(finder, new PartInfo(vault, cell, 0, flipX: false))) return "no crew path to the vault";
        if (reachable.FirstOrDefault(p => p.Location != cell && !HasCrewPath(finder, p)) is { Rules: not null } cut) return $"{cut.Rules.ID} at {cut.Location} loses its crew path";
        sui.Apply(ConstructionMode.InstantFree, builtByPlayer: true);
        return null;
    }

    /// <summary>Corridor tiles, and empty tiles with a part on all four sides.</summary>
    static IEnumerable<IntVector2> InteriorCells(Ship ship, HashSet<ID<PartRules>> corridors)
    {
        var parts = ship.BlueprintParts;
        var cells = parts.SelectMany(p => p.Rect).ToHashSet();
        foreach (var p in parts.Where(p => corridors.Contains(p.Rules.ID))) foreach (var c in p.Rect) yield return c;
        int minX = cells.Min(c => c.X), maxX = cells.Max(c => c.X), minY = cells.Min(c => c.Y), maxY = cells.Max(c => c.Y);
        for (int y = minY + 1; y < maxY; y++)
            for (int x = minX + 1; x < maxX; x++)
            {
                var c = new IntVector2(x, y);
                if (!cells.Contains(c) && new IntRect(x, y, 1, 1).GetAdjacentCells(includeCorners: false).All(cells.Contains)) yield return c;
            }
    }

    /// <summary>True when crew can walk from crew quarters to the part, through doors, like the build tool's path check.</summary>
    internal static bool HasCrewPath(BlueprintPathfinder finder, PartInfo part) => float.IsFinite(finder.GetPathToNearestCrewSource(part, new List<IntVector2>()));

    /// <summary>
    /// Grabs the smallest vanilla storage part (nearest to the ship center first): corridor tiles take its place and keep its crew paths.
    /// Returns the storage part and its tiles in vault order: tiles with a door of the storage part last (as corridors they keep a walkway through the room),
    /// the others nearest to the ship center first. Null when the ship has no storage part.
    /// </summary>
    static (PartInfo Part, bool HadCrewPath, List<IntVector2> Cells)? GrabStorage(Ship ship)
    {
        var center = Center2(ship);
        var bay = ship.BlueprintParts
            .Where(p => p.Rules.ID != Vaults.VaultId && p.Rules.Components.OfType<FlexResourceGridRules>().Any())
            .OrderBy(p => p.Rect.Area).ThenBy(p => Distance2(p.Rect, center)).ThenBy(p => p.Location.Y).ThenBy(p => p.Location.X)
            .FirstOrDefault();
        if (bay is null) return null;
        IntRect rect = bay.Rect;
        var part = Info(bay);
        bool hadCrewPath = HasCrewPath(new BlueprintPathfinder(ship), part);
        var doorTiles = bay.GetBlueprintDoors().Select(d => rect.Contains(d.Location.Cell) ? d.Location.Cell : d.Location.OppositeCell).ToHashSet();
        using (var sui = ShipUpdateInfo.Alloc(ship))
        {
            if (!sui.TryRemoveBlueprintPart(bay, mirrorPhysical: true, out var reasons, grabbed: true))
                throw new InvalidOperationException($"cannot remove {bay.Rules.ID} at {bay.Location} for the vaults: {reasons}");
            sui.Apply(ConstructionMode.InstantFree, builtByPlayer: true);
        }
        Log.Info($"vaults: {bay.Rules.ID} at {rect.Location} became corridor");
        return (part, hadCrewPath, rect.OrderBy(doorTiles.Contains).ThenBy(c => Distance2(new IntRect(c.X, c.Y, 1, 1), center)).ThenBy(c => c.Y).ThenBy(c => c.X).ToList());
    }

    /// <summary>
    /// Puts a part on a free spot next to the hull: the most sheltered spot first (most hull tiles next to it), then the one nearest to the ship center.
    /// The game adds its doors like the build tool. With requireCrewPath, a spot counts only when crew can walk to the part.
    /// The mode says how the part gets built: at once (default), or like a player's blueprint (the game's construction mode).
    /// Returns the part location, or null when requireCrewPath finds no spot. Throws when no spot is free at all.
    /// </summary>
    internal static IntVector2? PlaceOutside(Ship ship, PartRules rules, bool requireCrewPath, ConstructionMode mode = ConstructionMode.InstantFree)
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
            sui.Apply(mode, builtByPlayer: true);
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

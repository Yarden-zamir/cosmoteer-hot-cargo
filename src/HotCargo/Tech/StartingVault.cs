using Cosmoteer.Data;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Doors;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using HotCargo.Core;

namespace HotCargo.Tech;

/// <summary>
/// Spec 17: a new run starts with StartingTechUnits in Tech Vaults on the starting ship.
/// In co-op each player commands only their own ships, so the first ship of each player gets a vault and a share of the units.
/// The starter ships have no vault, so this adds vaults to the ship's blueprints and builds them at once.
/// </summary>
internal static class StartingVault
{
    static readonly ID<PartRules> StorageBayId = new("cosmoteer.storage_2x2");

    internal static void OnNewRun(Run run)
    {
        var sim = run.Game.Sim;
        int units = run.T.Tech.StartingTechUnits;
        var ships = sim.Ships.HumanPlayerShips.GroupBy(s => s.Metadata.PlayerIndex).OrderBy(g => g.Key)
            .Select(g => g.OrderBy(s => s.UniqueID).First()).ToList();
        if (ships.Count == 0) throw new InvalidOperationException("new run has no human player ship");
        bool needsDoor = false;
        for (int i = 0; i < ships.Count; i++)
            needsDoor |= Stock(ships[i], units / ships.Count + (i < units % ships.Count ? 1 : 0));
        // A co-op launch runs this on a worker thread, so the notice waits for the next frame.
        if (needsDoor) Run.OnNextFrame(() => Ux.UxKit.CenterText(run.Game, Strings.GetText("HotCargoTech/VaultOutside")));
    }

    /// <summary>Places vaults on the ship until they hold the units, and fills them. Returns true when a vault has no door.</summary>
    static bool Stock(Ship ship, int units)
    {
        PartRules vault = ship.Rules.PartsByID[Vaults.VaultId];
        bool needsDoor = false;
        // Each placement adds one vault. The loop handles a share above one vault's capacity.
        while (Vaults.OnShip(ship).Sum(g => g.MaxResources) < units)
            needsDoor |= !(ReplaceStorageBay(ship, vault) || PlaceOutside(ship, vault, requireDoor: true) || PlaceOutside(ship, vault, requireDoor: false));
        int added = Vaults.Add(Vaults.OnShip(ship), units);
        if (added != units) throw new InvalidOperationException($"starting tech: added {added} of {units} on {ship.Name}");
        Log.Info($"starting tech: {added} units in {Vaults.OnShip(ship).Count()} vault(s) on {ship.Name} (player {ship.Metadata.PlayerIndex})");
        return needsDoor;
    }

    /// <summary>
    /// Swaps a vanilla 2x2 storage bay for a vault in the same place. The vault keeps the bay's doors and its spot inside the hull.
    /// The bay's resources go where the instant rebuild puts them.
    /// </summary>
    static bool ReplaceStorageBay(Ship ship, PartRules vault)
    {
        var bay = ship.BlueprintParts
            .Where(p => p.Rules.ID == StorageBayId)
            .OrderBy(p => p.Location.Y).ThenBy(p => p.Location.X)
            .FirstOrDefault();
        if (bay is null) return false;
        ship.BlueprintParts.RemoveBlueprintPart(bay, removeDoors: false);
        ship.BlueprintParts.CreateBlueprintPart(vault, bay.Location, 0, false);
        ship.MatchPhysicalToBlueprints(adjustCrew: false, builtByPlayer: true);
        Log.Info($"starting vault replaces the storage bay at {bay.Location}");
        return true;
    }

    /// <summary>
    /// Puts a vault on a free 2x2 spot next to the hull, nearest to the ship center first.
    /// With requireDoor, the spot must allow a door to a neighbor part, and the door is added.
    /// Limit: a door to a neighbor does not prove that crew can walk there from the rest of the ship. Revisit if players report an unreachable vault.
    /// </summary>
    internal static bool PlaceOutside(Ship ship, PartRules vault, bool requireDoor)
    {
        var parts = ship.BlueprintParts;
        var cells = parts.SelectMany(p => p.Rect).ToHashSet();
        int minX = cells.Min(c => c.X), maxX = cells.Max(c => c.X), minY = cells.Min(c => c.Y), maxY = cells.Max(c => c.Y);
        // Doubled coordinates keep the distance math in integers.
        int Distance2(IntRect r) { int dx = 2 * r.Left + 2 - (minX + maxX + 1), dy = 2 * r.Top + 2 - (minY + maxY + 1); return dx * dx + dy * dy; }
        var spots = new List<IntRect>();
        for (int y = minY - 2; y <= maxY + 1; y++)
            for (int x = minX - 2; x <= maxX + 1; x++)
            {
                var rect = new IntRect(x, y, 2, 2);
                if (!parts.HasPartAt(rect) && rect.GetAdjacentCells(includeCorners: false).Any(cells.Contains)) spots.Add(rect);
            }
        foreach (var rect in spots.OrderBy(Distance2).ThenBy(r => r.Y).ThenBy(r => r.X))
        {
            int invalidBefore = parts.InvalidParts.Count;
            var bp = parts.CreateBlueprintPart(vault, rect.Location, 0, false);
            parts.UpdateValidity();
            DoorLocation? door = rect.GetAdjacentCells(includeCorners: false).Where(cells.Contains)
                .Select(c => DoorLocation.BetweenRectAndCell(rect, c))
                .Cast<DoorLocation?>()
                .FirstOrDefault(d => ship.BlueprintDoors.CanAddDoorAt(d!.Value));
            if (!bp.IsValid || parts.InvalidParts.Count != invalidBefore || (requireDoor && door is null))
            {
                parts.RemoveBlueprintPart(bp, removeDoors: true);
                parts.UpdateValidity();
                continue;
            }
            if (door is { } d) ship.BlueprintDoors.CreateBlueprintDoor(ship.Rules.DefaultDoor ?? throw new InvalidOperationException("ship rules have no default door"), d);
            ship.MatchPhysicalToBlueprints(adjustCrew: false, builtByPlayer: true);
            Log.Info($"starting vault placed outside at {rect.Location}, door={door?.ToString() ?? "none"}");
            return true;
        }
        if (requireDoor) return false;
        throw new InvalidOperationException("no free spot for the starting Tech Vault");
    }
}

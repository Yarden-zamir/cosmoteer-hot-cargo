using Cosmoteer.Crew;
using Cosmoteer.Data;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Simulation;

namespace HotCargo.Tech;

/// <summary>Stolen Tech in the simulation: fleet count and the Tech Vault grids. Call only from sim logic or single-player debug code.</summary>
public static class Vaults
{
    public static readonly ID<ResourceRules> TechId = new("yarden_zamir.stolen_tech");
    public static readonly ID<PartRules> VaultId = new("yarden_zamir.tech_vault");

    /// <summary>Human player ships, including ships in a local jump (they are not in Sim.Ships while they jump).</summary>
    public static IEnumerable<Ship> HumanShips(SimRoot sim) => sim.Ships.AllShips.Where(sim.Mode.IsHumanPlayerShip);

    /// <summary>
    /// Spec 65, 449: ships whose vaults hold tech for the players. Human ships, and derelict (junk) ships: a human ship without control
    /// or a piece that broke off one. Only player ships have vaults, so every vault on a derelict holds the players' tech.
    /// </summary>
    public static bool HoldsForPlayers(SimRoot sim, Ship ship) => ship.IsJunk || sim.Mode.IsHumanPlayerShip(ship);

    /// <summary>Tech in the vaults of one ship: the vault storage, the same units that the vault art shows.</summary>
    public static int ShipTech(Ship ship) => OnShip(ship).Sum(g => g.Resources);

    /// <summary>
    /// The fleet tech count (spec section 4, requirement 59): tech in the vaults of human and derelict ships, plus tech that crew carry
    /// for the players. Tech on ships left in another system is not in this sim, so it is not counted.
    /// </summary>
    public static int CountFleetTech(SimRoot sim)
    {
        int stored = sim.Ships.AllShips.Where(s => HoldsForPlayers(sim, s)).Sum(ShipTech);
        int carried = sim.Crew._allSouls.Where(c => CarriesForPlayers(sim, c)).Sum(c => c.CarriedResource.QuantityOfType(TechId));
        return stored + carried;
    }

    /// <summary>Spec 449: tech in crates (nuggets) in space. Not in the fleet count: crew must bring it back.</summary>
    public static int CrateUnits(SimRoot sim)
    {
        int units = 0;
        for (int i = 0; i < sim.Nuggets.Count; i++)
            if (sim.Nuggets[i].ResourceType == TechId) units += sim.Nuggets[i].Quantity;
        return units;
    }

    /// <summary>Spec 449: the vault tech of one player's ships, and the vault tech on derelict ships. For the heat panel.</summary>
    public static (int Player, int Derelict) Split(SimRoot sim, int playerIndex)
    {
        int player = 0, derelict = 0;
        foreach (var ship in sim.Ships.AllShips)
        {
            if (ship.IsJunk) derelict += ShipTech(ship);
            else if (ship.Metadata.PlayerIndex == playerIndex && sim.Mode.IsHumanPlayerShip(ship)) player += ShipTech(ship);
        }
        return (player, derelict);
    }

    /// <summary>
    /// Human crew, wherever they are (any ship or space). Also station crew that carry a sale from a human ship:
    /// the station pays on delivery, so until then the tech still belongs to the players.
    /// </summary>
    static bool CarriesForPlayers(SimRoot sim, CrewSoul crew) =>
        sim.Mode.IsHumanPlayerAllegiance(crew.PlayerIndex)
        || crew.Job is ResourceTransferJob { TradeType: TradeType.Sell, Source.Ship: { } from } && sim.Mode.IsHumanPlayerShip(from);

    public static IEnumerable<Part> VaultParts(Ship ship) => ship.Parts.Where(p => p.Rules.ID == VaultId);

    /// <summary>Tech Vault grids on human ships, in a stable order (player, then part location).</summary>
    public static List<TypedResourceGrid> All(SimRoot sim) =>
        HumanShips(sim)
            .OrderBy(s => s.Metadata.PlayerIndex).ThenBy(s => s.UniqueID)
            .SelectMany(OnShip)
            .ToList();

    /// <summary>Tech Vault grids on one ship, in part location order.</summary>
    public static IEnumerable<TypedResourceGrid> OnShip(Ship ship) =>
        TypedResourceGrid.GetAllOnShip(ship).Where(g => g.ResourceType == TechId && g.Part is not null)
            .OrderBy(g => g.Part!.Location.Y).ThenBy(g => g.Part!.Location.X);

    /// <summary>Adds tech to vaults in order. Returns the units added; it is less than requested when the vaults are full.</summary>
    public static int Add(SimRoot sim, int units) => Add(All(sim), units);

    /// <summary>Adds tech to the given vault grids in order. Returns the units added.</summary>
    public static int Add(IEnumerable<TypedResourceGrid> grids, int units)
    {
        if (units < 0) throw new ArgumentOutOfRangeException(nameof(units));
        int left = units;
        foreach (var grid in grids)
        {
            int n = Math.Min(left, grid.MaxResources - grid.Resources);
            grid.AddResources(n);
            left -= n;
            if (left == 0) break;
        }
        return units - left;
    }

    /// <summary>Removes tech from vaults in order. Returns the units removed.</summary>
    public static int Remove(SimRoot sim, int units)
    {
        if (units < 0) throw new ArgumentOutOfRangeException(nameof(units));
        int left = units;
        foreach (var grid in All(sim))
        {
            int n = Math.Min(left, grid.Resources);
            grid.AddResources(-n);
            left -= n;
            if (left == 0) break;
        }
        return units - left;
    }
}

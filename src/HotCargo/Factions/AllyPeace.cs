using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Hunters;

namespace HotCargo.Factions;

/// <summary>
/// Spec 1150: in corporate ally space the ally faction's ships and the hunters are not enemies, so neither side targets or damages the other,
/// and the faction does not join a fight between the players and the hunters. The patch is on the one vanilla enemy check
/// (<see cref="CareerSimModeManager.AreEnemies"/>), which AI targeting, auto-fire and damage all read.
/// </summary>
public static class AllyPeace
{
    /// <summary>
    /// The peace of the current system and the sim that it belongs to, or null outside corporate ally space.
    /// The sim tick sets it from sim state only (zone, roles, live hunters), so every co-op machine reads the same value at the same tick.
    /// AreEnemies runs very often, so it reads this value and does not compute the zone itself.
    /// </summary>
    static (SimRoot Sim, HunterPeace Peace)? _current;

    internal static void Register()
    {
        Run.Hooks.Tick += OnTick;
        Run.Hooks.RunEnded += _ => _current = null;
    }

    // Limit: a hunter that leaves stasis is covered from the next tick, so for one tick the faction AI can see it as an enemy.
    // Revisit if a playtest shows a faction ship that starts a fight with a hunter that just arrived.
    static void OnTick(Run run, SimRoot sim, double _)
    {
        var zone = FactionsArea.ZoneOf(run, run.Game.CurrentMapNode);
        if (zone != Zone.Ally) { _current = null; return; }
        var ally = FactionsArea.Roles(run).Ally;
        _current = FactionsModel.Peace(zone, ally.MilitaryPlayerIndex, ally.CivilianPlayerIndex, HunterIndex(sim)) is { } peace ? (sim, peace) : null;
    }

    /// <summary>The shared player index of the live hunters (the same pick as HunterSpawner: an allocated index), or null with none.</summary>
    static int? HunterIndex(SimRoot sim)
    {
        int maxPlayers = ((CareerSimModeManager)sim.Mode).Rules.MaxPlayers;
        foreach (var h in HunterSpawner.Hunters(sim))
            if (h.Metadata.PlayerIndex >= maxPlayers) return h.Metadata.PlayerIndex;
        return null;
    }

    /// <summary>Spec 1150: a ship of the ally faction in corporate ally space. It takes no hire contract: it does not fight the hunters here.</summary>
    public static bool IsAllyFactionHere(Ship ship) =>
        _current is { } c && c.Sim == ship.Sim && c.Peace.IsAllyFaction(ship.Metadata.PlayerIndex);

    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.AreEnemies))]
    static class AreEnemiesPatch
    {
        // No Errors.Guard: this runs for every enemy check, and the body has no call that can throw.
        static void Postfix(CareerSimModeManager __instance, int player1, int player2, ref bool __result)
        {
            if (__result && _current is { } c && c.Sim == __instance.Sim && c.Peace.Covers(player1, player2)) __result = false;
        }
    }
}

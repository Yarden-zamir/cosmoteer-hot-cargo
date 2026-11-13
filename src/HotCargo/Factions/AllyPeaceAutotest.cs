using Cosmoteer.Factions;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts.Weapons;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Hunters;

namespace HotCargo.Factions;

/// <summary>Autotest commands of spec 1150: counts the ticks in which the ally faction, the hunters and the players target each other.</summary>
static class AllyPeaceAutotest
{
    /// <summary>The faction whose ships count, or null while nothing is watched.</summary>
    static FactionRules? _watched;
    static int _allyHits, _hunterHits, _playerHits;

    internal static void Register()
    {
        // fa_peace_watch ally|here: resets the counts and counts the ships of the corporation's ally or of this system's controller.
        // Read only, so it is safe in co-op too.
        Autotest.Commands["fa_peace_watch"] = arg =>
        {
            var run = Autotest.RequireRun();
            _watched = arg switch
            {
                "ally" => FactionsArea.Roles(run).Ally,
                "here" => FactionsArea.Controller(run.Game.CurrentMapNode) ?? throw new InvalidOperationException("this system has no controlling faction"),
                _ => throw new InvalidOperationException("fa_peace_watch needs: ally|here"),
            };
            _allyHits = _hunterHits = _playerHits = 0;
            Log.Info($"AUTOTEST factions peace: watching {_watched.ID}");
        };
        Run.Hooks.Tick += OnTick;
        // Ticks in which a ship of the watched faction targets a hunter, a hunter targets a ship of that faction, and a human ship targets a hunter.
        Autotest.Fields["fa_peace_allyhits"] = () => _allyHits;
        Autotest.Fields["fa_peace_hunterhits"] = () => _hunterHits;
        Autotest.Fields["fa_peace_playerhits"] = () => _playerHits;
        Autotest.Fields["fa_peace_ships"] = () => _watched is { } f ? Ships(f, Autotest.RequireRun().Game.Sim).Count() : 0;
    }

    static IEnumerable<Ship> Ships(FactionRules faction, SimRoot sim) => sim.Ships.Where(s => !s.IsJunk && faction.MatchesPlayer(s.Metadata.PlayerIndex));

    static void OnTick(Run run, SimRoot sim, double _)
    {
        if (_watched is not { } faction) return;
        var hunters = HunterSpawner.Hunters(sim).ToList();
        if (hunters.Count == 0) return;
        var allies = Ships(faction, sim).ToList();
        if (allies.Any(a => hunters.Any(h => Targets(a, h)))) _allyHits++;
        if (hunters.Any(h => allies.Any(a => Targets(h, a)))) _hunterHits++;
        if (sim.Ships.HumanPlayerShips.Any(p => hunters.Any(h => Targets(p, h)))) _playerHits++;
    }

    /// <summary>The ship's current attack order or a turret aims at the other ship.</summary>
    static bool Targets(Ship ship, Ship other) =>
        ship.Commands.CurConfirmedCommand is IAttackCommand { Target: var t } && t == other
        || ship.Weapons.Any(w => w is TurretWeapon { Target: { } target } && target.Ship == other);
}

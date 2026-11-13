using Cosmoteer.Factions;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using HotCargo.Ai;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Tech;

namespace HotCargo.Diplomacy;

/// <summary>Sim reads that feed the chances (spec 561 to 585). Read only, stable order, so the UI and the MP input get the same numbers at one tick.</summary>
public static class Situation
{
    public static CareerSimModeManager Career(SimRoot sim) =>
        sim.Mode as CareerSimModeManager ?? throw new InvalidOperationException("diplomacy needs a career simulation");

    public static bool IsHunter(Ship ship) => ship.HasTag(HunterSpawner.HunterTag);

    public static bool IsStation(Ship ship) => ship.TryGetSpecialComponent<SpaceStation>(out _);

    /// <summary>Firepower times health: a damaged ship counts for less.</summary>
    public static float Strength(Ship ship) => ship.Weapons.GetTotalFirePower() * HunterOrders.HealthFraction(ship);

    static bool InArea(Ship center, Ship other, float radius) => center.DetWorldCenter.DistanceSquaredTo(other.DetWorldCenter) <= radius * radius;

    /// <summary>
    /// Leverage of the players over an AI ship (spec 563): human ships and ships hired by the players in the area, against the AI ship
    /// and its team in the area.
    /// </summary>
    public static float Leverage(Run run, SimRoot sim, Ship ai)
    {
        float radius = run.T.Diplomacy.AreaRadius;
        var hired = run.State.Diplomacy.Hires.Select(h => h.Ship).ToHashSet();
        float players = 0, other = 0;
        foreach (var s in sim.Ships)
        {
            if (!s.IsSignificant || s.IsJunk || !InArea(ai, s, radius)) continue;
            if (s.IsHumanPlayerShip || hired.Contains(s.UniqueID)) players += Strength(s);
            else if (s == ai || s.Metadata.Team == ai.Metadata.Team) other += Strength(s);
        }
        return DiplomacyModel.Leverage(players, other);
    }

    /// <summary>AI ships of the same team in the area, without the ship itself.</summary>
    public static int Allies(Run run, Ship ai) => AiGate.Allies(ai).Count(s => InArea(ai, s, run.T.Diplomacy.AreaRadius));

    /// <summary>Corporate hunters in the area of a hunter, without it.</summary>
    public static int OtherHunters(Run run, SimRoot sim, Ship hunter) =>
        HunterSpawner.Hunters(sim).Count(h => h != hunter && InArea(hunter, h, run.T.Diplomacy.AreaRadius));

    public static FactionRules? Faction(Ship ship) => FactionRules.GetFaction(ship.Metadata.Team);

    /// <summary>Reputation with the ship's faction, -1..1. Ships without a faction give 0.</summary>
    public static float Reputation(Run run, Ship ship) =>
        Faction(ship) is { } f ? DiplomacyModel.ReputationShare(run.Mode.GetFactionReputation(f.ID), run.T.Diplomacy.ReputationScale) : 0f;

    public static bool Wanted(SimRoot sim, int player, Ship ship) => Career(sim).Wanted.IsPlayerWantedBy(player, ship.Metadata.PlayerIndex);

    /// <summary>Wanted by any faction in the system. Vanilla keeps one wanted state for all human players.</summary>
    public static bool WantedAnywhere(SimRoot sim, int player) => Career(sim).Wanted.IsLocalPlayerWanted;

    /// <summary>The fame title as 0..1 (first to last title).</summary>
    public static float Fame(Run run)
    {
        var titles = run.Mode.Rules.FameTitles;
        int index = Array.IndexOf(titles, run.Mode.GetCurrentFameTitle());
        return titles.Length <= 1 ? 0f : Math.Max(0, index) / (float)(titles.Length - 1);
    }

    public static bool CanPay(Run run, int player, int credits) =>
        credits <= 0 || run.Mode.AllowDebt || run.Game.GetPlayerState(player, deterministic: true).Money >= credits;

    /// <summary>Tech Vault grids, the given player's ships first, then the other human ships. Removal takes from the front.</summary>
    public static List<TypedResourceGrid> VaultsFor(SimRoot sim, int player) =>
        Vaults.HumanShips(sim)
            .OrderBy(s => s.Commands.CanExecuteCrewCommandsFrom(player) ? 0 : 1).ThenBy(s => s.Metadata.PlayerIndex).ThenBy(s => s.UniqueID)
            .SelectMany(Vaults.OnShip)
            .ToList();

    /// <summary>Tech units in the vaults of the human fleet.</summary>
    public static int VaultTech(SimRoot sim) => Vaults.All(sim).Sum(g => g.Resources);

    /// <summary>The piles that stay after removing <paramref name="units"/> from the front of <see cref="VaultsFor"/>.</summary>
    public static List<TechPile> PilesAfter(SimRoot sim, int player, int units)
    {
        var piles = new List<TechPile>();
        int take = units;
        foreach (var g in VaultsFor(sim, player))
        {
            int taken = Math.Min(take, g.Resources);
            take -= taken;
            int left = g.Resources - taken;
            if (left <= 0 || g.Part is not { } part) continue;
            var at = part.Ship.DetTransformPointToWorld(part.LocalPhysicalCenter);
            piles.Add(new TechPile(at.X, at.Y, left));
        }
        return piles;
    }

    public static float Concentration(Run run, SimRoot sim, int player, int units) =>
        DiplomacyModel.Concentration(PilesAfter(sim, player, units), VaultTech(sim), run.T.Diplomacy.PileDistance);

    /// <summary>Removes tech from the front of <see cref="VaultsFor"/>. Returns the units removed.</summary>
    public static int RemoveTech(SimRoot sim, int player, int units)
    {
        int left = units;
        foreach (var g in VaultsFor(sim, player))
        {
            int n = Math.Min(left, g.Resources);
            g.AddResources(-n);
            left -= n;
            if (left == 0) break;
        }
        return units - left;
    }

    /// <summary>An armed AI ship that is friendly to the player and can talk: not a station, not a hunter, not a wreck.</summary>
    public static bool IsFriendlyShip(Ship caller, Ship ship) =>
        ship.AI is not null && !ship.IsHumanPlayerShip && ship.IsSignificant && !ship.IsJunk && !IsStation(ship) && !IsHunter(ship) && !caller.IsEnemiesWith(ship);

    /// <summary>
    /// Spec 574: a friendly ship that may hold tech. Military ships of the corporation's ally faction (Factions area) never do:
    /// they work with the corporation.
    /// </summary>
    public static bool MayHold(Run run, Ship ship) =>
        !(Cosmoteer.PlayerIndexes.IsFactionMilitary(ship.Metadata.PlayerIndex) && Factions.FactionsArea.IsAlly(run, Faction(ship)));

    /// <summary>
    /// A ship that can fight for the players: it has offensive weapons and an AI with the HcHired module (not a platform or station).
    /// Ships of the corporation's ally faction in its own space are at peace with the hunters (spec 1150), so they take no contract there.
    /// </summary>
    public static bool Hireable(Ship ship) =>
        AiGate.HasOffensiveWeapons(ship) && ship.AI?.GetStrategyModule<HiredStrategy>() is not null && !Factions.AllyPeace.IsAllyFactionHere(ship);

    /// <summary>Distance from a ship to the nearest station of the system.</summary>
    public static float StationDistance(SimRoot sim, Ship ship)
    {
        float best = float.MaxValue;
        foreach (var s in sim.Ships)
            if (s != ship && IsStation(s)) best = Math.Min(best, s.DetWorldCenter.DistanceTo(ship.DetWorldCenter));
        return best;
    }
}

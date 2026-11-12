using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Modes.Career;
using Cosmoteer.Nebulas;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Tactics;

namespace HotCargo.Ai;

public enum Behavior { Flee, Call, Focus, Regroup }

/// <summary>
/// Decides if an enemy AI behavior is on (spec 202), and holds the shared lookups of the behaviors.
/// With no active run every behavior is off, so the vanilla modules decide. All lookups walk the sim ship list in its stable order.
/// </summary>
public static class AiGate
{
    static readonly ID<ShipAIRules> HunterAi = new(HunterSpawner.AiId);

    /// <summary>Tunables when the behavior is on for this kind of AI, else null. hunter = the ship runs the HotCargoHunter AI.</summary>
    public static TacticsTunables? Active(Behavior behavior, bool hunter)
    {
        if (Run.Current is not { } run) return null;
        var t = run.T.Tactics;
        bool on = behavior switch
        {
            Behavior.Flee => hunter ? t.FleeEnabled : t.FleeOnVanilla,
            Behavior.Call => hunter ? t.CallEnabled : t.CallOnVanilla,
            Behavior.Focus => hunter ? t.FocusEnabled : t.FocusOnVanilla,
            Behavior.Regroup => hunter ? t.RegroupEnabled : t.RegroupOnVanilla,
            _ => throw new ArgumentOutOfRangeException(nameof(behavior), behavior, null),
        };
        return on ? t : null;
    }

    public static bool IsHunterAi(Ship ship) => ship.AI?.Rules.ID == HunterAi;

    /// <summary>Spec 361. True when personalities are off, or the ship's personality has the trait.</summary>
    public static bool PersonalityAllows(Ship ship, Trait trait, TacticsTunables t) =>
        !t.PersonalitiesEnabled || PersonalityStrategy.Traits(ship, t).Has(trait);

    /// <summary>
    /// A hunter of the corporate response (hc_hunter tag). Only these jump out when they flee and pull hunter groups with calls.
    /// Shady trap and event ambushers run the hunter AI too, but they are mission targets and act like vanilla ships there.
    /// </summary>
    public static bool IsCorporateHunter(Ship ship) => ship.HasTag(HunterSpawner.HunterTag);

    /// <summary>
    /// True when the ship is an enemy of at least one human player right now (wanted states included).
    /// Flee, call and regroup are enemy behaviors, so friendly and neutral ships (escorts, allies, traders) keep their vanilla AI.
    /// </summary>
    public static bool HostileToPlayers(Ship ship)
    {
        if (Run.Current is not { } run || ship.Sim?.Mode is not { } mode) return false;
        int index = ship.Metadata.PlayerIndex;
        foreach (int human in run.Game.HumanPlayersIndexes)
            if (mode.AreEnemies(index, human)) return true;
        return false;
    }

    /// <summary>A ship that surrendered to the players keeps its truce. The behaviors leave it alone.</summary>
    public static bool Surrendered(Ship ship) =>
        ship.Sim?.Mode is CareerSimModeManager career && career.SurrenderedAIs.Contains(ship.UniqueID);

    /// <summary>True when the ship has a weapon part that is not defensive only (point defense does not count).</summary>
    public static bool HasOffensiveWeapons(Ship ship)
    {
        foreach (var weapon in ship.Weapons)
            if (!weapon.Rules.IsDefensiveOnly && weapon.Rules.IncludeInFirepowerEstimation) return true;
        return false;
    }

    /// <summary>The nearest significant enemy ship on the radar of this ship's team, or null.</summary>
    public static Ship? NearestEnemy(Ship ship)
    {
        Ship? best = null;
        float bestDistance = float.MaxValue;
        foreach (var other in HunterOrders.Sim(ship).Ships)
        {
            if (other == ship || !other.IsSignificant || other.IsJunk || !ship.IsEnemiesWith(other)) continue;
            if (!other.IsWithinRadarOfTeam(ship.Metadata.Team)) continue;
            float d = HunterOrders.EdgeDistance(ship, other);
            if (d < bestDistance) { best = other; bestDistance = d; }
        }
        return best;
    }

    /// <summary>The nearest significant human ship that is an enemy of this ship and on its team's radar, or null.</summary>
    public static Ship? NearestHumanEnemy(Ship ship)
    {
        Ship? best = null;
        float bestDistance = float.MaxValue;
        foreach (var human in HunterOrders.Sim(ship).Ships.HumanPlayerSignificantShips)
        {
            if (!ship.IsEnemiesWith(human) || !human.IsWithinRadarOfTeam(ship.Metadata.Team)) continue;
            float d = HunterOrders.EdgeDistance(ship, human);
            if (d < bestDistance) { best = human; bestDistance = d; }
        }
        return best;
    }

    /// <summary>Significant AI ships on the same team, without the ship itself. Same team = same faction, or the same hunter or pirate group.</summary>
    public static IEnumerable<Ship> Allies(Ship ship)
    {
        foreach (var other in HunterOrders.Sim(ship).Ships)
            if (other != ship && other.AI is not null && other.IsSignificant && !other.IsJunk && ship.IsAlliesWith(other))
                yield return other;
    }

    static string[]? _stormSource;
    static HashSet<ID<NebulaTypeRules>> _storms = [];

    /// <summary>
    /// Spec 199. True when the ship is inside a nebula of a CallBlockedInNebulas type at CallStormStrength or more.
    /// An unknown nebula ID throws, so a tunables typo shows instead of silently allowing calls.
    /// </summary>
    public static bool InStorm(Ship ship, TacticsTunables t)
    {
        if (!ReferenceEquals(_stormSource, t.CallBlockedInNebulas))
        {
            var ids = new HashSet<ID<NebulaTypeRules>>();
            foreach (string name in t.CallBlockedInNebulas)
            {
                var id = new ID<NebulaTypeRules>(name);
                if (!GameApp.Rules.TryGetNebulaTypeRules(id, out _)) throw new InvalidOperationException($"Tactics.CallBlockedInNebulas: unknown nebula type '{name}'");
                ids.Add(id);
            }
            _storms = ids;
            _stormSource = t.CallBlockedInNebulas;
        }
        for (int i = 0; i < ship.Nebulas.Count; i++)
        {
            var (type, strength) = ship.Nebulas[i];
            if (strength >= t.CallStormStrength && _storms.Contains(type.ID)) return true;
        }
        return false;
    }
}

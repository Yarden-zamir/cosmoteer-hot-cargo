using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Random;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Diplomacy;
using HotCargo.Events;

namespace HotCargo.Friendly;

/// <summary>
/// Spec 1369 to 1371: the Wayfarers. When the players are clearly outclassed in a fight, a friendly fleet may come to help, at most
/// once per run. It has its own allegiance (not a vanilla faction, so it stays out of galaxy generation): friendly to the players, hostile to
/// their enemies. It fights through the Diplomacy hire. When the fight is over, its leader hails ("pay it forward"); then the fleet
/// repairs itself and jumps out.
/// </summary>
static class PayItForward
{
    /// <summary>
    /// The Wayfarers' player index: above the vanilla faction indexes (100 and up, a few hundred) and below the allocated unique indexes
    /// (CareerSimModeManager starts them at 100,000). Limit: a faction rules entry with this index would clash; revisit if a mod adds one.
    /// </summary>
    public const int Allegiance = 99_000;
    static readonly ID<SimObjectSpawner> Tag = new("hc_wayfarer");
    const string Spawn = "Wayfarers", HailEvent = "pay_it_forward";

    static FriendlyState S(Run run) => run.State.Friendly;

    /// <summary>The players' significant ships, and the enemy ships within PayForwardRadius of one of them.</summary>
    static (List<Ship> Players, List<Ship> Enemies) Sides(Run run, SimRoot sim)
    {
        var players = sim.Ships.HumanPlayerSignificantShips.ToList();
        float r = run.T.Friendly.PayForwardRadius;
        var enemies = sim.Ships.Where(s => s.IsSignificant && !s.IsJunk && !s.IsHumanPlayerShip && players.Any(p => s.IsEnemiesWith(p) && s.DetWorldCenter.DistanceTo(p.DetWorldCenter) <= r))
            .OrderBy(s => s.UniqueID).ToList();
        return (players, enemies);
    }

    /// <summary>Strength of the players and of their near enemies (Situation.Strength: firepower × health).</summary>
    static (float Players, float Enemies) Strengths(Run run, SimRoot sim)
    {
        var (players, enemies) = Sides(run, sim);
        return (players.Sum(Situation.Strength), enemies.Sum(Situation.Strength));
    }

    /// <summary>
    /// Spec 1370: an actual fight. Within PayForwardFightSeconds a player ship fired, or a near enemy fired and the players lost health
    /// since the last check. It updates the saved player health on every call, so call it on every check.
    /// </summary>
    static bool InFight(Run run, SimRoot sim, List<Ship> players, List<Ship> enemies)
    {
        float window = run.T.Friendly.PayForwardFightSeconds;
        double now = sim.LogicalTime;
        bool Fired(Ship s) => Events.PatrolListenModel.FiredRecently(now, s.Weapons.LastWeaponFireTime, window);
        long health = players.Sum(p => p.Parts.Sum(x => (long)x.Health));
        long before = S(run).PlayerHealth;
        S(run).PlayerHealth = health;
        bool hurt = before >= 0 && health < before;
        return players.Any(Fired) || hurt && enemies.Any(Fired);
    }

    /// <summary>Sim tick (Run.Hooks), every PayForwardCheckSeconds of game time: the help roll, the quiet check, the hail, the exit.</summary>
    internal static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("pay it forward tick", () =>
    {
        if (sim != run.Game.Sim) return;
        var t = run.T.Friendly;
        double now = run.State.GameSeconds;
        if (Math.Floor(now / t.PayForwardCheckSeconds) == Math.Floor((now - dt) / t.PayForwardCheckSeconds)) return;
        var s = S(run);
        if (s.Wayfarers.Count == 0) { TryHelp(run, sim); return; }
        s.Wayfarers.RemoveAll(id => sim.Ships[id] is not { IsJunk: false });
        if (s.Wayfarers.Count == 0)
        {
            Log.Info("friendly: the Wayfarers are gone");
            Reset(s);
            return;
        }
        if (s.HailedAt >= 0)
        {
            if (now >= s.HailedAt + t.PayForwardLeaveSeconds) Leave(run, sim);
            return;
        }
        var (_, enemies) = Strengths(run, sim);
        if (enemies > 0f) { s.QuietSince = -1; return; }
        if (s.QuietSince < 0) { s.QuietSince = now; return; }
        if (now - s.QuietSince < t.PayForwardQuietSeconds) return;
        Hail(run, sim);
    });

    static void Reset(FriendlyState s)
    {
        s.Wayfarers.Clear();
        s.QuietSince = -1;
        s.HailedAt = -1;
    }

    /// <summary>
    /// Spec 1370: the first time in a system that the players are clearly outclassed in an actual fight is a qualifying fight; it rolls
    /// once, from the system's ID, so every machine agrees. Enemies near the players with no fight (for example at the run start) do not
    /// count. The Wayfarers come at most once per run (PayForwardDone, saved in the run state).
    /// </summary>
    static void TryHelp(Run run, SimRoot sim)
    {
        var t = run.T.Friendly;
        var node = sim.NodeId();
        if (S(run).PayForwardDone || S(run).HelpRolled.Contains(node)) return;
        var (playerShips, enemyShips) = Sides(run, sim);
        bool fight = InFight(run, sim, playerShips, enemyShips);
        float players = playerShips.Sum(Situation.Strength), enemies = enemyShips.Sum(Situation.Strength);
        if (enemies <= 0f || enemies < t.PayForwardOutclassRatio * Math.Max(players, 1f) || !fight) return;
        S(run).HelpRolled.Add(node);
        float roll = new FastRand((uint)node, 0x5041_5946UL).Single();
        Log.Info($"friendly: outclassed (players {players:0}, enemies {enemies:0}); help roll {roll:0.000} vs {t.PayForwardChance:0.000}");
        if (roll < t.PayForwardChance) Arrive(run, sim);
    }

    static void Arrive(Run run, SimRoot sim)
    {
        var at = SimQueries.FleetCenter(sim) ?? throw new InvalidOperationException("no player ships for the Wayfarers to help");
        var before = sim.Ships.Where(x => x.HasTag(Tag)).Select(x => x.UniqueID).ToHashSet();
        EventLibrary.Spawn(Spawn).Generate(sim, new FastRand(sim.Rand.UInt64()), loadInstantly: true, rootLoc: at, addExistingSpawnTags: true);
        var fleet = sim.Ships.Where(x => x.HasTag(Tag) && !before.Contains(x.UniqueID)).OrderBy(x => x.UniqueID).ToList();
        if (fleet.Count == 0) throw new InvalidOperationException($"{Spawn} made no ships tagged {Tag}");
        // The employer of the hire: the first human player; the hired AI fights the enemies of the players near that player's ships.
        int player = run.Game.HumanPlayersIndexes.Min();
        double until = run.State.GameSeconds + run.T.Friendly.PayForwardFightMinutes * 60.0;
        S(run).PayForwardDone = true;
        foreach (var ship in fleet)
        {
            ship.Metadata.PlayerIndex = Allegiance;
            foreach (var crew in ship.Crew.Assigned) crew.PlayerIndex = Allegiance;
            run.State.Diplomacy.Hires.Add(new Hire { Ship = ship.UniqueID, Player = player, Until = until, Merc = true, HideLabel = true });
            S(run).Wayfarers.Add(ship.UniqueID);
        }
        FriendlyArea.Say(run.Game, FriendlyArea.Text("WayfarersArriveFmt", fleet.Count));
        Log.Info($"friendly: {fleet.Count} Wayfarers arrive to help");
    }

    /// <summary>Spec 1371: the fight is over. The leader's comms show the "pay it forward" page and it hails; the fleet repairs itself.</summary>
    static void Hail(Run run, SimRoot sim)
    {
        var s = S(run);
        s.HailedAt = run.State.GameSeconds;
        run.State.Diplomacy.Hires.RemoveAll(h => s.Wayfarers.Contains(h.Ship));
        foreach (var id in s.Wayfarers)
            if (sim.Ships[id] is { } ship)
                foreach (var part in ship.Parts) part.Health = part.Rules.MaxHealth;
        var leader = s.Wayfarers.Min();
        // Only one event page waits at a time (requirement 70): with another page open, the words go to the chat instead.
        if (run.State.Events.Pending is null) EventRunner.Open(run, sim, EventLibrary.Get(HailEvent), lead: "", carrier: leader, carrierHails: true);
        else FriendlyArea.Say(run.Game, FriendlyArea.Text("WayfarersWords"));
        FriendlyArea.Say(run.Game, FriendlyArea.Text("WayfarersRepair"));
        Log.Info("friendly: the fight is over; the Wayfarers hail and repair");
    }

    /// <summary>Effect "wayfarers_leave" (the answer to the hail), and the time-out: every Wayfarer jumps out.</summary>
    internal static void LeaveEffect(Run run, SimRoot sim, int player, PendingEvent p, List<string> lines) => Leave(run, sim);

    static void Leave(Run run, SimRoot sim)
    {
        var s = S(run);
        double due = run.State.GameSeconds + run.T.Friendly.LeaveDelaySeconds;
        foreach (var id in s.Wayfarers)
            if (sim.Ships[id] is not null && !run.State.Diplomacy.Leaving.Any(l => l.Ship == id))
                run.State.Diplomacy.Leaving.Add(new Leaving { Ship = id, Due = due });
        Ux.UxKit.ChatLine(run.Game, FriendlyArea.Text("WayfarersLeave"));
        Log.Info("friendly: the Wayfarers leave");
        Reset(s);
    }

    /// <summary>A jump: the Wayfarers stay in their system.</summary>
    internal static void OnSystemEntered(Run run, bool firstVisit) => Reset(S(run));

    /// <summary>
    /// Spec 1369: the Wayfarers' allegiance is friendly to the players and to faction civilians, and hostile to whatever is hostile to
    /// the players. No Errors.Guard: this runs for every enemy check, and the body has no call that can throw.
    /// </summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.AreEnemies))]
    static class AreEnemiesPatch
    {
        static void Postfix(CareerSimModeManager __instance, int player1, int player2, ref bool __result)
        {
            if (player1 != Allegiance && player2 != Allegiance) return;
            int other = player1 == Allegiance ? player2 : player1;
            if (other == Allegiance || __instance.Game is not { } game) { __result = false; return; }
            bool hostile = false;
            foreach (int h in game.HumanPlayersIndexes) hostile |= __instance.AreEnemies(h, other);
            __result = hostile;
        }
    }

    /// <summary>Spec 1369: the ship label of a Wayfarer.</summary>
    [HarmonyPatch(typeof(ShipIndicatorSource), nameof(ShipIndicatorSource.GetNonMouseoverText))]
    static class Label
    {
        static void Postfix(ShipIndicatorSource __instance, ref string? __result)
        {
            if (__instance.Ship.Metadata.PlayerIndex != Allegiance) return;
            string label = FriendlyArea.Text("LabelWayfarer");
            __result = __result is null ? label : label + "\n" + __result;
        }
    }
}

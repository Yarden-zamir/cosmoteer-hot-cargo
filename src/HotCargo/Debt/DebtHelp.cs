using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.HitEffects;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Tech;

namespace HotCargo.Debt;

/// <summary>Spec 1430 to 1437: a friendly faction helps in a fight and asks for pay after it, by the share of the damage it dealt.</summary>
static class DebtHelp
{
    internal static bool IsEnemyOfPlayers(SimRoot sim, Ship ship) =>
        !ship.IsHumanPlayerShip && !ship.IsJunk && sim.Game is { } game && game.HumanPlayersIndexes.Any(i => sim.Mode.AreEnemies(i, ship.Metadata.PlayerIndex));

    /// <summary>Spec 1430: enemy ships of the players within the fight radius of a human ship.</summary>
    static int EnemiesNear(Run run, SimRoot sim)
    {
        float r2 = run.T.Debt.HelpFightRadius * run.T.Debt.HelpFightRadius;
        var humans = sim.Ships.HumanPlayerShips.Select(h => h.DetWorldCenter).ToList();
        return sim.Ships.Count(s => IsEnemyOfPlayers(sim, s) && humans.Any(h => h.DistanceSquaredTo(s.DetWorldCenter) <= r2));
    }

    /// <summary>Spec 1430: the system's controlling faction, if it may help now.</summary>
    static FactionRules? Helper(Run run, SimRoot sim)
    {
        if (sim.MapNode?.ControllingFaction is not { } id) return null;
        var t = run.T.Debt;
        string key = id.ToString();
        bool hostileDebt = run.State.Debt.Of(key) is { } d && Lenders.StageOf(run, d) == DebtStage.Hostile;
        bool ok = key != Factions.FactionsArea.Roles(run).Ally.ID.ToString() && run.Mode.GetFactionReputation(id) >= t.HelpMinReputation
                  && !Lenders.Wants(sim, key) && !hostileDebt;
        return ok ? GameApp.Rules.GetFactionRules(id) : null;
    }

    public static float Share(HelpGroup g) => DebtModel.Share(g.HelperDamage, g.PlayerDamage);

    public static int Price(Run run, HelpGroup g) =>
        Events.EventRunner.Credits(run, DebtModel.HelpPriceUnits(Share(g), run.T.Debt.HelpPricePerShare), 0f, run.CurrentTier, run.TechValue());

    /// <summary>The asking group of a hailing helper, or null.</summary>
    public static HelpGroup? AskingAt(Run run, Ship ship) =>
        run.State.Result == RunResult.Running && run.State.Debt.Help is { Hailer: { } h } g && h == ship.UniqueID ? g : null;

    /// <summary>Runs once a second of sim ticks, after the debt tick.</summary>
    public static void Tick(Run run, SimRoot sim, double now)
    {
        var t = run.T.Debt;
        var s = run.State.Debt;
        if (sim.Tick % 60 != 0) return;
        if (s.Help is not { } g)
        {
            var node = sim.NodeId();
            if (!t.HelpEnabled || s.HelpSystems.Contains(node) || EnemiesNear(run, sim) < t.HelpMinEnemies) return;
            s.HelpSystems.Add(node);
            if (sim.Rand.Single() >= t.HelpChance || Helper(run, sim) is not { } faction || SimQueries.FleetCenter(sim) is not { } center)
            {
                Log.Info("debt: no help in this fight");
                return;
            }
            Come(run, sim, faction, center);
            return;
        }
        g.Ships.RemoveAll(id => sim.Ships[id] is not { IsJunk: false } ship || !ship.ExistsInSim(sim));
        if (g.Ships.Count == 0) { s.Help = null; return; }
        if (g.Hailer is null)
        {
            if (EnemiesNear(run, sim) > 0) { g.CalmSince = -1; return; }
            if (g.CalmSince < 0) { g.CalmSince = now; return; }
            if (now - g.CalmSince < t.HelpCalmSeconds) return;
            if (Share(g) < t.HelpMinShare)
            {
                DebtArea.Chat(run, DebtArea.Format("HelpIdleFmt", Lenders.Name(g.Faction)));
                Leave(run, sim, g);
                return;
            }
            g.Hailer = g.Ships[0];
            g.LeaveAt = now + t.HelpWaitSeconds;
            Log.Info($"debt: helpers of {g.Faction} ask for pay, share {Share(g):0.00}, price {Price(run, g)}");
        }
        if (sim.Ships[g.Hailer.Value] is not { } hailer) { s.Help = null; return; }
        if (!g.Hailed && !(g.Hailed = Events.EventHail.TryHail(sim, hailer.UniqueID)) && SimQueries.NearestHumanShip(sim, hailer.DetWorldCenter) is { } target)
            Hunters.HunterOrders.MoveTo(hailer, target.DetWorldCenter, 150f);
        if (now >= g.LeaveAt)
        {
            // Spec 1436: no answer puts the price on the tab.
            Tab(run, g);
            Leave(run, sim, g);
        }
    }

    internal static void Come(Run run, SimRoot sim, FactionRules faction, Halfling.Geometry.Vector2 center)
    {
        var g = new HelpGroup { Faction = faction.ID.ToString() };
        for (int i = 0; i < run.T.Debt.HelpShips; i++)
            g.Ships.Add(DebtTick.SpawnFactionShip(run, sim, faction.ID, center, run.T.Debt.HelpDistance).UniqueID);
        run.State.Debt.Help = g;
        DebtArea.Center(run, DebtArea.Format("HelpCenterFmt", faction.NameKey.Text));
        DebtArea.Chat(run, DebtArea.Format("HelpChatFmt", faction.NameKey.Text));
        Log.Info($"debt: {g.Ships.Count} ships of {faction.ID} come to help");
    }

    public static void Leave(Run run, SimRoot sim, HelpGroup g)
    {
        foreach (var id in g.Ships)
            if (sim.Ships[id] is { } ship && ship.ExistsInSim(sim)) Pursuit.Removals.JumpOut(run, ship);
        run.State.Debt.Help = null;
    }

    /// <summary>Spec 1435: the price goes on the debt with the helpers' faction.</summary>
    public static void Tab(Run run, HelpGroup g)
    {
        int price = Price(run, g);
        DebtDeals.Owe(run, g.Faction, price);
        DebtArea.Chat(run, DebtArea.Format("HelpTabFmt", Cosmoteer.Extensions.ToXmlCostString(price), Lenders.Name(g.Faction)));
    }

    /// <summary>
    /// Spec 1432: every damage hit on an enemy of the players counts for the helpers or for the players. Runs in the sim on every
    /// machine. It runs for every hit, so it returns early and allocates nothing while no helpers are here.
    /// </summary>
    [HarmonyPatch(typeof(DamageEffectRules), nameof(DamageEffectRules.DoEffect))]
    static class Hits
    {
        static void Postfix(DamageEffectRules __instance, HitEffectParams effectParams)
        {
            if (Run.Current is not { State.Debt.Help: { Hailer: null } g } run || effectParams.SourceShip is not { } source || effectParams.HitShip is not { } hit) return;
            try
            {
                bool helper = g.Ships.Contains(source.UniqueID);
                if (!helper && !source.IsHumanPlayerShip) return;
                if (!IsEnemyOfPlayers(effectParams.Sim, hit)) return;
                long damage = Math.Max(0, __instance.Damage.GetValue(effectParams));
                if (helper) g.HelperDamage += damage;
                else g.PlayerDamage += damage;
            }
            catch (Exception ex) { Errors.Report("debt help hits", ex.Message, ex); }
        }
    }
}

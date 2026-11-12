using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Hunters;

namespace HotCargo.Pursuit;

/// <summary>
/// Spec 915 to 919: the relay blockade. At a blockade stage the stage's first group parks at the relay that the players need.
/// A ship chases when players come close or hit it, so players can fight through, lure the ships away or jump blind.
/// </summary>
public static class Blockade
{
    /// <summary>Spec 915. Runs in the stage hook after the Hunters area spawned the stage's first group.</summary>
    internal static void OnStageStarted(Run run, SimRoot sim, int stage)
    {
        var t = run.T.Pursuit;
        if (!t.Enabled || !t.BlockadeStages.Contains(run.T.Heat.Stages[stage].Name) || HunterSpawner.LastSpawned.Count == 0) return;
        if (run.State.Pursuit.BlockadeRelay is not null || !Pacer.MayStart(run, PacedKinds.Blockade)) return;
        if (sim.Rand.Single() >= t.BlockadeChance) return;
        Start(run, sim, HunterSpawner.LastSpawned);
    }

    /// <summary>
    /// Spec 916: the relay that the players need: the one toward their confirmed destination, else the nearest relay to an unvisited system,
    /// else the nearest relay. Sim state only.
    /// </summary>
    internal static MetaFtlGate? NeededRelay(Run run, SimRoot sim)
    {
        var relays = FtlGate.GetGates(sim).OrderBy(g => g.DestinationID).ToList();
        if (relays.Count == 0 || SimQueries.FleetCenter(sim) is not { } fleet) return null;
        if (run.Game.DestinationMapNode.Confirmed is { } dest && run.Game.Map.Nodes.GetFirstNodeInPathTo(dest, requireJumpable: true) is { } first
            && FtlGate.GetGateForDestination(sim, first) is { } toDest)
            return toDest;
        var unvisited = relays.Where(g => !run.State.VisitedNodes.Contains(g.DestinationID)).ToList();
        return (unvisited.Count > 0 ? unvisited : relays).MinBy(g => g.DetWorldCenter.DistanceSquaredTo(fleet));
    }

    internal static bool Start(Run run, SimRoot sim, IReadOnlyList<ObjectID> ships)
    {
        if (NeededRelay(run, sim) is not { } relay) { Log.Info("pursuit: no relay for a blockade"); return false; }
        var s = run.State.Pursuit;
        s.BlockadeRelay = relay.DestinationID;
        s.Blockaders = ships.ToList();
        Pacer.Started(run, PacedKinds.Blockade);
        string where = relay.DestinationNode?.Name ?? "?";
        Log.Info($"pursuit: blockade of {ships.Count} ships at the relay to {where}");
        string text = PursuitText.Format("BlockadeFmt", where);
        PursuitText.Center(run, $"<bad>{text}</bad>");
        PursuitText.Chat(run, text);
        return true;
    }

    static Vector2? RelayAt(Run run, SimRoot sim) =>
        run.State.Pursuit.BlockadeRelay is { } id ? FtlGate.GetGates(sim).Where(g => g.DestinationID == id).Select(g => (Vector2?)g.DetWorldCenter).FirstOrDefault() : null;

    /// <summary>Each blockade ship has its own spot on a circle around the relay, by its place in the list.</summary>
    static Vector2? ParkSpot(Run run, SimRoot sim, ObjectID ship)
    {
        var s = run.State.Pursuit;
        int index = s.Blockaders.IndexOf(ship);
        if (index < 0 || RelayAt(run, sim) is not { } relay) return null;
        float angle = MathF.Tau * index / Math.Max(1, s.Blockaders.Count);
        return relay + Vector2.FromAngle(angle) * run.T.Pursuit.BlockadeParkRadius;
    }

    /// <summary>Spec 916, 917: a blockade ship parks at its spot until a human ship comes within BlockadeAggroRange or it takes damage. AI tick.</summary>
    internal static bool Override(Run run, Ship ship)
    {
        var s = run.State.Pursuit;
        if (!s.Blockaders.Contains(ship.UniqueID) || ship.Sim is not { } sim) return false;
        float range = run.T.Pursuit.BlockadeAggroRange;
        bool close = sim.Ships.HumanPlayerSignificantShips.Any(h => HunterOrders.EdgeDistance(ship, h) <= range);
        if (close || HunterOrders.HealthFraction(ship) < 0.98f)
        {
            s.Blockaders.Remove(ship.UniqueID);
            Log.Info($"pursuit: blockade ship {ship.Metadata.PlainShipName} chases (close={close})");
            return false;
        }
        if (ParkSpot(run, sim, ship.UniqueID) is not { } spot) return false;
        HunterOrders.MoveTo(ship, spot, 150f);
        return true;
    }

    /// <summary>Spec 916: in stasis a blockade ship flies to its spot, not to the players.</summary>
    internal static Vector2? StasisGoal(Run run, SimRoot sim, ObjectID id) => ParkSpot(run, sim, id);

    /// <summary>Drops blockade ships that are gone. The blockade ends when none park.</summary>
    internal static void Tick(Run run, SimRoot sim)
    {
        var s = run.State.Pursuit;
        if (s.BlockadeRelay is null) return;
        var live = SimQueries.TaggedShips(sim, HunterSpawner.HunterTag).Select(h => h.Id).ToHashSet();
        s.Blockaders.RemoveAll(id => !live.Contains(id));
        if (s.Blockaders.Count > 0) return;
        Log.Info("pursuit: blockade over");
        s.BlockadeRelay = null;
    }
}

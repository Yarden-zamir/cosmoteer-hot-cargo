using Cosmoteer.Game;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Hunters;

namespace HotCargo.Pursuit;

/// <summary>
/// Spec 910 to 914: the scout that reports. At the scout stage the first hunter can fly to the nearest relay instead of hunting.
/// If it gets there, it reports the players' position: heat rises. Destroyed or stopped first, it reports nothing.
/// </summary>
public static class Scout
{
    /// <summary>Spec 910. Runs in the stage hook after the Hunters area spawned the stage's first group.</summary>
    internal static void OnStageStarted(Run run, SimRoot sim, int stage)
    {
        var t = run.T.Pursuit;
        if (!t.Enabled || run.T.Heat.Stages[stage].Name != t.ScoutStage || HunterSpawner.LastSpawned.Count == 0) return;
        if (sim.Rand.Single() >= t.ScoutChance) return;
        Start(run, sim, HunterSpawner.LastSpawned[0]);
    }

    /// <summary>Makes a live hunter a reporting scout that flies to the relay nearest to it. Returns false without a hunter or a relay.</summary>
    internal static bool Start(Run run, SimRoot sim, ObjectID id)
    {
        if (sim.Ships[id] is not { } ship || !ship.HasTag(HunterSpawner.HunterTag)) return false;
        Vector2 from = ship.DetWorldCenter;
        var relays = FtlGate.GetGates(sim).OrderBy(g => g.DestinationID).ToList();
        if (relays.Count == 0) { Log.Info("pursuit: no relay in this system, the scout hunts"); return false; }
        var relay = relays.MinBy(g => g.DetWorldCenter.DistanceSquaredTo(from));
        run.State.Pursuit.Scouts.Add(new ScoutReport
        {
            Ship = id, Relay = relay.DestinationID, RelayAt = relay.DetWorldCenter, StartDistance = relay.DetWorldCenter.DistanceTo(in from),
        });
        Log.Info($"pursuit: scout {ship.Metadata.PlainShipName} flies to report at the relay to {relay.DestinationNode?.Name}, {relay.DetWorldCenter.DistanceTo(in from):0} away");
        PursuitText.Chat(run, PursuitText.Format("ScoutChatFmt", ship.Metadata.PlainShipName));
        return true;
    }

    static ScoutReport? Of(Run run, ObjectID id) => run.State.Pursuit.Scouts.FirstOrDefault(s => s.Ship == id);

    /// <summary>Spec 911: a scout flies to its relay and does not fight. AI tick.</summary>
    internal static bool Override(Run run, Ship ship)
    {
        if (Of(run, ship.UniqueID) is not { } s) return false;
        HunterOrders.MoveTo(ship, s.RelayAt, 100f);
        return true;
    }

    /// <summary>Spec 911: in stasis a scout keeps flying to its relay.</summary>
    internal static Vector2? StasisGoal(Run run, ObjectID id) => Of(run, id)?.RelayAt;

    /// <summary>Spec 912, 913. Each sim tick: a scout at its relay reports, a scout that is gone or stopped reports nothing.</summary>
    internal static void Tick(Run run, SimRoot sim)
    {
        var scouts = run.State.Pursuit.Scouts;
        for (int i = 0; i < scouts.Count; i++)
        {
            var s = scouts[i];
            var ship = sim.Ships[s.Ship];
            var stasis = ship is null ? sim.Stasis.FirstOrDefault(x => x.ReservedObjectID == s.Ship) : null;
            if (ship is null && stasis is null || ship is not null && Stopped(sim, ship))
            {
                Log.Info($"pursuit: scout {s.Ship} stopped before it reported");
                scouts.RemoveAt(i--);
                continue;
            }
            Vector2 at = ship?.DetWorldCenter ?? stasis!.Location;
            if (at.DistanceTo(s.RelayAt) > run.T.Pursuit.ScoutRelayRadius) continue;
            scouts.RemoveAt(i--);
            if (ship is not null)
            {
                HunterSpawner.ForgetHeldFuel(run, ship);
                Removals.JumpOut(run, ship);
            }
            else sim.Stasis.RemoveStasisSpawner(stasis!);
            Report(run, sim);
        }
    }

    /// <summary>Spec 913: a wreck, a ship that surrendered or made a truce, or one that lost the hunter tag (it agreed to leave).</summary>
    static bool Stopped(SimRoot sim, Ship ship)
    {
        var career = (CareerSimModeManager)sim.Mode;
        return ship.IsJunk || !ship.HasTag(HunterSpawner.HunterTag) || career.SurrenderedAIs.Contains(ship.UniqueID) || career.SurrenderedToAIs.Contains(ship.UniqueID);
    }

    /// <summary>Spec 912: heat + ScoutReportHeat, a center text and a chat line. During an outage the gain is blocked and the text says +0.</summary>
    static void Report(Run run, SimRoot sim)
    {
        float heat = run.T.Pursuit.ScoutReportHeat;
        bool blocked = Outage.Active(run);
        run.AddHeat(heat, sim);
        string text = PursuitText.Format("ScoutReportedFmt", blocked ? 0 : heat);
        Log.Info($"pursuit: scout reported, heat +{(blocked ? 0 : heat):0}");
        PursuitText.Center(run, $"<bad>{text}</bad>");
        PursuitText.Chat(run, text);
    }

    /// <summary>Spec 911: the marker of a reporting scout: REPORTING, a bar of how far it got and its distance to the relay. UI only.</summary>
    [HarmonyPatch(typeof(ShipIndicatorSource), nameof(ShipIndicatorSource.GetNonMouseoverText))]
    static class Marker
    {
        static void Postfix(ShipIndicatorSource __instance, ref string? __result)
        {
            if (Run.Current is not { } run || __instance.Ship is not { } ship || Of(run, ship.UniqueID) is not { } s) return;
            try
            {
                float d = ship.DetWorldCenter.DistanceTo(s.RelayAt);
                string bar = PursuitModel.Bar(PursuitModel.ReportProgress(s.StartDistance, d), 10);
                string label = $"<bad><b>{PursuitText.Text("Reporting")}</b> [{bar}] {d:N0}</bad>";
                __result = __result is null ? label : label + "\n" + __result;
            }
            catch (Exception ex) { Errors.Report("pursuit scout marker", ex.Message, ex); }
        }
    }
}

using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Diplomacy;
using HotCargo.Events;

namespace HotCargo.Friendly;

/// <summary>
/// Spec 1362, 1363: effect "apprentice". The carrier follows and fights like a hired ship (Diplomacy hire, spec 580 to 582) for
/// ApprenticeMinutes. Enemy ships that die near it count as fights it saw. Then it joins the chooser's fleet (enough fights) or thanks
/// the players and jumps out. Its ship label shows the fights and the goal.
/// </summary>
static class Apprentice
{
    static FriendlyState S(Run run) => run.State.Friendly;

    internal static void Apply(Run run, SimRoot sim, int player, PendingEvent p, List<string> lines)
    {
        var id = p.Carrier ?? throw new InvalidDataException($"event {p.EventId}: Effect apprentice needs a Carrier");
        if (sim.Ships[id] is not { IsJunk: false } ship)
        {
            lines.Add(FriendlyArea.Text("FollowerGone"));
            return;
        }
        var t = run.T.Friendly;
        double until = run.State.GameSeconds + t.ApprenticeMinutes * 60.0;
        ship.Tags?.Remove(EventHail.CarrierTag);
        // The hire ends at the same time; this area's tick decides first (it runs before the diplomacy tick).
        run.State.Diplomacy.Hires.Add(new Hire { Ship = id, Player = player, Until = until });
        S(run).Apprentices.Add(new ApprenticeState { Ship = id, Player = player, Until = until });
        string line = FriendlyArea.Text("ApprenticeFollowsFmt", ship.Metadata.XmlShipName, t.ApprenticeMinutes.ToString("0"), t.ApprenticeJoinFights);
        lines.Add(line);
        FriendlyArea.Say(run.Game, line);
        Log.Info($"friendly: {ship.Metadata.PlainShipName} is an apprentice of player {player} until {until:0}");
    }

    /// <summary>Sim tick (Run.Hooks): decides apprentices whose time is up, and ends those that ran or are gone.</summary>
    internal static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("apprentice tick", () =>
    {
        if (sim != run.Game.Sim) return;
        var list = S(run).Apprentices;
        if (list.Count == 0) return;
        var t = run.T.Friendly;
        double now = run.State.GameSeconds;
        foreach (var a in list.ToList())
        {
            var ship = sim.Ships[a.Ship];
            if (ship is null || ship.IsJunk)
            {
                list.Remove(a);
                Log.Info($"friendly: apprentice {a.Ship} is gone");
                continue;
            }
            var hire = run.State.Diplomacy.Hires.FirstOrDefault(h => h.Ship == a.Ship);
            // It ran from damage (hired flee, spec 582): it saw enough of the danger and leaves.
            if (hire is null || hire.Fleeing)
            {
                if (hire is not null) run.State.Diplomacy.Hires.Remove(hire);
                End(run, sim, ship, a, join: false, "ApprenticeRanFmt");
                continue;
            }
            if (now < a.Until) continue;
            run.State.Diplomacy.Hires.Remove(hire);
            bool join = a.Fights >= t.ApprenticeJoinFights;
            End(run, sim, ship, a, join, join ? "ApprenticeJoinsFmt" : "ApprenticeLeavesFmt");
        }
    });

    static void End(Run run, SimRoot sim, Ship ship, ApprenticeState a, bool join, string key)
    {
        S(run).Apprentices.Remove(a);
        if (join) Join.MakePlayerShip(ship, a.Player);
        else run.State.Diplomacy.Leaving.Add(new Leaving { Ship = a.Ship, Due = run.State.GameSeconds });
        FriendlyArea.Say(run.Game, FriendlyArea.Text(key, ship.Metadata.XmlShipName, a.Fights, run.T.Friendly.ApprenticeJoinFights));
        Log.Info($"friendly: apprentice {ship.Metadata.PlainShipName} saw {a.Fights} fights; joins: {join}");
    }

    /// <summary>A jump to another system: apprentices cannot follow (AI ships stay), so they stay behind.</summary>
    internal static void OnSystemEntered(Run run, bool firstVisit)
    {
        var list = S(run).Apprentices;
        if (list.Count == 0) return;
        Ux.UxKit.ChatLine(run.Game, FriendlyArea.Text("ApprenticeBehind"));
        Log.Info($"friendly: {list.Count} apprentices stay behind with the jump");
        list.Clear();
    }

    /// <summary>Spec 1363: an enemy of the players that dies near an apprentice counts as a fight it saw. Sim logic.</summary>
    [HarmonyPatch(typeof(CareerSimModeManager), "OnShipJunked")]
    static class FightSeen
    {
        static void Prefix(CareerSimModeManager __instance, Ship ship, int oldAllegiance) => Errors.Guard("apprentice fight", () =>
        {
            if (Run.Current is not { } run || run.Game.Sim != __instance.Sim || S(run).Apprentices.Count == 0) return;
            var game = run.Game;
            if (!game.HumanPlayersIndexes.Any(i => __instance.AreEnemies(oldAllegiance, i))) return;
            float r = run.T.Friendly.ApprenticeWatchRadius;
            foreach (var a in S(run).Apprentices)
            {
                if (__instance.Sim.Ships[a.Ship] is not { } watcher || watcher.DetWorldCenter.DistanceTo(ship.DetWorldCenter) > r) continue;
                a.Fights++;
                Log.Info($"friendly: apprentice {watcher.Metadata.PlainShipName} saw {ship.Metadata.PlainShipName} die ({a.Fights})");
            }
        });
    }

    /// <summary>Spec 1363: the ship label of an apprentice shows the fights it saw and the goal. Reads run state only.</summary>
    [HarmonyPatch(typeof(ShipIndicatorSource), nameof(ShipIndicatorSource.GetNonMouseoverText))]
    static class Label
    {
        static void Postfix(ShipIndicatorSource __instance, ref string? __result)
        {
            if (Run.Current is not { } run || S(run).Apprentices.Count == 0) return;
            try
            {
                var id = __instance.Ship.UniqueID;
                foreach (var a in S(run).Apprentices)
                {
                    if (a.Ship != id) continue;
                    string label = FriendlyArea.Text("LabelApprenticeFmt", a.Fights, run.T.Friendly.ApprenticeJoinFights);
                    __result = __result is null ? label : label + "\n" + __result;
                    return;
                }
            }
            catch (Exception ex) { Errors.Report("apprentice label", ex.Message, ex); }
        }
    }
}

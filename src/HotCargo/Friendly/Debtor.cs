using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Diplomacy;
using HotCargo.Events;

namespace HotCargo.Friendly;

/// <summary>
/// Spec 1367, 1368: effect "debtor". The players bought the carrier's debt. It follows and fights like a mercenary (Diplomacy hire)
/// until it dies or DebtorMinutes pass. Then it pays the chooser the full price of the debt and jumps out. If it dies, the debt is lost.
/// </summary>
static class Debtor
{
    static FriendlyState S(Run run) => run.State.Friendly;

    internal static void Apply(Run run, SimRoot sim, int player, PendingEvent p, List<string> lines)
    {
        var id = p.Carrier ?? throw new InvalidDataException($"event {p.EventId}: Effect debtor needs a Carrier");
        if (sim.Ships[id] is not { IsJunk: false } ship)
        {
            lines.Add(FriendlyArea.Text("FollowerGone"));
            return;
        }
        // The full price that the players paid: the cost of the choice that leads here, as the page showed it.
        var cost = EventLibrary.Get(p.EventId).Choices.Single(c => c.Outcomes.Any(o => o.Effect == "debtor")).Cost
            ?? throw new InvalidDataException($"event {p.EventId}: the debtor choice needs a Cost");
        int credits = EventRunner.CostCredits(run, cost, p);
        double until = run.State.GameSeconds + run.T.Friendly.DebtorMinutes * 60.0;
        ship.Tags?.Remove(EventHail.CarrierTag);
        // A mercenary hire runs at a lower health than a plain hire (spec 582), so it stays in the fight longer.
        run.State.Diplomacy.Hires.Add(new Hire { Ship = id, Player = player, Until = until, Merc = true, HideLabel = true });
        S(run).Debtors.Add(new DebtorState { Ship = id, Player = player, Credits = credits, Until = until, Node = sim.NodeId(), Name = ship.Metadata.XmlShipName });
        string line = FriendlyArea.Text("DebtorFollowsFmt", ship.Metadata.XmlShipName, run.T.Friendly.DebtorMinutes.ToString("0"), credits.ToString("N0"));
        lines.Add(line);
        FriendlyArea.Say(run.Game, line);
        Log.Info($"friendly: player {player} bought the debt of {ship.Metadata.PlainShipName} for {credits} until {until:0}");
    }

    /// <summary>Sim tick (Run.Hooks): a debtor that died loses the debt; one whose time is up pays and leaves.</summary>
    internal static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("debtor tick", () =>
    {
        if (sim != run.Game.Sim) return;
        var list = S(run).Debtors;
        if (list.Count == 0) return;
        double now = run.State.GameSeconds;
        bool here(DebtorState d) => d.Node.Equals(sim.NodeId());
        foreach (var d in list.ToList())
        {
            var ship = here(d) ? sim.Ships[d.Ship] : null;
            bool inStasis = here(d) && ship is null && sim.Stasis.Any(st => st.ReservedObjectID == d.Ship);
            // Dead or gone in this system: the debt dies with it. In another system it cannot die, so it pays at its time.
            if (here(d) && !inStasis && (ship is null || ship.IsJunk))
            {
                list.Remove(d);
                FriendlyArea.Say(run.Game, FriendlyArea.Text("DebtorDiedFmt", d.Name, d.Credits.ToString("N0")));
                Log.Info($"friendly: debtor {d.Ship} died; {d.Credits} lost");
                continue;
            }
            if (now < d.Until) continue;
            list.Remove(d);
            run.Game.GetPlayerState(d.Player, deterministic: true).AddMoney(d.Credits);
            run.State.Diplomacy.Hires.RemoveAll(h => h.Ship == d.Ship);
            if (ship is not null) run.State.Diplomacy.Leaving.Add(new Leaving { Ship = d.Ship, Due = now });
            FriendlyArea.Say(run.Game, FriendlyArea.Text("DebtorPaysFmt", d.Name, d.Credits.ToString("N0")));
            Log.Info($"friendly: debtor {d.Ship} pays {d.Credits} to player {d.Player}");
        }
    });

    /// <summary>A jump: the debtor cannot follow. It stays behind and still pays at its time (spec 1368).</summary>
    internal static void OnSystemEntered(Run run, bool firstVisit)
    {
        if (S(run).Debtors.Count > 0) Ux.UxKit.ChatLine(run.Game, FriendlyArea.Text("DebtorBehind"));
    }

    /// <summary>Spec 1368: a debtor's ship label shows what it owes and the time left. Reads run state only.</summary>
    [HarmonyPatch(typeof(ShipIndicatorSource), nameof(ShipIndicatorSource.GetNonMouseoverText))]
    static class Label
    {
        static void Postfix(ShipIndicatorSource __instance, ref string? __result)
        {
            if (Run.Current is not { } run || S(run).Debtors.Count == 0) return;
            try
            {
                var id = __instance.Ship.UniqueID;
                foreach (var d in S(run).Debtors)
                {
                    if (d.Ship != id) continue;
                    string left = TimeSpan.FromSeconds(Math.Max(0, d.Until - run.State.GameSeconds)).ToString(@"m\:ss");
                    string label = FriendlyArea.Text("LabelDebtorFmt", d.Credits.ToString("N0"), left);
                    __result = __result is null ? label : label + "\n" + __result;
                    return;
                }
            }
            catch (Exception ex) { Errors.Report("debtor label", ex.Message, ex); }
        }
    }
}

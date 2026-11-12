using Cosmoteer;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.Random;
using HotCargo.Ai;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Tech;

namespace HotCargo.Diplomacy;

/// <summary>
/// The sim side of diplomacy (spec 561, 572, 577, 582 to 585): AI hails, leaving hunters, held tech and the end of hires.
/// Runs inside the sim tick (Run.Hooks.Tick). Rolls use a FastRand seeded from the ship, once per ship and kind.
/// </summary>
static class DiplomacyTick
{
    static DiplomacyState S(Run run) => run.State.Diplomacy;

    internal static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("diplomacy tick", () =>
    {
        TickLeaving(run, sim);
        if (!run.T.Diplomacy.Enabled) return;
        if (run.State.GameSeconds < S(run).NextCheck) return;
        S(run).NextCheck = run.State.GameSeconds + run.T.Diplomacy.CheckSeconds;
        TickHeld(run, sim);
        TickHires(run, sim);
        TickHails(run, sim);
    });

    /// <summary>A jump to another system ends every hire: AI ships cannot follow. Held tech stays with its ship.</summary>
    internal static void OnSystemEntered(Run run, bool firstVisit)
    {
        var s = S(run);
        if (s.Hires.Count > 0) Log.Info($"diplomacy: {s.Hires.Count} hires end with the jump");
        s.Hires.Clear();
        s.Leaving.Clear();
        s.FeeAsks.Clear();
    }

    static float Roll(Ship ship, ulong salt) => new FastRand((uint)ship.UniqueID, salt).Single();

    /// <summary>Hunters that agreed to leave: the FTL jump-out effect at Due, then removal, like a fleeing hunter (spec 195).</summary>
    static void TickLeaving(Run run, SimRoot sim)
    {
        var list = S(run).Leaving;
        double now = run.State.GameSeconds;
        for (int i = 0; i < list.Count; i++)
        {
            var l = list[i];
            var ship = sim.Ships[l.Ship];
            if (ship is null || !ship.ExistsInSim(sim)) { list.RemoveAt(i--); continue; }
            if (!l.Jumping && now >= l.Due)
            {
                _ = ship.Renderer.AnimateFtlJumpOut(ship.WorldFlightDirection);
                l.Jumping = true;
                l.RemoveAt = now + ship.Rules.FtlEffects.TotalDuration.Seconds;
                HunterSpawner.ForgetHeldFuel(run, ship);
                AiText.Chat(ship, "JumpedOutFmt");
            }
            else if (l.Jumping && now >= l.RemoveAt)
            {
                sim.EnqueueDeterministic(ship.UniqueID, () => sim.Ships.Remove(ship, dispose: true, handleLostShip: false));
                list.RemoveAt(i--);
            }
        }
    }

    /// <summary>
    /// Spec 577. Held tech in this system follows its ship. A wreck spills the tech as loose nuggets. A ship that is gone (it left the
    /// system, or nothing is left of it) takes the tech with it.
    /// </summary>
    static void TickHeld(Run run, SimRoot sim)
    {
        var node = sim.NodeId();
        var held = S(run).Held;
        var inStasis = sim.Stasis.Select(st => st.ReservedObjectID).ToHashSet();
        for (int i = 0; i < held.Count; i++)
        {
            var h = held[i];
            if (!h.Node.Equals(node)) continue;
            var ship = sim.Ships[h.Ship];
            if (ship is not null && ship.ExistsInSim(sim) && !ship.IsJunk) { h.Location = ship.DetWorldCenter; continue; }
            if (ship is null && inStasis.Contains(h.Ship)) continue;
            if (ship is not null && ship.IsJunk)
            {
                var rect = new RotRect(ship.DetWorldCenter, new Vector2(10f, 10f), Direction.Zero);
                sim.Nuggets.DropNuggets(Vaults.TechId, h.Units, rect, Vector2.Zero, randomVelocity: true);
                Ux.UxKit.ChatLine(run.Game, DipText.Format("HeldWreckFmt", h.ShipName, h.Units));
                Log.Info($"diplomacy: holder {h.ShipName} is a wreck; {h.Units} tech dropped");
            }
            else
            {
                Ux.UxKit.ChatLine(run.Game, DipText.Format("HeldGoneFmt", h.ShipName, h.Units));
                Log.Info($"diplomacy: holder {h.ShipName} is gone with {h.Units} tech");
            }
            held.RemoveAt(i--);
        }
    }

    static void TickHires(Run run, SimRoot sim)
    {
        var hires = S(run).Hires;
        double now = run.State.GameSeconds;
        for (int i = 0; i < hires.Count; i++)
        {
            var h = hires[i];
            var ship = sim.Ships[h.Ship];
            if (ship is null || !ship.ExistsInSim(sim) || ship.IsJunk) { hires.RemoveAt(i--); continue; }
            if (now < h.Until || h.Fleeing) continue;
            Ux.UxKit.ChatLine(run.Game, DipText.Format("HireDoneFmt", ship.Metadata.XmlShipName));
            Log.Info($"diplomacy: hire of {ship.Metadata.PlainShipName} ended");
            hires.RemoveAt(i--);
        }
    }

    /// <summary>The first human ship that can talk with the AI ship now, or null.</summary>
    static Ship? InComms(SimRoot sim, Ship ai)
    {
        var career = Situation.Career(sim);
        return sim.Ships.HumanPlayerSignificantShips.OrderBy(h => h.Metadata.PlayerIndex).ThenBy(h => h.UniqueID)
            .FirstOrDefault(h => career.CanShipCallShip(h, ai) && career.WillShipAnswerCall(h, ai));
    }

    static void Hail(SimRoot sim, Ship ai, Ship human)
    {
        Situation.Career(sim).AIHailPlayer(ai, human);
        Log.Info($"diplomacy: {ai.Metadata.PlainShipName} hails player {human.Metadata.PlayerIndex}");
    }

    static void TickHails(Run run, SimRoot sim)
    {
        var t = run.T.Diplomacy;
        var s = S(run);
        double now = run.State.GameSeconds;
        foreach (var hunter in HunterSpawner.Hunters(sim).ToList())
        {
            if (s.Leaving.Any(l => l.Ship == hunter.UniqueID)) continue;
            // Spec 572: a beaten hunter may offer its surrender, once.
            string yield = DiplomacyState.Key(hunter.UniqueID, "yield");
            if (!s.Rolled.Contains(yield) && HunterOrders.HealthFraction(hunter) < t.HunterYieldHealth)
            {
                s.Rolled.Add(yield);
                if (Roll(hunter, 0x5949_454CUL) < t.HunterYieldChance)
                {
                    s.Rolled.Add(DiplomacyState.Key(hunter.UniqueID, "yieldok"));
                    if (InComms(sim, hunter) is { } h) Hail(sim, hunter, h);
                }
            }
            // Spec 561: the first time a hunter can talk with the players, it may demand the tech.
            string demand = DiplomacyState.Key(hunter.UniqueID, "demand");
            if (s.Rolled.Contains(demand) || InComms(sim, hunter) is not { } human) continue;
            s.Rolled.Add(demand);
            if (Roll(hunter, 0x4445_4D44UL) < t.DemandHailChance && now - s.LastDemandAt >= t.DemandHailGapSeconds && Situation.VaultTech(sim) > 0)
            {
                s.LastDemandAt = now;
                Hail(sim, hunter, human);
            }
        }
        // Spec 584 and 585: mercenaries near stations, and their offers.
        foreach (var ship in sim.Ships.ToList())
        {
            if (ship.AI is null || ship.IsHumanPlayerShip || !ship.IsSignificant || ship.IsJunk || Situation.IsStation(ship) || Situation.IsHunter(ship)) continue;
            if (AiGate.HostileToPlayers(ship) || !Situation.Hireable(ship)) continue;
            string merc = DiplomacyState.Key(ship.UniqueID, "merc");
            if (!s.Rolled.Contains(merc) && Situation.StationDistance(sim, ship) <= t.MercStationRadius)
            {
                s.Rolled.Add(merc);
                if (Roll(ship, 0x4D45_5243UL) < t.MercChance) { s.Mercs.Add(ship.UniqueID); Log.Info($"diplomacy: {ship.Metadata.PlainShipName} is a mercenary"); }
            }
            if (!s.Mercs.Contains(ship.UniqueID) || s.Hires.Any(h => h.Ship == ship.UniqueID)) continue;
            string hail = DiplomacyState.Key(ship.UniqueID, "merchail");
            if (s.Rolled.Contains(hail) || InComms(sim, ship) is not { } to) continue;
            s.Rolled.Add(hail);
            if (Roll(ship, 0x4D48_4149UL) < t.MercHailChance) Hail(sim, ship, to);
        }
    }
}

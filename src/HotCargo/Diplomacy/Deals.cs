using Cosmoteer.Factions;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Random;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Tech;
using Cosmoteer.Game;

namespace HotCargo.Diplomacy;

/// <summary>
/// Runs a hail choice (spec 561 to 583). Called only from the shared choice input (<see cref="DiplomacyChoiceDomain"/>), so every machine runs it at the same tick.
/// It checks the choice again, marks the once-per-ship offer as used, rolls with a seed from the ship and the tick, and writes a result.
/// </summary>
public static class Deals
{
    static DiplomacyState S(Run run) => run.State.Diplomacy;

    public static bool Choose(Run run, SimRoot sim, int player, Ship caller, Ship callee, Offer offer, int amount)
    {
        var t = run.T.Diplomacy;
        if (!t.Enabled || run.State.Result == RunResult.Lost) return false;
        if (!caller.ExistsInSim(sim) || !callee.ExistsInSim(sim) || !caller.IsHumanPlayerShip || !caller.Commands.CanExecuteCrewCommandsFrom(player))
        {
            Log.Info($"diplomacy: {offer} refused, the ships are gone or not the player's");
            return false;
        }
        if (S(run).WasUsed(callee.UniqueID, offer)) { Log.Info($"diplomacy: {offer} to {callee.Metadata.PlainShipName} was already made"); return false; }
        if (!DiplomacyHail.Allowed(run, sim, player, caller, callee, offer, amount, out string why))
        {
            Log.Info($"diplomacy: {offer} {amount} to {callee.Metadata.PlainShipName} refused: {why}");
            return false;
        }
        if (!DiplomacyState.Repeatable(offer)) S(run).Used.Add(DiplomacyState.Key(callee.UniqueID, offer));
        var rand = new FastRand(((ulong)(uint)callee.UniqueID << 32) | (uint)offer, (ulong)(uint)sim.Tick + (ulong)S(run).Serial);
        float roll = rand.Single();
        string name = callee.Metadata.XmlShipName;
        switch (offer)
        {
            case Offer.HandOver:
            {
                int stock = Situation.VaultTech(sim);
                // Spec 562: all units is a sure deal. The normal loss rule then decides if the run ends (held tech keeps it going).
                float chance = amount >= stock ? 1f : Quotes.HandOver(run, sim, player, callee, amount);
                if (DiplomacyModel.Succeeds(roll, chance))
                {
                    // Spec 594: they take the tech, then want more for their trouble. The wave leaves only after the punishment.
                    int n = Situation.RemoveTech(sim, player, amount);
                    S(run).Punish = new Punishment
                    {
                        Serial = int.MinValue + ++S(run).Serial, Hunter = callee.UniqueID, Units = n, Due = run.State.GameSeconds + t.PunishSeconds,
                    };
                    foreach (var h in HunterSpawner.Hunters(sim)) MarkHunterTalksUsed(run, h);
                    Result(run, callee, DipText.Format("HandOverYesFmt", name, n));
                }
                else
                {
                    MarkHunterTalksUsed(run, callee);
                    Result(run, callee, DipText.Format("HandOverNoFmt", name));
                }
                Log.Info($"diplomacy: hand over {amount}/{stock} roll {roll:0.00} chance {chance:0.00}");
                break;
            }
            case Offer.Bluff:
            {
                float chance = Quotes.Bluff(run, sim, player, amount);
                int n = Situation.RemoveTech(sim, player, amount);
                MarkHunterTalksUsed(run, callee);
                if (DiplomacyModel.Succeeds(roll, chance))
                {
                    // Spec 565: they believe the tech is gone, like after a hand-over.
                    WaveLeaves(run, sim, caller);
                    DropHeat(run, sim);
                    Result(run, callee, DipText.Format("BluffYesFmt", name, n));
                }
                else
                {
                    run.AddHeat(t.BluffCaughtHeat, sim);
                    Result(run, callee, DipText.Format("BluffNoFmt", name, n));
                }
                Log.Info($"diplomacy: bluff {amount} roll {roll:0.00} chance {chance:0.00}");
                break;
            }
            case Offer.Deal:
            {
                float chance = Quotes.Deal(run, sim, callee, amount);
                int price = Quotes.DealPrice(run, amount);
                MarkHunterTalksUsed(run, callee);
                if (DiplomacyModel.Succeeds(roll, chance))
                {
                    Pay(run, player, price);
                    WaveLeaves(run, sim, caller);
                    Result(run, callee, DipText.Format("DealYesFmt", name, Events.EventText.Money(-price)));
                }
                else Result(run, callee, DipText.Format("DealNoFmt", name));
                Log.Info($"diplomacy: deal {amount} for {price} roll {roll:0.00} chance {chance:0.00} others {Situation.OtherHunters(run, sim, callee)}");
                break;
            }
            case Offer.Refuse:
                MarkHunterTalksUsed(run, callee);
                Result(run, callee, DipText.Format("RefuseFmt", name));
                break;
            case Offer.DemandSurrender:
            {
                float chance = Quotes.DemandSurrender(run, sim, player, callee);
                if (DiplomacyModel.Succeeds(roll, chance)) Surrendered(run, sim, callee, caller, rand.UInt64());
                else Result(run, callee, DipText.Format("SurrenderNoFmt", name));
                Log.Info($"diplomacy: demand surrender roll {roll:0.00} chance {chance:0.00}");
                break;
            }
            case Offer.AcceptYield:
                Surrendered(run, sim, callee, caller, rand.UInt64());
                break;
            case Offer.OfferSurrender:
            {
                float chance = Quotes.OfferSurrender(run, sim, player, callee);
                int price = Quotes.OfferPrice(run, sim, player, callee);
                if (DiplomacyModel.Succeeds(roll, chance))
                {
                    Pay(run, player, price);
                    PlayerSurrender(sim, caller, callee);
                    Result(run, callee, DipText.Format("OfferYesFmt", name, Events.EventText.Money(-price)));
                }
                else Result(run, callee, DipText.Format("OfferNoFmt", name));
                Log.Info($"diplomacy: offer surrender for {price} roll {roll:0.00} chance {chance:0.00}");
                break;
            }
            case Offer.Hold:
            {
                float chance = Quotes.Hold(run, sim, callee, amount);
                if (!DiplomacyModel.Succeeds(roll, chance)) { Result(run, callee, DipText.Format(Quotes.HunterNear(run, sim, callee) ? "HoldHuntersFmt" : "HoldNoFmt", name)); break; }
                if (rand.Single() < Quotes.Fee(run, callee))
                {
                    int fee = Quotes.FeePrice(run, amount);
                    S(run).FeeAsks.RemoveAll(f => f.Ship == callee.UniqueID);
                    S(run).FeeAsks.Add(new FeeAsk { Ship = callee.UniqueID, Player = player, Units = amount, Fee = fee });
                    Result(run, callee, DipText.Format("HoldFeeFmt", name, Events.EventText.Money(-fee), amount));
                }
                else DoHold(run, sim, player, callee, amount, feePaid: false, rand.Single());
                Log.Info($"diplomacy: hold {amount} roll {roll:0.00} chance {chance:0.00}");
                break;
            }
            case Offer.PayHoldFee:
            {
                var ask = S(run).FeeAsks.First(f => f.Ship == callee.UniqueID);
                S(run).FeeAsks.Remove(ask);
                Pay(run, player, ask.Fee);
                DoHold(run, sim, player, callee, Math.Min(ask.Units, Situation.VaultTech(sim)), feePaid: true, roll);
                break;
            }
            case Offer.DeclineHoldFee:
                S(run).FeeAsks.RemoveAll(f => f.Ship == callee.UniqueID);
                Result(run, callee, DipText.Format("HoldDeclinedFmt", name));
                break;
            case Offer.Return:
                Return(run, sim, player, callee);
                break;
            case Offer.Hire:
            {
                int credits = amount * Quotes.HireStep(run);
                float chance = Quotes.Hire(run, sim, player, callee, credits);
                if (DiplomacyModel.Succeeds(roll, chance))
                {
                    Pay(run, player, credits);
                    AddHire(run, callee, player, t.HireSeconds, merc: false);
                    Result(run, callee, DipText.Format("HireYesFmt", name, Minutes(t.HireSeconds)));
                }
                else Result(run, callee, DipText.Format("HireNoFmt", name));
                Log.Info($"diplomacy: hire for {credits} roll {roll:0.00} chance {chance:0.00}");
                break;
            }
            case Offer.Merc:
            {
                int price = Quotes.MercPrice(run, amount);
                Pay(run, player, price);
                AddHire(run, callee, player, amount * 60.0, merc: true);
                Result(run, callee, DipText.Format("MercYesFmt", name, amount, Events.EventText.Money(-price)));
                break;
            }
            default: throw new ArgumentOutOfRangeException(nameof(offer), offer, null);
        }
        return true;
    }

    static int Minutes(double seconds) => (int)Math.Ceiling(seconds / 60.0);

    /// <summary>Spec 593: heat falls by HandOverHeatDrop. Stages below the new heat start again as heat rises (Run.AddHeat).</summary>
    static void DropHeat(Run run, SimRoot sim)
    {
        float before = run.State.Heat;
        run.AddHeat(-run.T.Diplomacy.HandOverHeatDrop, sim);
        Log.Info($"diplomacy: heat {before:0.0} -> {run.State.Heat:0.0}");
    }

    /// <summary>
    /// The sim check of a punishment submit (spec 595): at least one option, money for money and bribe from the chooser, enough crew.
    /// The page greys Submit out with the same check.
    /// </summary>
    public static bool PunishAllowed(Run run, SimRoot sim, int player, IReadOnlyList<MultiChoice.Pick> picks, out string why)
    {
        why = "";
        if (picks.Count == 0) { why = "select an option"; return false; }
        int credits = picks.Sum(p => Quotes.PunishCredits(run, (PunishOption)p.Option, p.Value));
        if (!Situation.CanPay(run, player, credits)) { why = "money"; return false; }
        int crew = picks.Where(p => p.Option == (int)PunishOption.Crew).Sum(p => p.Value);
        if (crew > Quotes.PunishCrewAvailable(run, sim)) { why = "crew"; return false; }
        return true;
    }

    /// <summary>
    /// Spec 595 to 597. Submit rolls the combined chance of the draft. Success: the chooser pays the money and the bribe, the crew go to
    /// cryo jail, the wave leaves and heat drops. Failure, refusal or a timeout: the hunters keep the tech and the wave attacks; the
    /// players keep their money and crew. Returns false when the sim rejects the submit (the page stays).
    /// </summary>
    public static bool Punish(Run run, SimRoot sim, int player, Offer offer)
    {
        if (S(run).Punish is not { } punish) return false;
        var picks = MultiChoice.MultiChoices.Draft(run, DiplomacyMultiDomain.Name, punish.Serial);
        if (offer == Offer.PunishRefuse) { PunishFails(run, sim, punish, "PunishRefusedFmt"); return true; }
        if (offer != Offer.Punish) return false;
        if (!PunishAllowed(run, sim, player, picks, out string why)) { Log.Info($"diplomacy: punishment submit refused: {why}"); return false; }
        float chance = Quotes.PunishCombined(run, sim, picks);
        float roll = new FastRand(((ulong)(uint)punish.Serial << 32) | (uint)player, (ulong)(uint)sim.Tick).Single();
        Log.Info($"diplomacy: punishment [{string.Join(", ", picks.Select(p => $"{(PunishOption)p.Option}={p.Value}"))}] roll {roll:0.00} chance {chance:0.00}");
        if (!DiplomacyModel.Succeeds(roll, chance)) { PunishFails(run, sim, punish, "PunishNoFmt"); return true; }
        var lines = new List<string>();
        int credits = picks.Sum(p => Quotes.PunishCredits(run, (PunishOption)p.Option, p.Value));
        if (credits > 0) { Pay(run, player, credits); lines.Add(Events.EventText.Money(-credits)); }
        int crew = picks.Where(p => p.Option == (int)PunishOption.Crew).Sum(p => p.Value);
        if (crew > 0) lines.Add(DipText.Format("CryoFmt", Cryo(sim, player, crew)));
        S(run).Punish = null;
        MultiChoice.MultiChoices.Clear(run, DiplomacyMultiDomain.Name, punish.Serial);
        var anyShip = Vaults.HumanShips(sim).OrderBy(sh => sh.UniqueID).FirstOrDefault();
        if (anyShip is not null) WaveLeaves(run, sim, anyShip);
        DropHeat(run, sim);
        Result(run, punish.Hunter, DipText.Text("PunishYes") + (lines.Count > 0 ? "\n" + string.Join("\n", lines) : ""));
        return true;
    }

    /// <summary>The punishment fails: the tech stays with the hunters, and they fight on.</summary>
    internal static void PunishFails(Run run, SimRoot sim, Punishment punish, string key)
    {
        S(run).Punish = null;
        MultiChoice.MultiChoices.Clear(run, DiplomacyMultiDomain.Name, punish.Serial);
        foreach (var h in HunterSpawner.Hunters(sim)) MarkHunterTalksUsed(run, h);
        Result(run, punish.Hunter, DipText.Format(key, punish.Units));
        Log.Info($"diplomacy: punishment failed ({key}); {punish.Units} tech lost");
    }

    /// <summary>Sends crew to cryo jail: crew that carry nothing leave, the chooser's ships first. Returns the crew that left.</summary>
    static int Cryo(SimRoot sim, int player, int count)
    {
        var ships = Vaults.HumanShips(sim).OrderBy(sh => sh.Commands.CanExecuteCrewCommandsFrom(player) ? 0 : 1).ThenBy(sh => sh.UniqueID).ToList();
        int left = 0;
        foreach (var ship in ships)
        {
            var pool = ship.Crew.Assigned.Where(c => c.CarriedResource.Quantity == 0).OrderBy(c => c.UniqueID).ToList();
            // Each ship keeps one crew member, like PunishCrewAvailable counts.
            foreach (var soul in pool.Take(Math.Max(0, Math.Min(pool.Count, ship.Crew.Assigned.Count() - 1))))
            {
                if (left == count) return left;
                soul.Delete(killed: false);
                left++;
            }
        }
        return left;
    }

    /// <summary>A refused or failed talk with a hunter ends that hunter's demand page: no second try of any kind with it.</summary>
    static void MarkHunterTalksUsed(Run run, Ship hunter)
    {
        foreach (var o in new[] { Offer.HandOver, Offer.Bluff, Offer.Deal, Offer.Refuse })
            S(run).Used.Add(DiplomacyState.Key(hunter.UniqueID, o));
    }

    /// <summary>Money loss of the chooser. The input checked that the player can pay.</summary>
    static void Pay(Run run, int player, int credits)
    {
        if (credits > 0) run.Game.GetPlayerState(player, deterministic: true).AddMoney(-credits);
    }

    /// <summary>Spec 562: every corporate hunter in the system stops fighting and jumps out, and the groups that wait to arrive stay away.</summary>
    static void WaveLeaves(Run run, SimRoot sim, Ship playerShip)
    {
        foreach (var h in HunterSpawner.Hunters(sim).ToList()) Leave(run, sim, h, playerShip);
        int pending = run.State.Hunters.Pending.Count;
        run.State.Hunters.Pending.Clear();
        Log.Info($"diplomacy: the hunter wave leaves; {pending} waiting groups dropped");
    }

    static void Leave(Run run, SimRoot sim, Ship hunter, Ship playerShip)
    {
        if (S(run).Leaving.Any(l => l.Ship == hunter.UniqueID)) return;
        var career = Situation.Career(sim);
        double wait = run.T.Diplomacy.HunterLeaveSeconds;
        // AddTruce flips the player index of the ship, so it must run once per ship.
        if (career.GetTruce(hunter) is null) career.AddTruce(hunter, playerShip, Halfling.Timing.Time.FromSeconds(wait + 30));
        // A truce negates the ship's player index. Without the hunter tag, the Hunters area no longer counts the ship and new hunter
        // groups do not copy that index (HunterSpawner.HunterAllegiance). The ship takes its held fuel with it.
        HunterSpawner.ForgetHeldFuel(run, hunter);
        hunter.Tags?.Remove(HunterSpawner.HunterTag);
        S(run).Leaving.Add(new Leaving { Ship = hunter.UniqueID, Due = run.State.GameSeconds + wait });
    }

    static void Surrendered(Run run, SimRoot sim, Ship ai, Ship playerShip, ulong seed)
    {
        bool hunter = Situation.IsHunter(ai);
        int payout = Quotes.SurrenderPayout(run, sim, ai);
        var lines = Loot.Surrender(run, sim, ai, playerShip, payout, seed);
        // A surrendered hunter has no exit module and its truce runs out, so it jumps out instead.
        if (hunter) Leave(run, sim, ai, playerShip);
        Result(run, ai, DipText.Format("SurrenderYesFmt", ai.Metadata.XmlShipName) + (lines.Count > 0 ? "\n" + string.Join("\n", lines) : ""));
    }

    /// <summary>The player side of the vanilla surrender (MPShipSurrenderedInput, a player surrenders to an AI), after the price check passed.</summary>
    static void PlayerSurrender(SimRoot sim, Ship playerShip, Ship ai)
    {
        var career = Situation.Career(sim);
        career.SurrenderedToAIs.Add(ai.UniqueID);
        if (FactionRules.GetFaction(ai.Metadata.PlayerIndex) is { } faction) career.Wanted.RemoveFactionFromWanting(faction.ID);
        else
            foreach (var s in sim.Ships.Where(s => s.Metadata.Team == ai.Metadata.Team).ToList())
                career.AddTruce(s, playerShip, career.Rules.Dialogue.PlayerSurrenderDuration, ai.DetWorldCenter);
    }

    static void DoHold(Run run, SimRoot sim, int player, Ship ship, int units, bool feePaid, float betrayRoll)
    {
        int n = Situation.RemoveTech(sim, player, units);
        if (n <= 0) { Result(run, ship, DipText.Format("HoldNothingFmt", ship.Metadata.XmlShipName)); return; }
        S(run).Held.Add(new HeldTech
        {
            Ship = ship.UniqueID, ShipName = ship.Metadata.PlainShipName, Node = sim.NodeId(), Player = player, Units = n,
            FeePaid = feePaid, BetrayRoll = betrayRoll, Location = ship.DetWorldCenter,
        });
        Result(run, ship, DipText.Format("HoldYesFmt", ship.Metadata.XmlShipName, n));
        Log.Info($"diplomacy: {ship.Metadata.PlainShipName} holds {n} tech for player {player} (betray roll {betrayRoll:0.00})");
    }

    /// <summary>Spec 576: the betrayal chance at the return against the roll from the hand-over. Units that do not fit stay with the ship.</summary>
    static void Return(Run run, SimRoot sim, int player, Ship ship)
    {
        var records = S(run).Held.Where(h => h.Ship == ship.UniqueID && h.Player == player).ToList();
        if (records.Count == 0) return;
        var lines = new List<string>();
        foreach (var held in records)
        {
            float chance = Quotes.Betray(run, sim, ship, held);
            if (DiplomacyModel.Succeeds(held.BetrayRoll, chance))
            {
                S(run).Held.Remove(held);
                lines.Add(DipText.Format("BetrayFmt", ship.Metadata.XmlShipName, held.Units));
                Log.Info($"diplomacy: {ship.Metadata.PlainShipName} keeps {held.Units} tech (roll {held.BetrayRoll:0.00} < {chance:0.00})");
                continue;
            }
            int back = Vaults.Add(Situation.VaultsFor(sim, player), held.Units);
            held.Units -= back;
            if (held.Units == 0) S(run).Held.Remove(held);
            lines.Add(DipText.Format(held.Units > 0 ? "ReturnPartFmt" : "ReturnFmt", ship.Metadata.XmlShipName, back, held.Units));
            Log.Info($"diplomacy: {ship.Metadata.PlainShipName} returned {back} tech, keeps {held.Units} for lack of vault room");
        }
        Result(run, ship, string.Join("\n", lines));
    }

    static void AddHire(Run run, Ship ship, int player, double seconds, bool merc)
    {
        var hire = S(run).Hires.FirstOrDefault(h => h.Ship == ship.UniqueID);
        double now = run.State.GameSeconds;
        if (hire is null) S(run).Hires.Add(hire = new Hire { Ship = ship.UniqueID, Player = player, Until = now });
        hire.Until = Math.Max(hire.Until, now) + seconds;
        hire.Merc = merc;
        hire.Player = player;
        hire.Fleeing = false;
    }

    /// <summary>Stores the result of a ship's hail for its dialogue and writes it to the chat of every player.</summary>
    public static void Result(Run run, Ship ship, string xml) => Result(run, ship.UniqueID, xml);

    public static void Result(Run run, ObjectID ship, string xml)
    {
        if (xml.Length == 0) return;
        var s = S(run);
        s.Serial++;
        s.Results.RemoveAll(r => r.Ship == ship);
        s.Results.Add(new HailResult { Ship = ship, Serial = s.Serial, Text = xml });
        // Keeps the saved list short: results of ships that are long gone do not matter.
        if (s.Results.Count > 40) s.Results.RemoveAt(0);
        Ux.UxKit.ChatLine(run.Game, xml);
        Ui.CommsHail.Unlock(DiplomacyChoiceDomain.Name, unchecked((int)(uint)ship));
    }
}

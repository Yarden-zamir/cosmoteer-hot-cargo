using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Modes.Career;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI.StrategyModules;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.Pooling;
using Halfling.Random;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Tech;

namespace HotCargo.Diplomacy;

/// <summary>
/// Spec 569 to 571. A ship that surrenders to the players gives the vanilla payout, all its FTL fuel and, with a chance, part of one
/// other resource that it carries. Fuel that does not fit drops into space next to the receiving ship; the other resource gives only what fits.
/// Sim only: MP inputs.
/// </summary>
public static class Loot
{
    static ID<ResourceRules> Fuel => GameApp.Rules.FtlFuelResource;

    /// <summary>Human ships that take loot for a player: the receiving ship first, then the player's other ships, then the rest of the fleet.</summary>
    static List<Ship> Takers(SimRoot sim, Ship receiver) =>
        Vaults.HumanShips(sim)
            .OrderBy(s => s == receiver ? 0 : s.Metadata.PlayerIndex == receiver.Metadata.PlayerIndex ? 1 : 2).ThenBy(s => s.UniqueID)
            .ToList();

    /// <summary>Adds to the takers in order. Returns what did not fit.</summary>
    static int Store(SimRoot sim, Ship receiver, ID<ResourceRules> id, int amount)
    {
        int left = amount;
        foreach (var s in Takers(sim, receiver))
        {
            if (left == 0) break;
            left -= s.Resources.AddResources(id, left, excludeConstructionSinks: true);
        }
        return left;
    }

    /// <summary>Drops exactly <paramref name="amount"/> as loose nuggets beside the ship (no drop roll).</summary>
    static void Drop(SimRoot sim, Ship near, ID<ResourceRules> id, int amount)
    {
        if (amount <= 0) return;
        SimQueries.DropLoose(sim, near.DetWorldCenter, near.WorldBoundingRadius, id, amount);
    }

    /// <summary>Gives the fuel and the extra resource of a ship that surrendered. Returns the result lines (XML).</summary>
    public static List<string> Give(Run run, SimRoot sim, Ship from, Ship receiver, ulong seed)
    {
        var lines = new List<string>();
        int stored = from.Resources.GetResourceTotal(Fuel, includeStored: true, includeCarried: false);
        int taken = from.Resources.RemoveResources(Fuel, stored);
        int held = run.State.Hunters.HeldFuel.Where(h => h.Ship == from.UniqueID).Sum(h => h.Amount);
        HunterSpawner.ForgetHeldFuel(run, from);
        int fuel = taken + held;
        if (fuel > 0)
        {
            int dropped = Store(sim, receiver, Fuel, fuel);
            Drop(sim, receiver, Fuel, dropped);
            if (fuel > dropped) lines.Add(Events.EventText.Resource(Fuel, fuel - dropped));
            if (dropped > 0) lines.Add(DipText.Format("LootDroppedFmt", dropped));
        }
        var t = run.T.Diplomacy;
        var rand = new FastRand(seed, 0x4C4F_4F54UL);
        if (rand.Single() < t.ExtraLootChance)
        {
            var carried = GameApp.Rules.Resources
                .Where(r => r.ID != Fuel && r.ID != Vaults.TechId)
                .Select(r => (r.ID, Amount: from.Resources.GetResourceTotal(r.ID, includeStored: true, includeCarried: false)))
                .Where(x => x.Amount > 0)
                .OrderBy(x => x.ID.ToString(), StringComparer.Ordinal)
                .ToList();
            if (carried.Count > 0)
            {
                var (id, amount) = carried[rand.Int32(0, carried.Count - 1)];
                int want = Math.Max(1, (int)(amount * t.ExtraLootShare));
                int fits = want - StoreTest(sim, receiver, id, want);
                if (fits > 0)
                {
                    int removed = from.Resources.RemoveResources(id, fits);
                    int notStored = Store(sim, receiver, id, removed);
                    // Space checked a moment ago, so nothing should be left. Anything left goes back to the ship that gave it.
                    if (notStored > 0) from.Resources.AddResources(id, notStored, excludeConstructionSinks: true);
                    lines.Add(Events.EventText.Resource(id, removed - notStored));
                }
            }
        }
        Log.Info($"diplomacy: {from.Metadata.PlainShipName} gave fuel {fuel} to player {receiver.Metadata.PlayerIndex}; {string.Join(" | ", lines)}");
        return lines;
    }

    /// <summary>What would not fit of an amount, without storing it: adds and takes back at once.</summary>
    static int StoreTest(SimRoot sim, Ship receiver, ID<ResourceRules> id, int amount)
    {
        int left = amount;
        var added = new List<(Ship, int)>();
        foreach (var s in Takers(sim, receiver))
        {
            if (left == 0) break;
            int n = s.Resources.AddResources(id, left, excludeConstructionSinks: true);
            if (n > 0) added.Add((s, n));
            left -= n;
        }
        foreach (var (s, n) in added) s.Resources.RemoveResources(id, n);
        return left;
    }

    /// <summary>
    /// The AI side of the vanilla surrender (MPShipSurrenderedInput, AI surrenders to a player), for surrenders that the vanilla
    /// price check refuses: hunters, and the players' demand. Then the payout split and the loot. Returns the result lines.
    /// </summary>
    public static List<string> Surrender(Run run, SimRoot sim, Ship ai, Ship player, int payout, ulong seed)
    {
        var career = Situation.Career(sim);
        var module = ai.AI?.GetCommsModule<Cosmoteer.Ships.AI.CommsModules.AIDialogueModule>();
        if (module is not null) module.WouldConsiderSurrenderingToPlayer = false;
        if (Situation.Faction(ai) is null && ai.AI?.GetStrategyModule<AIExitSectorModule>() is { } exit) exit.IsActivated = true;
        career.SurrenderedAIs.Add(ai.UniqueID);
        career.AddTruce(ai, player, career.Rules.Dialogue.AISurrenderDuration);
        career.AddProtectionAgreement(ai, player, career.Rules.Dialogue.AISurrenderDuration);
        ai.Statuses.ClearOnShipJunked();
        career.AddInvincibleShip(ai, career.Rules.Dialogue.AIInvincibilityOnSurrenderDuration);
        var lines = new List<string>();
        if (payout > 0)
        {
            using var players = TempList<PlayerState>.Alloc();
            run.Game.GetHumanPlayerStates(players, deterministic: true);
            int each = payout / Math.Max(1, players.Count);
            foreach (var p in players) p.AddMoney(each);
            lines.Add(Events.EventText.Money(payout));
        }
        lines.AddRange(Give(run, sim, ai, player, seed));
        return lines;
    }

    /// <summary>Spec 570: a vanilla surrender (the AI's own offer in the vanilla dialogue) gives fuel and the extra resource too.</summary>
    [HarmonyPatch(typeof(MPShipSurrenderedInput), nameof(MPShipSurrenderedInput.Execute))]
    static class VanillaSurrenderPatch
    {
        static void Prefix(MPShipSurrenderedInput __instance, out bool __state)
        {
            __state = false;
            if (__instance.Surrenderer is { IsHumanPlayerShip: false } ai && ai.Sim?.Mode is CareerSimModeManager c)
                __state = !c.SurrenderedAIs.Contains(ai.UniqueID);
        }

        static void Postfix(MPShipSurrenderedInput __instance, bool __state)
        {
            if (!__state || Run.Current is not { } run || !run.T.Diplomacy.Enabled) return;
            if (__instance.Surrenderer is not { } ai || __instance.SurrenderingTo is not { } player || ai.Sim is not { } sim) return;
            if (!Situation.Career(sim).SurrenderedAIs.Contains(ai.UniqueID)) return;
            Errors.Guard("surrender loot", () =>
            {
                var lines = Give(run, sim, ai, player, ((ulong)(uint)ai.UniqueID << 32) ^ (uint)sim.Tick);
                Deals.Result(run, ai, string.Join("\n", lines));
            });
        }
    }
}

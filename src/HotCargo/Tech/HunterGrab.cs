using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using HotCargo.Core;
using HotCargo.Hunters;

namespace HotCargo.Tech;

/// <summary>Tech that a hunter took from a crate in space (spec 446). It is not the players' tech: it does not count in the fleet.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class HunterTech
{
    [Serialize] public ObjectID Ship;
    [Serialize] public string ShipName = "";
    /// <summary>The system of the hunter. An entry of another system waits until the players come back.</summary>
    [Serialize] public ObjectID Node;
    [Serialize] public int Units;
    /// <summary>Last known position of the hunter.</summary>
    [Serialize] public Vector2 Location;
}

/// <summary>Rules for the crate grab of hunters. Rules name: HcGrabTech.</summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcGrabTech")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class GrabTechRules : AIStrategyModuleRules
{
    /// <summary>A grab move is renewed only when the crate drifted further than this from the last move destination.</summary>
    [Serialize(Optional = true), PortableHash] public float MoveTolerance = 20f;

    public override AIStrategyModule CreateModule(AIParameters? parameters) => new GrabTech(this);
}

/// <summary>
/// Spec 446: a hunter flies to the nearest tech crate within HunterGrabRange that it wants. It wants each crate with chance
/// HunterGrabChance, rolled once for each hunter and crate from their IDs, so the choice holds and is the same on every client.
/// The module sits above the fight tactics in the hunter AI, so a hunter leaves a fight for a crate. Flee, call and fall back come first.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class GrabTech(GrabTechRules rules) : AIStrategyModule
{
    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        if (Run.Current is not { } run || ship.Sim is not { } sim || run.Game.Sim != sim) return false;
        if (HunterGrab.Target(run, sim, ship) is not { } crate) return false;
        HunterOrders.MoveTo(ship, crate.DetWorldLocation, rules.MoveTolerance);
        return true;
    }

    public override AIStrategyModule Clone(AIParameters? parameters) => new GrabTech(rules);
}

/// <summary>Spec 446: hunters take tech crates. The pickup and the held tech run in the sim tick.</summary>
public static class HunterGrab
{
    /// <summary>The crate that the hunter goes for, or null. Crates in nugget order, so ties pick the same crate on every client.</summary>
    internal static Nugget? Target(Run run, SimRoot sim, Ship ship)
    {
        var t = run.T.Tech;
        Nugget? best = null;
        float bestDistance = t.HunterGrabRange;
        for (int i = 0; i < sim.Nuggets.Count; i++)
        {
            var n = sim.Nuggets[i];
            if (n.ResourceType != Vaults.TechId || !Wants(ship, n, t.HunterGrabChance)) continue;
            float d = ship.DetWorldCenter.DistanceTo(n.DetWorldLocation);
            if (d < bestDistance) { best = n; bestDistance = d; }
        }
        return best;
    }

    static bool Wants(Ship ship, Nugget crate, float chance) =>
        new FastRand(((ulong)(uint)ship.UniqueID << 32) | (uint)crate.UniqueID, 0x4752_4142UL).Single() < chance;

    internal static IEnumerable<(Ship Ship, int Units)> Holders(SimRoot sim)
    {
        if (Run.Current is not { } run) yield break;
        foreach (var h in run.State.Tech.HunterHeld)
            if (sim.Ships[h.Ship] is { } ship) yield return (ship, h.Units);
    }

    internal static void OnTick(Run run, SimRoot sim, double dt)
    {
        PickUp(run, sim);
        TrackHeld(run, sim);
    }

    /// <summary>A live hunter whose hull reaches a crate takes it. Hunters in sim order, crates in nugget order.</summary>
    static void PickUp(Run run, SimRoot sim)
    {
        var hunters = HunterSpawner.Hunters(sim).ToList();
        if (hunters.Count == 0) return;
        float reach = run.T.Tech.HunterGrabReach;
        for (int i = sim.Nuggets.Count - 1; i >= 0; i--)
        {
            var n = sim.Nuggets[i];
            if (n.ResourceType != Vaults.TechId) continue;
            var hunter = hunters.FirstOrDefault(h => h.DetWorldBoundingCircle.DistanceTo(n.DetWorldLocation) <= reach);
            if (hunter is null) continue;
            int units = n.Quantity;
            sim.Nuggets.RemoveNugget(n);
            var held = run.State.Tech.HunterHeld;
            var entry = held.FirstOrDefault(h => h.Ship == hunter.UniqueID);
            if (entry is null)
                held.Add(entry = new HunterTech { Ship = hunter.UniqueID, ShipName = hunter.Metadata.PlainShipName, Node = sim.NodeId() });
            entry.Units += units;
            entry.Location = hunter.DetWorldCenter;
            Log.Info($"hunter {entry.ShipName} took {units} tech from a crate; it holds {entry.Units}");
            Tell(run, string.Format(Strings.GetText("HotCargoTech/HunterGrabbedFmt"), hunter.Metadata.XmlShipName, units), toast: true);
        }
    }

    /// <summary>
    /// Held tech follows its hunter. A hunter that becomes a wreck spills the tech as crates. A hunter that is gone from the system
    /// (it jumped out, or nothing is left of it) takes the tech. A hunter in stasis or in another system keeps it.
    /// Limit: a hunter destroyed to its last part in one tick, without a wreck first, takes the tech too. Revisit if players report it.
    /// </summary>
    static void TrackHeld(Run run, SimRoot sim)
    {
        var held = run.State.Tech.HunterHeld;
        if (held.Count == 0) return;
        var node = sim.NodeId();
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
                Drop(sim, ship.DetWorldCenter, h.Units);
                WreckSpills++;
                Log.Info($"hunter {h.ShipName} is a wreck; {h.Units} tech dropped");
                Tell(run, string.Format(Strings.GetText("HotCargoTech/HunterWreckFmt"), h.ShipName, h.Units), toast: false);
            }
            else
            {
                Log.Info($"hunter {h.ShipName} is gone with {h.Units} tech");
                Tell(run, string.Format(Strings.GetText("HotCargoTech/HunterGoneFmt"), h.ShipName, h.Units), toast: false);
            }
            held.RemoveAt(i--);
        }
    }

    /// <summary>Hunter wrecks that spilled held tech since start. For autotests.</summary>
    internal static int WreckSpills;

    internal static void Drop(SimRoot sim, Vector2 at, int units) =>
        sim.Nuggets.DropNuggets(Vaults.TechId, units, new RotRect(at, new Vector2(10f, 10f), Direction.Zero), Vector2.Zero, randomVelocity: true);

    /// <summary>A chat line, and a toast when asked. The sim tick can run on a worker thread, so both wait for the next frame.</summary>
    static void Tell(Run run, string text, bool toast) => Run.OnNextFrame(() =>
    {
        Ux.UxKit.ChatLine(run.Game, text);
        if (toast) run.Game.Gui.ShowAreaName(text);
    });
}

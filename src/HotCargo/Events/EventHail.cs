using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Network;
using Halfling.Random;
using Halfling.Serialization;
using HotCargo.Core;
using HotCargo.Ui;

namespace HotCargo.Events;

/// <summary>
/// Events in ship comms (spec 767-771). Ships carry event pages, and stations and faction ships offer Hot Cargo lines in their hails.
/// It plugs into the one comms hook, <see cref="CommsHail"/> (diplomacy owns it), as a provider. The page model, the choice input
/// (HotCargoEventChoice) and the effects stay the same; only where the page shows changes.
/// </summary>
public static class EventHail
{
    public static readonly ID<SimObjectSpawner> CarrierTag = new("hc_ev_carrier");
    static readonly ID<SimObjectSpawner> TradeStationTag = new("trade_station");
    static readonly ID<SimObjectSpawner> BuyerTag = new("hc_buyer");

    /// <summary>True when the pending page is carried by a ship. The event window does not show it.</summary>
    public static bool IsCarried(PendingEvent p) => p.Carrier is not null;

    /// <summary>Spawns the event's carrier near the fleet and returns its ID. The generator must tag one ship hc_ev_carrier.</summary>
    internal static ObjectID SpawnCarrier(SimRoot sim, EventRules e, ulong seed)
    {
        var at = SimQueries.FleetCenter(sim) ?? throw new InvalidOperationException("no human ships to spawn a carrier near");
        var before = SimQueries.TaggedShips(sim, CarrierTag).Select(t => t.Id).ToHashSet();
        EventLibrary.Spawn(e.Carrier).Generate(sim, new FastRand(seed, 0x4341_5252UL), loadInstantly: true, rootLoc: at, addExistingSpawnTags: true);
        var spawned = SimQueries.TaggedShips(sim, CarrierTag).Select(t => t.Id).Where(id => !before.Contains(id)).ToList();
        if (spawned.Count != 1) throw new InvalidDataException($"carrier spawn {e.Carrier} made {spawned.Count} ships tagged hc_ev_carrier, not 1");
        return spawned[0];
    }

    /// <summary>
    /// The carrier hails a human ship that could call it back (in comms range, it answers), like a vanilla AI hail
    /// (CareerSimModeManager.AIHailPlayer; the game drops a hail that the players cannot answer). Sim state: the hailer list.
    /// Returns false while no human ship is in range.
    /// </summary>
    internal static bool TryHail(SimRoot sim, ObjectID carrierId)
    {
        if (sim.Ships[carrierId] is not { } carrier) return false;
        var mode = (CareerSimModeManager)sim.Mode;
        var target = sim.Ships.HumanPlayerSignificantShips
            .Where(h => mode.CanShipCallShip(h, carrier) && mode.WillShipAnswerCall(h, carrier))
            .MinBy(h => h.DetWorldCenter.DistanceSquaredTo(carrier.DetWorldCenter));
        if (target is null) return false;
        mode.AIHailPlayer(carrier, target);
        Log.Info($"event carrier {carrier.Metadata.PlainShipName} hails {target.Metadata.PlainShipName}");
        return true;
    }

    /// <summary>True while the carrier exists in this system, live or in stasis.</summary>
    internal static bool CarrierAlive(SimRoot sim, ObjectID id) =>
        sim.Ships[id] is { IsJunk: false } || sim.Stasis.Any(s => s.ReservedObjectID == id);

    /// <summary>Which hail lines a ship offers: trade stations, or non-hostile faction ships.</summary>
    public static HailTarget? TargetOf(Ship callee, Ship caller)
    {
        if (callee.HasTag(TradeStationTag)) return callee.HasTag(BuyerTag) ? null : HailTarget.Station;
        if (FactionRules.GetFaction(callee.Metadata.PlayerIndex) is not null && !callee.IsEnemiesWith(caller) && Cosmoteer.Ships.Special.SpaceStation.GetStation(callee, onlyOperating: false) is null)
            return HailTarget.FactionShip;
        return null;
    }

    static string UseKey(string eventId, ObjectID ship) => $"{eventId}@{ship}";

    /// <summary>Hail events that this ship can offer now. Sim state only, so every machine gives the same answer.</summary>
    public static List<EventRules> Lines(Run run, SimRoot sim, Ship caller, Ship callee)
    {
        var s = run.State.Events;
        if (EventLibrary.LoadError is not null || s.Pending is not null || run.State.Result != RunResult.Running) return [];
        // The contact of an active hand-over shows its own page's line until the hand-over ends, so a "not yet" can come back (spec 1102).
        // A mission step, so it works with random events turned off.
        var handOver = HandOverObjective.ForContact(run, callee.UniqueID);
        if (handOver is null && !run.T.Events.Enabled) return [];
        // With an item, the line needs a unit in any of its forms: sealed or opened (spec 1117).
        if (handOver?.ResourceType is { } item && HandOverObjective.FleetUnits(sim, HandOverObjective.Forms(item)) == 0) return [];
        HailTarget? target = handOver is not null ? HailTarget.Contact : TargetOf(callee, caller);
        if (target is null) return [];
        var snap = EventRunner.Snap(run, sim, run.Game.GetPlayerState(caller.Metadata.PlayerIndex, deterministic: true).Money, run.CurrentTier, EventRunner.CurrentTechValue(run));
        return EventLibrary.InOrder
            .Where(e => e.Trigger == EventTrigger.Hail && e.HailTarget == target)
            .Where(e => handOver is not null ? e.ID == handOver.Page : !s.HailUsed.Contains(UseKey(e.ID, callee.UniqueID)))
            .Where(e => EventRunner.Eligible(run, e, snap))
            .ToList();
    }

    /// <summary>Opens a hail line's page on the ship. Runs from the hail MP input on every machine.</summary>
    internal static void OpenLine(Run run, SimRoot sim, int playerIndex, string eventId, ObjectID shipId)
    {
        if (sim.Ships[shipId] is not { } callee) return;
        var caller = sim.Ships.HumanPlayerSignificantShips.Where(sh => sh.Metadata.PlayerIndex == playerIndex)
            .MinBy(sh => sh.DetWorldCenter.DistanceSquaredTo(callee.DetWorldCenter));
        if (caller is null) return;
        var e = Lines(run, sim, caller, callee).FirstOrDefault(x => x.ID == eventId);
        if (e is null) { Log.Info($"hail line {eventId} ignored: not offered by {callee.Metadata.PlainShipName} now"); return; }
        if (e.HailTarget != HailTarget.Contact) run.State.Events.HailUsed.Add(UseKey(e.ID, shipId));
        EventRunner.Open(run, sim, e, lead: "", carrier: shipId);
    }
}

/// <summary>
/// The comms side (UI): a ship that carries the pending page shows the page and its choices; a station or faction ship shows its
/// Hot Cargo lines. Page choices are shared choices (HailPage.AddChoice); lines send the hail MP input. One page serial for each opened page, so two players on the same ship do not double-apply.
/// </summary>
public sealed class EventHailProvider : IHailProvider
{
    public bool Answers(Ship caller, Ship callee)
    {
        if (Run.Current is not { } run || caller.Sim is not { } sim || sim != run.Game.Sim) return false;
        if (run.State.Events.Pending is { Carrier: { } carrier }) return carrier == callee.UniqueID;
        return EventHail.Lines(run, sim, caller, callee).Count > 0;
    }

    public void Build(Ship caller, Ship callee, HailPage page)
    {
        if (Run.Current is not { } run || caller.Sim is not { } sim) return;
        if (run.State.Events.Pending is { Carrier: { } carrier } p && carrier == callee.UniqueID)
        {
            var view = EventView.Pending(run, sim, p, run.Game.LocalPlayerIndex);
            page.AddText($"<b>{view.Title}</b>\n{view.Body}");
            // Shared choices: the comms tab shows co-op hovers and votes, and sends the shared choice input with this page's serial.
            page.Shared = (EventsChoiceDomain.Name, p.Serial);
            // Spec 810: the page's options as toggle rows of the shared draft, above the choices.
            if (view.Options.Count > 0) page.Multi = (EventsMultiDomain.Name, p.Serial);
            foreach (var o in view.Options)
                page.AddRow(new HailRow("ev" + o.Id, o.Text, o.Id, o.Slider, o.Active, o.Tooltip, Toggle: true, Selected: o.Selected, Value: o.Value));
            foreach (var c in view.Choices) page.AddChoice(c.Text, c.Index, active: c.Enabled, submit: c.Submit);
            return;
        }
        foreach (var e in EventHail.Lines(run, sim, caller, callee))
        {
            string id = e.ID;
            var shipId = callee.UniqueID;
            page.AddButton(e.HailLine, () => run.Game.NetManager.EnqueueInput(new EventHailInput(id, shipId)));
        }
    }
}

[HarmonyLib.HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.AllowMPInput))]
static class AllowEventHailPatch
{
    static void Postfix(MPInput mpInput, ref bool __result)
    {
        if (mpInput is EventHailInput) __result = true;
    }
}

/// <summary>A player clicks a Hot Cargo line in a ship's comms (spec 769). Opens the page on that ship in the sim, on every machine.</summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoEventHail")]
internal sealed class EventHailInput : MPInput
{
    [Serialize] public string EventId { get; private set; }
    [Serialize] public ObjectID ShipId { get; private set; }

    public EventHailInput(string eventId, ObjectID shipId)
    {
        EventId = eventId;
        ShipId = shipId;
    }

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false)
    {
        if (Run.Current is not { } run || run.Game != game || game.Sim is not { } sim) return;
        Errors.Guard("event hail", () => EventHail.OpenLine(run, sim, playerIndex, EventId, ShipId));
    }
}

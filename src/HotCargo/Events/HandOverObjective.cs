using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HotCargo.Core;

namespace HotCargo.Events;

/// <summary>
/// Rules type "HotCargoHandOver" (spec 1100 to 1103, 1107): an exchange with the Contact ship in its comms. Page is the Hail event
/// (HailTarget = Contact) that the contact shows while this objective is active; its outcome with HandOver = true calls <see cref="Deliver"/>.
/// With a ResourceType, the players carry one unit and hand it over. GiveOnAccept: the accepting player's ship gets it on accept
/// (<see cref="MissionCargo"/>). Otherwise the players first recover it (salvage, a pickup); the step reads "Recover" until the fleet holds it.
/// Once held, the objective fails when no unit is left in the players' ships or loose in the system. Without a ResourceType, LineKey names the exchange.
/// Fails when the contact dies.
/// Limit: the lost check counts units of the resource, not units of this mission. Two open jobs with the same item fail only when every
/// unit is gone. Revisit when a second mission gives out the same item.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoHandOver")]
internal sealed class HandOverObjective : Objective, Missions.IMissionMarkers
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoHandOver")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        [Serialize(Optional = true), PortableHash] public ID<ResourceRules>? ResourceType;
        /// <summary>The accept loads the unit. False: the players must recover it first (spec 1107).</summary>
        [Serialize(Optional = true), PortableHash] public bool GiveOnAccept = true;
        /// <summary>
        /// The ship that holds the unit to recover. The first time it is live, it gets exactly one unit (a rules ResourceLoadout
        /// fills every storage tile, so it cannot give exactly one). Salvage or capture it to get the unit out.
        /// </summary>
        [Serialize(Optional = true), PortableHash] public SpawnedObjectSearch? SourceShipTag;
        /// <summary>A ship that the players deliver to the contact, which takes it (spec 1110). A choice with Need { HandOverReady } waits for it.</summary>
        [Serialize(Optional = true), PortableHash] public SpawnedObjectSearch? ShipTag;
        /// <summary>Hull-to-hull gap between the delivered ship and the contact.</summary>
        [Serialize(Optional = true), PortableHash] public float DeliveryRadius = 800f;
        [Serialize, PortableHash] public SpawnedObjectSearch ContactShipTag = null!;
        /// <summary>The Hail event (events.rules) that the contact's comms show for this exchange.</summary>
        [Serialize, PortableHash] public string Page = "";
        /// <summary>Objective line, a string key with {0} = the contact. Needed without a ResourceType.</summary>
        [Serialize(Optional = true), PortableHash] public string? LineKey;
        /// <summary>A POI area around the contact. The map marks it until a player ship has the contact on radar, like vanilla GotoShip.</summary>
        [Serialize(Optional = true), PortableHash] public SpawnedObjectSearch? ContactAreaTag;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            if (Page.Length == 0) throw new InvalidDataException("a HotCargoHandOver needs a Page (a Hail event with HailTarget = Contact)");
            if (ResourceType is null && LineKey is null) throw new InvalidDataException("a HotCargoHandOver without a ResourceType needs a LineKey");
            invalid = !objects.TryFindObject(ContactShipTag, loc, rand, out var contact);
            if (invalid) return null;
            ObjectID id = contact!.UniqueID ?? throw new InvalidOperationException($"contact tag {ContactShipTag} found an object without an ID");
            ObjectiveTargetShip? source = null;
            if (SourceShipTag is not null)
            {
                if (ResourceType is null || GiveOnAccept) throw new InvalidDataException("a SourceShipTag needs a ResourceType and GiveOnAccept = false");
                var found = objects.FindObject(SourceShipTag, loc, rand);
                source = new ObjectiveTargetShip(sim.RequireGame(), sim.NodeId(), found.UniqueID ?? throw new InvalidOperationException($"source tag {SourceShipTag} found an object without an ID"));
            }
            return new HandOverObjective(ResourceType, new ObjectiveTargetShip(sim.RequireGame(), sim.NodeId(), id), Page)
            {
                LineKey = LineKey,
                GiveOnAccept = GiveOnAccept,
                Source = source,
                DeliveredShipId = ShipTag is null ? null : objects.FindObject(ShipTag, loc, rand).UniqueID
                    ?? throw new InvalidOperationException($"ship tag {ShipTag} found an object without an ID"),
                DeliveryRadius = DeliveryRadius,
                AreaDoodadID = ContactAreaTag is null ? null : objects.FindObject(ContactAreaTag, loc, rand).UniqueID,
                Tier = Math.Max(1, ((Cosmoteer.Modes.Career.CareerSimModeManager)sim.Mode).TierFields.GetTier(contact.Location.Center)),
            };
        }
    }

    [Serialize(Optional = true)] public ID<ResourceRules>? ResourceType { get; private set; }
    [Serialize] public ObjectiveTargetShip Contact { get; private set; }
    [Serialize] public string Page { get; private set; }
    [Serialize(Optional = true)] public string? LineKey { get; private set; }
    [Serialize(Optional = true)] public bool GiveOnAccept { get; private set; } = true;
    /// <summary>The fleet held the unit at least once (the accept loaded it, or the players recovered it). The lost check starts here.</summary>
    [Serialize(Optional = true)] public bool Held { get; private set; }
    [Serialize(Optional = true)] public ObjectiveTargetShip? Source { get; private set; }
    /// <summary>The source ship got its one unit.</summary>
    [Serialize(Optional = true)] public bool Seeded { get; private set; }
    [Serialize(Optional = true)] public ObjectID? DeliveredShipId { get; private set; }
    [Serialize(Optional = true)] public float DeliveryRadius { get; private set; } = 800f;

    /// <summary>
    /// Why the delivered ship cannot be handed over yet, or null when it can (or there is none). Sim state only, so every machine agrees.
    /// </summary>
    internal string? Blocker(SimRoot sim)
    {
        if (DeliveredShipId is not { } id) return null;
        if (sim.Ships[id] is not { } ship) return Strings.GetText("HotCargoEventMissions/DeliverGone");
        string name = ship.Metadata.PlainShipName;
        if (Contact.TargetShip is not { } contact) return Strings.FormatText("HotCargoEventMissions/DeliverFarFmt", name, DeliveryRadius);
        Circle a = ship.DetWorldBoundingCircle, b = contact.DetWorldBoundingCircle;
        if (a.Center.DistanceTo(b.Center) - a.Radius - b.Radius > DeliveryRadius) return Strings.FormatText("HotCargoEventMissions/DeliverFarFmt", name, DeliveryRadius);
        if (PlayerCrewAboard(sim, ship)) return Strings.FormatText("HotCargoEventMissions/DeliverCrewFmt", name);
        return null;
    }

    /// <summary>Human crew whose home is the ship, or who stand on it now.</summary>
    static bool PlayerCrewAboard(SimRoot sim, Ship ship) =>
        (sim.Mode.IsHumanPlayerShip(ship) && ship.Crew.Assigned.Count > 0)
        || Tech.Vaults.HumanShips(sim).SelectMany(s => s.Crew.Assigned).Any(c => c.CurrentShip == ship);

    /// <summary>A surrendered delivered ship stays in the system and flies to the contact (spec 1110). Sim tick.</summary>
    internal void SteerSurrendered(SimRoot sim)
    {
        if (DeliveredShipId is not { } id || sim.Ships[id] is not { } ship || Contact.Location is not { } at) return;
        if (!((Cosmoteer.Modes.Career.CareerSimModeManager)sim.Mode).SurrenderedAIs.Contains(id)) return;
        if (ship.AI is not { Parameters: { } parameters } ai || parameters.PatrolOrigin is { } origin && origin.DistanceTo(at) < 100f) return;
        var p = parameters.Clone();
        p.PatrolOrigin = at;
        ai.Parameters = p;
        Log.Info($"hand-over: surrendered {ship.Metadata.PlainShipName} flies to its contact");
    }

    /// <summary>True for the delivered ship of any open hand-over: vanilla must not fly it out of the system (spec 1110).</summary>
    internal static bool IsHeld(Run run, ObjectID shipId)
    {
        var found = new List<HandOverObjective>();
        foreach (Mission m in run.Mode.Missions) m.GetObjectives(found);
        return found.Any(o => o.Completion == CompletionState.InProgress && o.DeliveredShipId == shipId);
    }

    /// <summary>Puts the one unit into the source ship once it is live. Sim tick, for every open hand-over (its turn or not).</summary>
    internal void SeedSource(SimRoot sim)
    {
        // By ID in this sim: the source is junk, and an objective that is not running yet does not track its target.
        if (Seeded || Source is null || sim.NodeId() != Source.MapNodeID || sim.Ships[Source.ShipID] is not { } ship || ResourceType is not { } item) return;
        if (ship.Resources.AddResources(item, 1, excludeConstructionSinks: true) != 1) throw new InvalidOperationException($"{ship.Metadata.PlainShipName} has no storage for {item}");
        Seeded = true;
        Log.Info($"hand-over: {item} placed in {ship.Metadata.PlainShipName}");
    }
    [Serialize(Optional = true)] public int Tier { get; private set; } = 1;
    [Serialize(Optional = true)] public ObjectID? AreaDoodadID { get; private set; }

    public override string ScriptTypeName => "HotCargoHandOver";

    public HandOverObjective(ID<ResourceRules>? resourceType, ObjectiveTargetShip contact, string page)
    {
        ResourceType = resourceType;
        Contact = contact ?? throw new ArgumentNullException(nameof(contact));
        Page = page.Length > 0 ? page : throw new ArgumentException("needs a page", nameof(page));
    }

    [GenericConstructor]
    HandOverObjective(GenericSerialReader reader)
    {
        Contact = null!;
        Page = "";
        reader.ReflectiveRead(this);
    }

    /// <summary>The active hand-over whose contact is this ship, or null.</summary>
    public static HandOverObjective? ForContact(Run run, ObjectID shipId) => Open(run).FirstOrDefault(o => o.Contact.ShipID == shipId);

    public static IEnumerable<HandOverObjective> Open(Run run)
    {
        var found = new List<HandOverObjective>();
        foreach (Mission m in run.Mode.Missions) m.GetObjectives(found);
        // Its turn: an exchange after other steps (kill the escorts first) shows its page only when the sequence reaches it.
        return found.Where(o => o.Completion == CompletionState.InProgress && o._registered);
    }

    /// <summary>The page outcome of a hand-over (HandOver = true) on this contact. The page cost already took the unit. Sim logic.</summary>
    public static void Deliver(Run run, ObjectID? contact)
    {
        if (contact is not { } id || ForContact(run, id) is not { } o)
            throw new InvalidOperationException($"a HandOver outcome ran on ship {contact?.ToString() ?? "none"}, which is no open hand-over contact");
        Log.Info($"hand-over: {o.Page} done with {o.Contact.GetPlainTargetName(run.Game.Sim)}");
        if (o.DeliveredShipId is { } shipId && run.Game.Sim is { } sim)
        {
            if (o.Blocker(sim) is { } why) throw new InvalidOperationException($"hand-over {o.Page} ran while blocked: {why}");
            var ship = sim.Ships[shipId] ?? throw new InvalidOperationException("the delivered ship is gone");
            Log.Info($"hand-over: {o.Contact.GetPlainTargetName(sim)} takes {ship.Metadata.PlainShipName}");
            sim.Ships.Remove(ship, dispose: true, handleLostShip: false);
        }
        o.Complete();
    }

    /// <summary>Units in the players' ships, stored or carried by crew.</summary>
    public static int FleetUnits(SimRoot sim, ID<ResourceRules> resource) =>
        Tech.Vaults.HumanShips(sim).Sum(s => s.Resources.GetResourceTotal(resource, includeStored: true, includeCarried: true, includeOffShipCarried: true));

    /// <summary>Units in the players' ships and loose in this system.</summary>
    public static int UnitsLeft(SimRoot sim, ID<ResourceRules> resource) =>
        FleetUnits(sim, resource) + sim.Nuggets.Where(n => n.ResourceType == resource).Sum(n => n.Quantity);

    internal void MarkHeld() => Held = true;

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        ResourceType?.GetIntegrityHash(iHash);
        Contact.GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => Contact.MapNode;

    /// <summary>The contact when it is on radar, else its area POI once explored. Same rule as vanilla GotoShipObjective.GetAreaLocation.</summary>
    public override Circle? GetAreaLocation(GameRoot game)
    {
        if (Completion != CompletionState.InProgress) return null;
        if (Contact.TargetShip is { IsWithinRadarOfLocalPlayer: true } ship) return ship.WorldBoundingCircle;
        return Area(game) is { IsExplored: true } poi ? new Circle(poi.WorldLocation, poi.WorldUniformScale) : null;
    }

    /// <summary>Spec 996: the contact's area marker goes with the mission.</summary>
    public IEnumerable<ObjectID> MarkerIds => AreaDoodadID is { } id ? [id] : [];

    POIDoodad? Area(GameRoot game) => AreaDoodadID is { } id ? game.Sim?.Doodads[id] as POIDoodad : null;

    public override bool IsPOIAssociated(POIDoodad poi) =>
        Completion == CompletionState.InProgress && poi.UniqueID == AreaDoodadID && Contact.TargetShip is not { IsWithinRadarOfLocalPlayer: true };

    public override bool IsShipAssociated(MetaShip ship) => Completion == CompletionState.InProgress && Contact.TargetMetaShip == ship;

    /// <summary>
    /// Only a running step expires; an offer stays even when its contact died, and fails at once when accepted (RegisterEvents).
    /// Limit: an offer that expired would keep its map marker, because marker cleanup (MissionMarkers) runs when a mission leaves the
    /// log, not when an offer expires. Revisit when MissionMarkers also cleans up expired offers.
    /// </summary>
    public override bool CheckExpired(GameRoot game) => Completion == CompletionState.Failed || _registered && game.Sim is { } sim && ContactGone(sim);

    /// <summary>Gone: not in this system's ships or stasis. By ID, because ObjectiveTargetShip tracks nothing before the step runs.</summary>
    bool ContactGone(SimRoot sim) =>
        sim.NodeId() == Contact.MapNodeID && sim.Ships[Contact.ShipID] is null && !sim.Stasis.Any(st => st.ReservedObjectID == Contact.ShipID);

    public override bool CanFocusDisplayLine(GameRoot game) => Contact.CanFocus();

    public override void OnDisplayLineFocused(GameRoot game) => Contact.OnFocused();

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState)
    {
        string contact = Contact.GetXmlTargetName(game.Sim);
        var text = ResourceType?.GetResourceRules() is { } r
            ? Held ? Strings.KeyStringFmt("HotCargoEventMissions/HandOverFmt", r.TextIconXml, r.NameKey, contact)
                : Strings.KeyStringFmt("HotCargoEventMissions/RecoverFmt", r.TextIconXml, r.NameKey)
            : Strings.KeyStringFmt(LineKey ?? throw new InvalidOperationException("a hand-over without an item has no LineKey"), contact);
        lines.Add(new DisplayLine
        {
            Text = text,
            State = Completion,
            CanFocus = CanFocusDisplayLine,
            OnFocused = OnDisplayLineFocused,
        });
    }

    /// <summary>
    /// True while the mission runs this step. A Sequential parent registers a step only when it reaches it, on every machine,
    /// so this is the same everywhere. Vanilla IsActive does not follow the sequence.
    /// </summary>
    bool _registered;

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        _registered = true;
        Log.Info($"hand-over: step {Page} starts (loading: {isLoading})");
        Contact.Killed += OnContactKilled;
        game.EventHub.Tick += OnTick;
        if (!isLoading && game.Sim is { } sim && ContactGone(sim)) OnContactKilled(null, EventArgs.Empty);
    }

    public override void UnregisterEvents(GameRoot game)
    {
        _registered = false;
        Contact.Killed -= OnContactKilled;
        game.EventHub.Tick -= OnTick;
    }

    void OnContactKilled(object? sender, EventArgs e)
    {
        if (Completion == CompletionState.InProgress) Fail();
    }

    void OnTick(object? sender, SimRoot sim) => Errors.Guard("hand-over tick", () =>
    {
        if (Mission?.Game is not { } game || Completion != CompletionState.InProgress || sim != game.Sim) return;
        if (DeliveredShipId is { } shipId && sim.NodeId() == Contact.MapNodeID && sim.Ships[shipId] is null && !sim.Stasis.Any(st => st.ReservedObjectID == shipId))
        {
            Log.Info("hand-over: the ship to deliver is gone; the job fails");
            Fail();
            return;
        }
        if (ResourceType is not { } item) return;
        if (!Held)
        {
            if (FleetUnits(sim, item) == 0)
            {
                // The source is gone (destroyed, or salvaged with the unit destroyed) and no unit drifts loose: it is lost.
                bool sourceGone = Source is { } src && sim.NodeId() == src.MapNodeID && sim.Ships[src.ShipID] is null && !sim.Stasis.Any(st => st.ReservedObjectID == src.ShipID);
                if (!sourceGone || UnitsLeft(sim, item) > 0) return;
                Log.Info($"hand-over: the source of {item} is gone and no unit is loose; the job fails");
                string lost = item.GetResourceRules().NameKey.Text;
                Fail();
                Ux.UxKit.CenterText(game, Strings.FormatText("HotCargoEventMissions/HandOverLostFmt", lost));
                return;
            }
            Held = true;
            Log.Info($"hand-over: the fleet recovered {item}");
            OnDisplayLinesChanged();
            return;
        }
        if (UnitsLeft(sim, item) > 0) return;
        Log.Info($"hand-over: no {item} left in the fleet or the system; the job fails");
        // Fail() can take the mission out of the log at once, so read everything first.
        string name = item.GetResourceRules().NameKey.Text;
        Fail();
        // UI only: tell the players why.
        Ux.UxKit.CenterText(game, Strings.FormatText("HotCargoEventMissions/HandOverLostFmt", name));
    });

    public override int GetTypeIntegrityHash() => 0x4843_484F; // "HCHO"
}

/// <summary>
/// Items that a mission gives on accept (spec 1101): one unit of each hand-over's resource goes to the accepting player's ship with room.
/// The contracts accept gate refuses the accept when no ship has room. A job that leaves the log without the hand-over takes its unit back.
/// </summary>
public static class MissionCargo
{
    static List<HandOverObjective> Given(Mission m)
    {
        var found = new List<HandOverObjective>();
        m.GetObjectives(found);
        return found.Where(o => o.ResourceType is not null && o.GiveOnAccept).ToList();
    }

    static IEnumerable<ID<ResourceRules>> Items(Mission m) => Given(m).Select(o => o.ResourceType!.Value);

    public static bool HasItems(Mission m) => Items(m).Any();

    /// <summary>The first ship of this player, by ID, that can take one unit. Deterministic order.</summary>
    static Ship? ShipWithRoom(SimRoot sim, int playerIndex, ID<ResourceRules> resource) =>
        Tech.Vaults.HumanShips(sim).Where(s => s.Metadata.PlayerIndex == playerIndex)
            .OrderBy(s => s.UniqueID)
            .FirstOrDefault(s => s.Resources.GetBestReceivingSink(resource, 1, MPValueType.Confirmed, out _, includeAnticipatedDeliveries: false, externalDelivery: false,
                excludeDeconstructionQueued: false, ignoreAllowManualReceiveToggles: true, excludeConstructionSinks: true) is not null);

    /// <summary>The first item that no ship of this player has room for, or null when all fit.</summary>
    public static ID<ResourceRules>? NoRoomFor(SimRoot sim, Mission m, int playerIndex) =>
        Items(m).Select(r => (ID<ResourceRules>?)r).FirstOrDefault(r => ShipWithRoom(sim, playerIndex, r!.Value) is null);

    /// <summary>Runs inside the accept input after the mission reached the log. Sim logic.</summary>
    public static void Load(SimRoot sim, Mission m, int playerIndex)
    {
        foreach (var r in Items(m))
        {
            var ship = ShipWithRoom(sim, playerIndex, r) ?? throw new InvalidOperationException($"no room for {r} after the accept check");
            if (ship.Resources.AddResources(r, 1, excludeConstructionSinks: true) != 1) throw new InvalidOperationException($"{ship.Metadata.PlainShipName} did not take {r}");
            Log.Info($"mission cargo: {r} loaded into {ship.Metadata.PlainShipName} for {m.MetatypeID} {m.MissionID}");
        }
        foreach (var o in Given(m)) o.MarkHeld();
        Watch(m.Game);
    }

    /// <summary>Sim tick (Run.Hooks): watches the mission log and fills source ships of open hand-overs.</summary>
    internal static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("mission cargo tick", () =>
    {
        if (sim != run.Game.Sim) return;
        Watch(run.Game);
        var found = new List<HandOverObjective>();
        foreach (Mission m in run.Mode.Missions) m.GetObjectives(found);
        foreach (var o in found.Where(o => o.Completion == CompletionState.InProgress))
        {
            if (o.Source is not null && !o.Seeded) o.SeedSource(sim);
            o.SteerSurrendered(sim);
        }
    });

    static MissionList? _watched;

    /// <summary>Subscribes to the mission log of the current game. A new or loaded game has a new log.</summary>
    internal static void Watch(GameRoot game)
    {
        if (Run.Current is not { } run || run.Game != game || _watched == run.Mode.Missions) return;
        if (_watched is not null) _watched.MissionRemoved -= OnMissionRemoved;
        _watched = run.Mode.Missions;
        _watched.MissionRemoved += OnMissionRemoved;
    }

    /// <summary>A job without its hand-over (failed or abandoned) takes its unit back, so dead cargo does not block a storage slot.</summary>
    static void OnMissionRemoved(object? sender, Mission m) => Errors.Guard("mission cargo", () =>
    {
        if (Run.Current is not { } run || !ReferenceEquals(sender, run.Mode.Missions) || run.Game.Sim is not { } sim) return;
        var open = new List<HandOverObjective>();
        m.GetObjectives(open);
        foreach (var o in open.Where(o => o.Completion != CompletionState.Completed))
        {
            if (o.ResourceType is not { } item || !o.Held) continue;
            int removed = Tech.Vaults.HumanShips(sim).OrderBy(s => s.UniqueID).Select(s => s.Resources.RemoveResources(item, 1)).FirstOrDefault(n => n > 0);
            Log.Info($"mission cargo: {m.MetatypeID} {m.MissionID} left the log without the hand-over; removed {removed} {item}");
        }
    });
}

/// <summary>Spec 1110: a surrendered ship that a running hand-over must deliver does not leave the system until the mission ends.</summary>
[HarmonyLib.HarmonyPatch(typeof(Cosmoteer.Ships.AI.StrategyModules.AIExitSectorModule), nameof(Cosmoteer.Ships.AI.StrategyModules.AIExitSectorModule.OnAITick))]
static class HeldShipExitPatch
{
    static bool Prefix(Cosmoteer.Ships.AI.StrategyModules.AIExitSectorModule __instance, Ship ship, ref bool __result)
    {
        // Cheap check first: this runs on every AI tick of every ship with the module.
        if (!__instance.IsActivated) return true;
        bool held = false;
        Errors.Guard("held ship exit", () => held = Run.Current is { } run && ship.Sim == run.Game.Sim && HandOverObjective.IsHeld(run, ship.UniqueID));
        if (!held) return true;
        __result = false;
        return false;
    }
}

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
/// Rules type "HotCargoHandOver" (spec 1100 to 1105): the players carry one unit of Resource and hand it to the Contact ship.
/// The accepting player's ship gets the unit when the mission is accepted (<see cref="MissionCargo"/>). The hand-over is a comms page on
/// the contact (a Hail event with HailTarget = Contact); its outcome with HandOver = true calls <see cref="Deliver"/>.
/// Fails when the contact dies, or when no unit is left in the players' ships or loose in the system.
/// Limit: the lost check counts units of the resource, not units of this mission. Two open jobs with the same item fail only when every
/// unit is gone. Revisit when a second mission gives out the same item.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoHandOver")]
internal sealed class HandOverObjective : Objective
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoHandOver")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        [Serialize, PortableHash] public ID<ResourceRules> ResourceType;
        [Serialize, PortableHash] public SpawnedObjectSearch ContactShipTag = null!;
        /// <summary>A POI area around the contact. The map marks it until a player ship has the contact on radar, like vanilla GotoShip.</summary>
        [Serialize(Optional = true), PortableHash] public SpawnedObjectSearch? ContactAreaTag;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            invalid = !objects.TryFindObject(ContactShipTag, loc, rand, out var contact);
            if (invalid) return null;
            ObjectID id = contact!.UniqueID ?? throw new InvalidOperationException($"contact tag {ContactShipTag} found an object without an ID");
            return new HandOverObjective(ResourceType, new ObjectiveTargetShip(sim.RequireGame(), sim.NodeId(), id))
            {
                AreaDoodadID = ContactAreaTag is null ? null : objects.FindObject(ContactAreaTag, loc, rand).UniqueID,
                Tier = Math.Max(1, ((Cosmoteer.Modes.Career.CareerSimModeManager)sim.Mode).TierFields.GetTier(contact.Location.Center)),
            };
        }
    }

    [Serialize] public ID<ResourceRules> ResourceType { get; private set; }
    [Serialize] public ObjectiveTargetShip Contact { get; private set; }
    [Serialize(Optional = true)] public int Tier { get; private set; } = 1;
    [Serialize(Optional = true)] public ObjectID? AreaDoodadID { get; private set; }

    public override string ScriptTypeName => "HotCargoHandOver";

    public HandOverObjective(ID<ResourceRules> resourceType, ObjectiveTargetShip contact)
    {
        ResourceType = resourceType;
        Contact = contact ?? throw new ArgumentNullException(nameof(contact));
    }

    [GenericConstructor]
    HandOverObjective(GenericSerialReader reader)
    {
        Contact = null!;
        reader.ReflectiveRead(this);
    }

    /// <summary>The open hand-over whose contact is this ship, or null.</summary>
    public static HandOverObjective? ForContact(Run run, ObjectID shipId) => Open(run).FirstOrDefault(o => o.Contact.ShipID == shipId);

    public static IEnumerable<HandOverObjective> Open(Run run)
    {
        var found = new List<HandOverObjective>();
        foreach (Mission m in run.Mode.Missions) m.GetObjectives(found);
        return found.Where(o => o.Completion == CompletionState.InProgress);
    }

    /// <summary>The page outcome of a hand-over (HandOver = true) on this contact. The page cost already took the unit. Sim logic.</summary>
    public static void Deliver(Run run, ObjectID? contact)
    {
        if (contact is not { } id || ForContact(run, id) is not { } o)
            throw new InvalidOperationException($"a HandOver outcome ran on ship {contact?.ToString() ?? "none"}, which is no open hand-over contact");
        Log.Info($"hand-over: {o.ResourceType} delivered to {o.Contact.GetPlainTargetName(run.Game.Sim)}");
        o.Complete();
    }

    /// <summary>Units in the players' ships (stored or carried by crew) and loose in this system.</summary>
    public static int UnitsLeft(SimRoot sim, ID<ResourceRules> resource) =>
        Tech.Vaults.HumanShips(sim).Sum(s => s.Resources.GetResourceTotal(resource, includeStored: true, includeCarried: true, includeOffShipCarried: true))
        + sim.Nuggets.Where(n => n.ResourceType == resource).Sum(n => n.Quantity);

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        ResourceType.GetIntegrityHash(iHash);
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

    POIDoodad? Area(GameRoot game) => AreaDoodadID is { } id ? game.Sim?.Doodads[id] as POIDoodad : null;

    public override bool IsPOIAssociated(POIDoodad poi) =>
        Completion == CompletionState.InProgress && poi.UniqueID == AreaDoodadID && Contact.TargetShip is not { IsWithinRadarOfLocalPlayer: true };

    public override bool IsShipAssociated(MetaShip ship) => Completion == CompletionState.InProgress && Contact.TargetMetaShip == ship;

    public override bool CheckExpired(GameRoot game) => Completion != CompletionState.Completed && !Contact.IsAlive;

    public override bool CanFocusDisplayLine(GameRoot game) => Contact.CanFocus();

    public override void OnDisplayLineFocused(GameRoot game) => Contact.OnFocused();

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState)
    {
        var r = ResourceType.GetResourceRules();
        lines.Add(new DisplayLine
        {
            Text = Strings.KeyStringFmt("HotCargoEventMissions/HandOverFmt", r.TextIconXml, r.NameKey, Contact.GetXmlTargetName(game.Sim)),
            State = Completion,
            CanFocus = CanFocusDisplayLine,
            OnFocused = OnDisplayLineFocused,
        });
    }

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        Contact.Killed += OnContactKilled;
        game.EventHub.Tick += OnTick;
        if (!isLoading && !Contact.IsAlive) OnContactKilled(null, EventArgs.Empty);
    }

    public override void UnregisterEvents(GameRoot game)
    {
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
        MissionCargo.Watch(game);
        if (UnitsLeft(sim, ResourceType) > 0) return;
        Log.Info($"hand-over: no {ResourceType} left in the fleet or the system; the job fails");
        // Fail() can take the mission out of the log at once, so read everything first.
        string name = ResourceType.GetResourceRules().NameKey.Text;
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
    static IEnumerable<ID<ResourceRules>> Items(Mission m)
    {
        var found = new List<HandOverObjective>();
        m.GetObjectives(found);
        return found.Select(o => o.ResourceType);
    }

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
        Watch(m.Game);
    }

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
            int removed = Tech.Vaults.HumanShips(sim).OrderBy(s => s.UniqueID).Select(s => s.Resources.RemoveResources(o.ResourceType, 1)).FirstOrDefault(n => n > 0);
            Log.Info($"mission cargo: {m.MetatypeID} {m.MissionID} left the log without the hand-over; removed {removed} {o.ResourceType}");
        }
    });
}

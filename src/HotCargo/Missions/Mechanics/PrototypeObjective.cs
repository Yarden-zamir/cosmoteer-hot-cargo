using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HotCargo.Core;

namespace HotCargo.Missions.Mechanics;

/// <summary>
/// Rules type "HotCargoPrototype" (spec 133): a pirate ship carries a Prototype Core part. Recover it intact.
/// The core goes onto the carrier's hull the first time the carrier is live after the players accept (ships in stasis have no parts).
/// Recovered: the ship that holds the core surrenders to the players (they hand the core over at once), or it is a wreck or a
/// player ship and a human ship is within RecoverRange of the core. The crew then lift the core out (the part goes away).
/// Failed: the core part is destroyed, or the ship that holds it is gone.
/// When the carrier breaks apart, the core moves to a new ship with the same cell; the objective follows it.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoPrototype")]
internal sealed class PrototypeObjective : Objective
{
    public static readonly ID<PartRules> CoreId = new("yarden_zamir.prototype_core");

    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoPrototype")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : ObjectiveSpawner
    {
        [Serialize, PortableHash] public SpawnedObjectSearch CarrierTag = null!;
        /// <summary>A POI that marks the search area. The objective reveals it on acceptance.</summary>
        [Serialize(Optional = true), PortableHash] public SpawnedObjectSearch? AreaTag;
        [Serialize(Optional = true), PortableHash] public float RecoverRange = 400f;
        [Serialize(Optional = true), PortableHash] public int TierOffset;

        public override Objective? CreateObjectiveImpl(SimRoot sim, Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, out bool invalid)
        {
            invalid = !objects.TryFindObject(CarrierTag, loc, rand, out var carrier);
            if (invalid) return null;
            SimSpawner.SpawnedObject? area = null;
            if (AreaTag is not null && !objects.TryFindObject(AreaTag, loc, rand, out area)) { invalid = true; return null; }
            ObjectID id = carrier!.UniqueID ?? throw new InvalidOperationException($"carrier tag {CarrierTag} found an object without an ID");
            int tier = Math.Max(1, ((CareerSimModeManager)sim.Mode).TierFields.GetTier(carrier.Location.Center) + TierOffset);
            return new PrototypeObjective(new HullRef(sim, id), RecoverRange, tier) { AreaPoiId = area?.UniqueID };
        }
    }

    /// <summary>The ship that holds the core now. It changes when the carrier breaks apart.</summary>
    [Serialize] public HullRef Holder { get; private set; }
    [Serialize] public float RecoverRange { get; private set; }
    [Serialize] public int Tier { get; private set; }
    [Serialize(Optional = true)] public ObjectID? AreaPoiId { get; private set; }
    /// <summary>Cell of the core part in its ship. Null until the core is on the hull.</summary>
    [Serialize(Optional = true)] public IntVector2? CoreCell { get; private set; }
    [Serialize(Optional = true)] public Vector2? LastCoreLocation { get; private set; }

    public override string ScriptTypeName => "HotCargoPrototype";

    public PrototypeObjective(HullRef holder, float recoverRange, int tier)
    {
        Holder = holder ?? throw new ArgumentNullException(nameof(holder));
        if (recoverRange <= 0f) throw new ArgumentOutOfRangeException(nameof(recoverRange), recoverRange, "must be above 0");
        RecoverRange = recoverRange;
        Tier = tier;
    }

    [GenericConstructor]
    PrototypeObjective(GenericSerialReader reader)
    {
        Holder = null!;
        reader.ReflectiveRead(this);
    }

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        Holder.ShipId.GetIntegrityHash(iHash);
        (CoreCell?.X ?? -9999).GetIntegrityHash(iHash);
        (CoreCell?.Y ?? -9999).GetIntegrityHash(iHash);
    }

    public override Range<int>? GetTierRange(SimRoot curSim) => Tier;

    public override MapNode? GetFirstMapNode(GameRoot game) => game.Map.Nodes[Holder.NodeId];

    public override Circle? GetAreaLocation(GameRoot game) =>
        Completion == CompletionState.InProgress && Holder.Location(game.Sim) is { } at ? new Circle(at, 1500f) : null;

    public override bool IsShipAssociated(MetaShip ship) => Completion == CompletionState.InProgress && ship.UniqueID == Holder.ShipId;

    public override bool IsPOIAssociated(POIDoodad poi) => Completion == CompletionState.InProgress && poi.UniqueID == AreaPoiId;

    public override bool CheckExpired(GameRoot game) => Completion == CompletionState.Failed || !Holder.Exists(game.Sim);

    public override void GetStringArgs(Dictionary<string, object?> args, GameRoot game)
    {
        base.GetStringArgs(args, game);
        args["target_ship"] = Holder.XmlName(game);
    }

    public override bool CanFocusDisplayLine(GameRoot game) => Holder.Live(game.Sim) is not null;

    public override void OnDisplayLineFocused(GameRoot game) => Holder.Live(game.Sim)?.FocusCamera();

    public override void GetDisplayLinesImpl(List<DisplayLine> lines, GameRoot game, CompletionState parentState) =>
        lines.Add(new DisplayLine
        {
            Text = Completion == CompletionState.Failed
                ? Strings.KeyString("HotCargoMechanics/CoreLost")
                : Strings.KeyStringFmt("HotCargoMechanics/CoreFmt", Holder.XmlName(game)),
            State = Completion,
            CanFocus = CanFocusDisplayLine,
            OnFocused = OnDisplayLineFocused,
        });

    public override void RegisterEvents(GameRoot game, bool isLoading)
    {
        if (AreaPoiId is { } poiId && game.Sim?.Doodads[poiId] is POIDoodad poi) poi.IsUndiscovered = false;
        game.EventHub.Tick += OnTick;
    }

    public override void UnregisterEvents(GameRoot game) => game.EventHub.Tick -= OnTick;

    void OnTick(object? sender, SimRoot sim)
    {
        if (Mission is null || Completion != CompletionState.InProgress || sim != Mission.Game.Sim || !Holder.InSystem(sim)) return;
        if (!Holder.Exists(sim)) { Lose("the ship that held the core is gone"); return; }
        if (Holder.Live(sim) is not { } holder) return;
        if (CoreCell is null) { Install(sim, holder); return; }
        Part? core = FindCore(sim, ref holder);
        if (core is null) { Lose("the core part was destroyed"); return; }
        Vector2 at = holder.DetTransformPointToWorld(core.LocalCenter);
        LastCoreLocation = at;
        // A carrier that surrendered hands the core over at once: without a faction it leaves the system soon after (AIExitSectorModule).
        bool surrendered = ((CareerSimModeManager)sim.Mode).SurrenderedAIs.Contains(holder.UniqueID);
        if (!surrendered)
        {
            if (!holder.IsJunk && !holder.IsHumanPlayerShip) return;
            if (!sim.Ships.HumanPlayerSignificantShips.Any(s => s != holder && MechanicsModel.Gap(s.DetWorldCenter.DistanceTo(at), s.DetWorldBoundingCircle.Radius, 0f) <= RecoverRange)) return;
        }
        Log.Info($"prototype core recovered from {holder.Metadata.PlainShipName} (surrendered={surrendered}, junk={holder.IsJunk}, player={holder.Metadata.PlayerIndex})");
        holder.Parts.RemovePart(core, PartsManager.CrewAction.None, destroyed: false, removeDoors: true);
        Complete();
    }

    /// <summary>
    /// Puts the core on the carrier's hull. A carrier that is already a wreck (the players broke it before they accepted) cannot take it,
    /// so the mission fails at once.
    /// Limit: the instant build also repairs other damage on the carrier. Revisit if players see a damaged carrier heal when they arrive.
    /// </summary>
    void Install(SimRoot sim, Ship holder)
    {
        if (holder.IsJunk || holder.IsHumanPlayerShip) { Lose("the carrier was wrecked before the core was aboard"); return; }
        PartRules rules = holder.Rules.PartsByID[CoreId];
        Tech.StartingVault.PlaceOutside(holder, rules, requireDoor: false);
        Part core = holder.Parts.Where(p => p.Rules.ID == CoreId).OrderBy(p => p.Location.Y).ThenBy(p => p.Location.X).LastOrDefault()
            ?? throw new InvalidOperationException($"prototype core did not build on {holder.Metadata.PlainShipName}");
        CoreCell = core.Location;
        LastCoreLocation = holder.DetTransformPointToWorld(core.LocalCenter);
        Log.Info($"prototype core installed on {holder.Metadata.PlainShipName} at {core.Location}");
        OnDisplayLinesChanged();
    }

    /// <summary>
    /// The core part on the holder. After a split the part sits on a new ship at the same cell; the holder then moves to that ship.
    /// </summary>
    Part? FindCore(SimRoot sim, ref Ship holder)
    {
        IntVector2 cell = CoreCell ?? throw new InvalidOperationException("core is not installed");
        if (CoreAt(holder, cell) is { } here) return here;
        Vector2 last = LastCoreLocation ?? holder.DetWorldCenter;
        for (int i = 0; i < sim.Ships.Count; i++)
        {
            Ship s = sim.Ships[i];
            if (s == holder || CoreAt(s, cell) is not { } part) continue;
            if (s.DetTransformPointToWorld(part.LocalCenter).DistanceTo(last) > 200f) continue;
            Log.Info($"prototype core moved with a split from {holder.Metadata.PlainShipName} to {s.Metadata.PlainShipName}");
            Holder.ShipId = s.UniqueID;
            holder = s;
            return part;
        }
        return null;
    }

    static Part? CoreAt(Ship ship, IntVector2 cell) =>
        ship.Parts[cell] is { } p && p.Rules.ID == CoreId && p.Location == cell && p.Health > 0 ? p : null;

    void Lose(string why)
    {
        Log.Info($"prototype mission failed: {why}");
        Fail();
    }

    public override int GetTypeIntegrityHash() => 0x4843_5052; // "HCPR"
}

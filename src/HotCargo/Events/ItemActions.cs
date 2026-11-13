using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.Network;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using HarmonyLib;
using HotCargo.Core;
using System.Runtime.CompilerServices;

namespace HotCargo.Events;

/// <summary>One possible result of an item action (spec 1112). The weights pick one when a player uses the action.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ItemContentRules
{
    /// <summary>The item that the action puts into the storage. Null: nothing.</summary>
    [Serialize(Optional = true)] public ID<ResourceRules>? Resource;
    [Serialize(Optional = true)] public float Weight = 1f;
    [Serialize(Optional = true)] public float WeightPerHeat;
    /// <summary>The result message and chat line. {0} = the ship.</summary>
    [Serialize] public KeyString MessageKey = null!;
}

/// <summary>
/// A context action of an item (spec 1111). The item's resource rules list them in <c>HotCargoActions</c>. A storage part that holds
/// a unit shows one button for each action; the button acts on one unit in that part.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ItemActionRules
{
    [Serialize] public string ID = "";
    [Serialize] public KeyString NameKey = null!;
    [Serialize(Optional = true)] public KeyString? ToolTipKey;
    /// <summary>The unit turns into this item, in the same storage slot. Null: the unit is used up.</summary>
    [Serialize(Optional = true)] public ID<ResourceRules>? Becomes;
    /// <summary>One of these goes into the same storage, or loose beside the ship when it is full. Empty: no contents.</summary>
    [Serialize(Optional = true)] public ItemContentRules[] Contents = [];
    /// <summary>The message when there are no contents. {0} = the ship.</summary>
    [Serialize(Optional = true)] public KeyString? MessageKey;
}

/// <summary>Item context actions (spec 1111 to 1114): rules, the sim side, and the MP input.</summary>
public static class ItemActions
{
    static readonly ConditionalWeakTable<ResourceRules, ItemActionRules[]> Table = new();

    /// <summary>Test only (item_force): the next action picks this content index. Set on every machine in co-op.</summary>
    internal static int? ForcedContent;

    /// <summary>Reads <c>HotCargoActions</c> from the same rules node as the vanilla resource rules.</summary>
    [HarmonyPatch(typeof(ResourceRules), MethodType.Constructor, typeof(GenericSerialReader))]
    static class ReadPatch
    {
        static void Postfix(ResourceRules __instance, GenericSerialReader reader) => Errors.Guard("item actions read", () =>
        {
            if (!reader.TryReadFromPath<ItemActionRules[]>("HotCargoActions", out var actions) || actions.Length == 0) return;
            string at = $"HotCargoActions of {__instance.ID}";
            if (actions.Select(a => a.ID).Distinct().Count() != actions.Length) throw new InvalidDataException($"{at}: two actions with one ID");
            foreach (var a in actions)
            {
                if (a.ID.Length == 0) throw new InvalidDataException($"{at}: an action has no ID");
                if (a.Contents.Length == 0 && a.MessageKey is null) throw new InvalidDataException($"{at} {a.ID}: an action without Contents needs a MessageKey");
                if (a.Contents.Any(c => c.Weight < 0 || c.WeightPerHeat < 0)) throw new InvalidDataException($"{at} {a.ID}: weights must not be negative");
            }
            Table.AddOrUpdate(__instance, actions);
        });
    }

    public static IReadOnlyList<ItemActionRules> For(ID<ResourceRules> item) =>
        Table.TryGetValue(item.GetResourceRules(), out var a) ? a : [];

    /// <summary>Every item with actions, in rules order, with its actions.</summary>
    public static IEnumerable<(ResourceRules Item, ItemActionRules Action)> All() =>
        GameApp.Rules.Resources.SelectMany(r => Table.TryGetValue(r, out var a) ? a.Select(x => (r, x)) : []);

    static IEnumerable<FlexResourceGrid.ResourceTile> Tiles(Part part) => FlexResourceGrid.GetAllOnPart(part).SelectMany(g => g);

    /// <summary>True when the part's storage holds a unit of the item.</summary>
    public static bool Holds(Part part, ID<ResourceRules> item) => Tiles(part).Any(t => t.HasResources && t.ResourceType == item);

    /// <summary>
    /// Sim logic, from the MP input on every machine. Takes one unit of the item out of the part, puts its Becomes item into the same
    /// slot and one rolled content into the part (or loose). Returns the message, or null when the part holds no unit.
    /// </summary>
    public static string? Apply(Run run, SimRoot sim, Part part, ID<ResourceRules> item, string actionId)
    {
        var action = For(item).FirstOrDefault(a => a.ID == actionId)
            ?? throw new InvalidOperationException($"item {item} has no action {actionId}");
        var tile = Tiles(part).FirstOrDefault(t => t.HasResources && t.ResourceType == item);
        if (tile is null) return null;
        var ship = part.Ship ?? throw new InvalidOperationException("the part has no ship");
        tile.SetResources(tile.Resources - 1, item);
        if (action.Becomes is { } becomes) Place(sim, part, tile, becomes);
        string message;
        if (action.Contents.Length > 0)
        {
            var weights = action.Contents.Select(c => EventModel.EffectiveWeight(c.Weight, c.WeightPerHeat, 0f, run.State.Heat, run.State.FleetTech)).ToList();
            float roll = sim.Rand.Single();
            int pick = ForcedContent ?? EventModel.PickWeighted(weights, roll);
            ForcedContent = null;
            if (pick >= action.Contents.Length) throw new InvalidOperationException($"forced content {pick + 1} of {action.Contents.Length}");
            if (pick < 0) throw new InvalidDataException($"all contents of {item} {actionId} weigh 0");
            var content = action.Contents[pick];
            if (content.Resource is { } r) Place(sim, part, tile, r);
            message = Strings.FormatText(content.MessageKey.Key, ship.Metadata.XmlShipName);
            Log.Info($"item action {item} {actionId} on {ship.Metadata.PlainShipName}: roll {roll:0.000} over [{string.Join(", ", weights.Select(w => w.ToString("0.##")))}] -> {content.Resource?.ToString() ?? "nothing"}");
        }
        else
        {
            message = action.MessageKey is { } k ? Strings.FormatText(k.Key, ship.Metadata.XmlShipName) : "";
            Log.Info($"item action {item} {actionId} on {ship.Metadata.PlainShipName}");
        }
        return message;
    }

    /// <summary>One unit into the part: the freed slot first, then a slot that takes it, else loose beside the ship.</summary>
    static void Place(SimRoot sim, Part part, FlexResourceGrid.ResourceTile freed, ID<ResourceRules> r)
    {
        var tile = new[] { freed }.Concat(Tiles(part)).FirstOrDefault(t => Takes(t, r));
        if (tile is not null)
        {
            tile.SetResources(tile.Resources + 1, r);
            return;
        }
        var ship = part.Ship!;
        float radius = ship.WorldBoundingRadius + 30f;
        var rect = new RotRect(ship.DetWorldCenter + new Vector2(radius, 0f), new Vector2(40f, 40f), Direction.Zero);
        sim.Nuggets.DropNuggets(r, 1, rect, Vector2.Zero, randomVelocity: true);
        Log.Info($"item action: no room in {ship.Metadata.PlainShipName} for {r}; it drifts loose");
    }

    static bool Takes(FlexResourceGrid.ResourceTile t, ID<ResourceRules> r)
    {
        var preferred = t.PreferredResourceType.Confirmed;
        if (preferred is { } p && p != r) return false;
        if (!t.HasResources) return true;
        return t.ResourceType == r && t.Resources < r.GetResourceRules().MaxStackSize;
    }
}

/// <summary>Spec 1116: a Tracer Beacon in a player ship adds heat once a minute. Loose beacons do not count.</summary>
static class TracerBeacon
{
    public static readonly ID<ResourceRules> Id = new("yarden_zamir.tracer_beacon");
    const double PingSeconds = 60;

    /// <summary>Sim tick (Run.Hooks).</summary>
    internal static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("tracer beacon tick", () =>
    {
        if (sim != run.Game.Sim) return;
        var s = run.State.Events;
        int units = HandOverObjective.FleetUnits(sim, Id);
        double now = run.State.GameSeconds;
        if (units == 0) { s.TracerPingSeconds = -1; return; }
        if (s.TracerPingSeconds < 0) { s.TracerPingSeconds = now + PingSeconds; return; }
        if (now < s.TracerPingSeconds) return;
        s.TracerPingSeconds = now + PingSeconds;
        float heat = run.T.Events.TracerHeatPerMinute * units;
        run.AddHeat(heat, sim);
        Log.Info($"tracer beacon: {units} aboard, heat +{heat:0.#}");
        // UI only.
        Ux.UxKit.ChatLine(run.Game, Strings.FormatText("HotCargoItems/TracerPingFmt", heat.ToString("0.#")));
    });
}

/// <summary>A player uses an item action on a storage part (spec 1113). Runs in lockstep on every machine.</summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoItemAction")]
internal sealed class ItemActionInput : MPInput
{
    [Serialize] public Part? Part { get; private set; }
    [Serialize] public ID<ResourceRules> Item { get; private set; }
    [Serialize] public string Action { get; private set; } = "";

    public ItemActionInput(Part part, ID<ResourceRules> item, string action)
    {
        Part = part ?? throw new ArgumentNullException(nameof(part));
        Item = item;
        Action = action;
    }

    ItemActionInput() { }

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false) => Errors.Guard("item action", () =>
    {
        if (Run.Current is not { } run || run.Game != game || game.Sim is not { } sim) return;
        if (Part?.Ship is not { } ship || !Part.ExistsInSim(sim) || Part.IsDestroyed || !ship.Commands.IsCommandableBy(playerIndex)) return;
        if (ItemActions.Apply(run, sim, Part, Item, Action) is not { Length: > 0 } message) return;
        // UI only: every machine shows the same result.
        Ux.UxKit.CenterText(game, message);
        Ux.UxKit.ChatLine(game, message);
    });
}

[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.AllowMPInput))]
static class AllowItemActionPatch
{
    static void Postfix(MPInput mpInput, ref bool __result)
    {
        if (mpInput is ItemActionInput) __result = true;
    }
}

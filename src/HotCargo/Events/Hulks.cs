using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Cosmoteer.Localization;
using Halfling;
using Halfling.Geometry;
using Halfling.ObjectText;
using Halfling.Serialization;
using HotCargo.Core;
using HotCargo.Tech;

namespace HotCargo.Events;

/// <summary>One loot outcome of a hulk kind (spec 1124). Weights: Weight + WeightPerHeat × heat.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class HulkLootRules
{
    [Serialize(Optional = true)] public float Weight = 1f;
    [Serialize(Optional = true)] public float WeightPerHeat;
    /// <summary>Cargo that goes into the hulk's storage.</summary>
    [Serialize(Optional = true)] public ResourceAmount[] Cargo = [];
    /// <summary>The message when the hold breaks open. {0} = the hulk's name.</summary>
    [Serialize] public string Text = "";
    /// <summary>Spec 1126: when the hold breaks open, player ships within Events.HulkBlastRadius lose this fraction of max health on their nearest parts.</summary>
    [Serialize(Optional = true)] public float Blast;
}

/// <summary>A kind of hulk (spec 1124): Abandoned Ship, Cold Hulk.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class HulkKindRules
{
    [Serialize] public string ID = "";
    [Serialize(Optional = true)] public float Weight = 1f;
    /// <summary>Hulks of this kind per run. 0: no limit.</summary>
    [Serialize(Optional = true)] public int MaxPerRun;
    /// <summary>The approach message. {0} = the hulk's name.</summary>
    [Serialize] public string Text = "";
    [Serialize] public HulkLootRules[] Loot = [];
}

[ReflectiveSerialization(Explicit = true)]
sealed class HulkFile
{
    [Serialize] public HulkKindRules[] Hulks = [];
}

/// <summary>One part of a hulk's hold. A destroyed part leaves structure on its cell, so the part ID tells the two apart.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class HoldPart
{
    [Serialize] public IntVector2 Cell;
    [Serialize] public string Part = "";
}

/// <summary>The rolled loot of one hulk, saved in EventsState.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class HulkState
{
    [Serialize] public string Kind = "";
    [Serialize] public int Loot;
    /// <summary>The cells of the hold: the parts that got loot, or the cargo storage parts when the loot is nothing. One lost: the hold broke open.</summary>
    [Serialize] public List<HoldPart> Hold = [];
    [Serialize(Optional = true)] public bool Messaged;
}

/// <summary>
/// Spec 1124 to 1126: vanilla abandoned ships carry real loot, rolled when they first show up live. The players see it when they
/// wreck or salvage the hulk. Content: mod/events/hulks.rules.
/// </summary>
public static class Hulks
{
    public static string RulesPath => Path.Combine(Mod.ModRoot, "events", "hulks.rules");
    static HulkKindRules[]? _kinds;

    /// <summary>Test only (ev_hulk_force): the next stocked hulk gets this kind and loot index, and the loot chance is skipped. Set on every machine in co-op.</summary>
    internal static (string Kind, int Loot)? Forced;

    /// <summary>All hulk kinds in file order. Loads and validates on first use; a content error throws.</summary>
    public static IReadOnlyList<HulkKindRules> Kinds => _kinds ??= Load();

    static HulkKindRules[] Load()
    {
        var file = App.ObjectTextSerializer.Read<HulkFile>(new OTFile(RulesPath)) ?? throw new InvalidDataException($"{RulesPath} is empty");
        if (file.Hulks.Length == 0) throw new InvalidDataException($"{RulesPath} has no hulks");
        if (file.Hulks.Select(k => k.ID).Distinct().Count() != file.Hulks.Length) throw new InvalidDataException($"{RulesPath}: two hulks with one ID");
        foreach (var k in file.Hulks)
        {
            string at = $"{RulesPath} {k.ID}";
            if (k.ID.Length == 0 || k.Text.Length == 0) throw new InvalidDataException($"{at}: a hulk needs an ID and a Text");
            if (k.Weight < 0 || k.MaxPerRun < 0 || k.Loot.Length == 0) throw new InvalidDataException($"{at}: Weight and MaxPerRun must be 0 or more, and Loot must not be empty");
            foreach (var l in k.Loot)
            {
                if (l.Weight < 0 || l.WeightPerHeat < 0 || l.Text.Length == 0) throw new InvalidDataException($"{at}: loot needs a Text and weights of 0 or more");
                if (l.Blast is < 0 or > 1) throw new InvalidDataException($"{at}: Blast must be 0 to 1");
                foreach (var c in l.Cargo)
                    if (GameApp.Rules.Resources.All(r => r.ID.ToString() != c.ID) || c.Amount <= 0) throw new InvalidDataException($"{at}: cargo '{c.ID}' is not a resource or has no amount");
            }
        }
        return file.Hulks;
    }

    public static HulkKindRules Kind(string id) => Kinds.FirstOrDefault(k => k.ID == id) ?? throw new InvalidDataException($"no hulk '{id}' in {RulesPath}");

    /// <summary>True when the part holds a unit of one of the resources, in cargo tiles or in a tank (any simple storage).</summary>
    static bool Holds(Part part, IReadOnlyCollection<ID<ResourceRules>> ids) =>
        FlexResourceGrid.GetAllOnPart(part).SelectMany(g => g).Any(t => t.HasResources && ids.Contains(t.ResourceType!.Value))
        || part.Components.OfType<ISimpleResourceStorage>().Any(c => c.Resources > 0 && ids.Contains(c.ResourceType));

    /// <summary>The hold of a freshly stocked hulk (HulkState.Hold), by part location.</summary>
    static List<HoldPart> HoldOf(Ship hulk, HulkLootRules loot)
    {
        var ids = loot.Cargo.Select(c => new ID<ResourceRules>(c.ID)).ToHashSet();
        var parts = ids.Count > 0 ? hulk.Parts.Where(p => Holds(p, ids)) : hulk.Parts.Where(p => FlexResourceGrid.GetAllOnPart(p).Any());
        return [.. parts.OrderBy(p => p.Location.Y).ThenBy(p => p.Location.X).Select(p => new HoldPart { Cell = p.Location, Part = p.Rules.ID.ToString() })];
    }

    /// <summary>True when a part of the hold is destroyed or gone.</summary>
    static bool HoldBroken(Ship hulk, HulkState h) =>
        h.Hold.Any(x => hulk.Parts[x.Cell] is not { } p || p.Location != x.Cell || p.IsDestroyed || p.Rules.ID.ToString() != x.Part);

    static IEnumerable<FlexResourceGrid.ResourceTile> Tiles(Ship ship) => FlexResourceGrid.GetAllOnShip(ship).SelectMany(g => g);

    /// <summary>
    /// Sim logic, once per hulk. With chance HulkLootChance it rolls a kind and a loot outcome (sim random generator), clears the
    /// vanilla cargo and puts the loot cargo into the storage. Else the hulk keeps its vanilla cargo and stays quiet.
    /// </summary>
    internal static void Stock(Run run, SimRoot sim, Ship hulk)
    {
        var s = run.State.Events;
        var forced = Forced;
        Forced = null;
        float chanceRoll = sim.Rand.Single();
        if (forced is null && chanceRoll >= run.T.Events.HulkLootChance) return;
        var kinds = Kinds;
        int kindIndex = forced is { } f ? kinds.ToList().FindIndex(k => k.ID == f.Kind)
            : EventModel.PickWeighted(kinds.Select(k => k.MaxPerRun > 0 && s.HulkKinds.GetValueOrDefault(k.ID) >= k.MaxPerRun ? 0f : k.Weight).ToList(), sim.Rand.Single());
        if (kindIndex < 0)
        {
            if (forced is not null) throw new InvalidOperationException($"no hulk kind '{forced.Value.Kind}'");
            return;
        }
        var kind = kinds[kindIndex];
        var weights = kind.Loot.Select(l => EventModel.EffectiveWeight(l.Weight, l.WeightPerHeat, 0f, run.State.Heat, 0)).ToList();
        int loot = forced?.Loot ?? EventModel.PickWeighted(weights, sim.Rand.Single());
        if (loot < 0 || loot >= kind.Loot.Length) throw new InvalidOperationException($"hulk {kind.ID}: loot {loot} of {kind.Loot.Length}");

        foreach (var tile in Tiles(hulk).Where(t => t.HasResources && t.PreferredResourceType.Confirmed is null)) tile.ClearResources();
        var placed = new List<string>();
        int total = 0;
        foreach (var c in kind.Loot[loot].Cargo)
        {
            var id = new ID<ResourceRules>(c.ID);
            int want = c.At(run.CurrentTier), done = Fill(hulk, id, want);
            // Cargo that the cargo tiles do not take goes where the game puts it: tanks (hyperium in a hyperdrive) and other storage.
            if (done < want) done += hulk.Resources.AddResources(id, want - done, excludeConstructionSinks: true);
            placed.Add($"{c.ID} {done}/{want}");
            total += done;
        }
        // A hull with no room for any of the cargo gets no loot and no messages, so a message never promises cargo that is not there.
        // Its vanilla cargo is already gone. Limit: rare (a hulk without storage or tanks); revisit if playtests find empty hulks often.
        if (total == 0 && kind.Loot[loot].Cargo.Length > 0)
        {
            Log.Info($"hulk {hulk.Metadata.PlainShipName}: {kind.ID} loot {loot + 1} has no room ({string.Join(", ", placed)}); no loot");
            return;
        }
        // The vanilla stasis spawner rolls new trade cargo when the hulk comes back from stasis, unless it remembers the cargo.
        if (hulk.SpecialStasisHandler is StasisShipSpawner.StasisHandler h) h.OriginalSpawner.RememberResourcesAndCrew = true;
        s.HulkKinds[kind.ID] = s.HulkKinds.GetValueOrDefault(kind.ID) + 1;
        s.Hulks[hulk.UniqueID] = new HulkState { Kind = kind.ID, Loot = loot, Hold = HoldOf(hulk, kind.Loot[loot]) };
        Log.Info($"hulk {hulk.Metadata.PlainShipName}: {kind.ID} loot {loot + 1} (heat {run.State.Heat:0}); cargo {string.Join(", ", placed)}; hold {s.Hulks[hulk.UniqueID].Hold.Count} parts");
    }

    /// <summary>Puts units into storage tiles that take the resource, in tile order. Returns the units placed; the rest does not fit.</summary>
    static int Fill(Ship ship, ID<ResourceRules> id, int amount)
    {
        int left = amount, max = id.GetResourceRules().MaxStackSize;
        foreach (var tile in Tiles(ship))
        {
            if (left == 0) break;
            if (!ItemActions.Takes(tile, id)) continue;
            int have = tile.HasResources ? tile.Resources : 0, n = Math.Min(max - have, left);
            if (n <= 0) continue;
            tile.SetResources(have + n, id);
            left -= n;
        }
        return amount - left;
    }

    /// <summary>Sim tick, twice a game second: new hulks get their roll, an approach shows the kind's message, a broken hold its outcome.</summary>
    internal static void Check(Run run, SimRoot sim)
    {
        var s = run.State.Events;
        var t = run.T.Events;
        var players = sim.Ships.HumanPlayerSignificantShips.ToList();
        foreach (var hulk in sim.Ships.Where(sh => sh.HasLabel(Abandoned.Label) && !sh.HasTag(Abandoned.RelicTag)).OrderBy(sh => sh.UniqueID).ToList())
        {
            if (s.AbandonedSeen.Add(hulk.UniqueID)) Stock(run, sim, hulk);
            if (!s.Hulks.TryGetValue(hulk.UniqueID, out var h)) continue;
            var kind = Kind(h.Kind);
            if (!h.Messaged && players.Count > 0 && players.Min(p => Distance(p, hulk)) <= t.AbandonedTriggerDistance)
            {
                h.Messaged = true;
                Say(run.Game, string.Format(kind.Text, hulk.Metadata.XmlShipName));
            }
            if (HoldBroken(hulk, h)) Reveal(run, sim, hulk, h);
        }
        // A hulk that left the sim and is not in stasis is gone: its hold broke open somewhere.
        foreach (var id in s.Hulks.Keys.Where(id => sim.Ships[id] is null && !sim.Stasis.Any(st => st.ReservedObjectID == id)).OrderBy(id => id).ToList())
            Reveal(run, sim, null, s.Hulks[id], id);
    }

    static float Distance(Ship a, Ship b) => a.DetWorldBoundingCircle.DistanceTo(b.DetWorldBoundingCircle);

    /// <summary>The outcome message, once per hulk, and the blast. A hulk that is gone gets the message without the blast.</summary>
    static void Reveal(Run run, SimRoot sim, Ship? hulk, HulkState h, ObjectID? goneId = null)
    {
        var s = run.State.Events;
        s.Hulks.Remove(hulk?.UniqueID ?? goneId ?? throw new ArgumentNullException(nameof(goneId)));
        var loot = Kind(h.Kind).Loot[h.Loot];
        string name = hulk?.Metadata.XmlShipName ?? Strings.GetText("HotCargoEvents/HulkGone");
        string text = string.Format(loot.Text, name);
        int hit = hulk is not null && loot.Blast > 0 ? Blast(sim, hulk, loot.Blast, run.T.Events.HulkBlastRadius) : 0;
        if (hit > 0) text += " " + EventText.HullDamage(hit);
        Log.Info($"hulk {hulk?.Metadata.PlainShipName ?? "(gone)"}: {h.Kind} loot {h.Loot + 1} revealed, blast hit {hit} parts");
        Say(run.Game, text);
    }

    /// <summary>
    /// Spec 1126: each player ship whose hull is within radius of the hulk loses fraction × max health on its BlastParts parts nearest to
    /// the hulk. Parts keep 1 health; vaults are skipped. No random: the order is distance, then location.
    /// </summary>
    static int Blast(SimRoot sim, Ship hulk, float fraction, float radius)
    {
        const int BlastParts = 6;
        int hits = 0;
        foreach (var ship in Vaults.HumanShips(sim).Where(sh => Distance(sh, hulk) <= radius).OrderBy(sh => sh.UniqueID))
        {
            var local = ship.DetTransformPointFromWorld(hulk.DetWorldCenter);
            foreach (var part in ship.Parts.Where(p => p.Rules.ID != Vaults.VaultId && p.Health > 1)
                .OrderBy(p => new Halfling.Geometry.Vector2(p.Rect.X + p.Rect.Width / 2f, p.Rect.Y + p.Rect.Height / 2f).DistanceSquaredTo(local)).ThenBy(p => p.Location.Y).ThenBy(p => p.Location.X).Take(BlastParts).ToList())
            {
                part.Health = Math.Max(1, part.Health - (int)(fraction * part.Rules.MaxHealth));
                hits++;
            }
        }
        return hits;
    }

    /// <summary>UI only: every machine shows the same line in the center and in the chat.</summary>
    static void Say(GameRoot game, string xml)
    {
        Ux.UxKit.CenterText(game, xml);
        Ux.UxKit.ChatLine(game, xml);
    }
}

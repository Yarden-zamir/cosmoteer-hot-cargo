using Cosmoteer;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Generators.Galaxies;
using Cosmoteer.Modes.Career;
using Halfling;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;

namespace HotCargo.Journey;

/// <summary>
/// Galaxy map rules type "HotCargoTierCap" (spec requirement 3). Runs after the vanilla tier spawners.
/// A system that is J jumps from the start gets at most the low tier (start tier + TierPerJump × J), so a sector never jumps ahead
/// of the main route. Systems above MaxChangeableLowTier (faction home systems) keep their tier.
/// The result depends on jump distances only, not on the order of node connections.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoTierCap")]
[TrackMultipleReferences]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class TierCapSpawner : GalaxySpawner
{
    [Serialize, PortableHash] public int TierPerJump;
    [Serialize, PortableHash] public int MaxChangeableLowTier;

    // Public because the build publicizes the game; the runtime allows a wider access on an override.
    public override void DoSpawn(MapRoot map, Rand rand, Action<MapNode>? onNodeCreated)
    {
        if (map.Game.Mode is not CareerGameModeManager mode) return;
        var start = map.Nodes.FirstOrDefault(n => mode.GetMapNodeData(n).IsInitNode)
            ?? throw new InvalidOperationException("HotCargoTierCap: the map has no starting system; put it after StartingNodePicker");
        int startTier = mode.GetMapNodeData(start).TierRange?.Min ?? 1;
        foreach (var (node, jumps) in JumpDistances(start))
        {
            var data = mode.GetMapNodeData(node);
            if (data.TierRange is { } range && TierCapModel.Capped(range.Min, range.Max, Cap(startTier, jumps), MaxChangeableLowTier) is var (min, max))
                data.TierRange = new Range<int>(min, max);
        }
    }

    public int Cap(int startTier, int jumps) => TierCapModel.Cap(startTier, TierPerJump, jumps);

    /// <summary>Spec 305: the main route tier of a sector index: the peak tier cap, up to MaxChangeableLowTier (the final sector tier).
    /// Null when the career does not use the Hot Cargo run map.</summary>
    public static int? RouteTier(CareerGameModeManager mode, int sectorIndex) =>
        RunMapCap(mode) is { } cap ? TierCapModel.RouteTier(StartTier(mode), cap.TierPerJump, cap.MaxChangeableLowTier, sectorIndex) : null;

    static TierCapSpawner? RunMapCap(CareerGameModeManager mode) =>
        mode.Init is CareerInit init && init.MapSizeIndex < GameApp.Rules.CareerMode.MapSizes.Length
            ? GameApp.Rules.CareerMode.MapSizes[init.MapSizeIndex].MapGenerator.Spawners.OfType<TierCapSpawner>().FirstOrDefault()
            : null;

    /// <summary>
    /// Spec 303: the highest tier that counts toward the peak tier (and so the tech value) at this sector index. A system J jumps from
    /// the start needs at least J + 1 visited systems, so normal systems never pass this cap. Only faction home systems do: a short visit
    /// to one must not multiply the tech value. Null when the career does not use the Hot Cargo run map (no cap).
    /// </summary>
    public static int? PeakTierCap(CareerGameModeManager mode, int sectorIndex) =>
        RunMapCap(mode) is { } cap ? TierCapModel.PeakTierCap(StartTier(mode), cap.TierPerJump, sectorIndex) : null;

    static int StartTier(CareerGameModeManager mode)
    {
        var start = mode.Game.Map.Nodes.FirstOrDefault(n => mode.GetMapNodeData(n).IsInitNode)
            ?? throw new InvalidOperationException("the career map has no starting system");
        return mode.GetMapNodeData(start).TierRange?.Min ?? 1;
    }

    /// <summary>Each node reachable from the start with its jump count (breadth-first).</summary>
    public static Dictionary<MapNode, int> JumpDistances(MapNode start) => TierCapModel.JumpDistances(start, n => n.Connections);
}

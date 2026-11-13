using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Nebulas;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using HotCargo.Core;
using System.Runtime.CompilerServices;

namespace HotCargo.Worlds;

/// <summary>The current of a trade hub (spec 221): its flow direction, its push and its flow arrows.</summary>
public static class Current
{
    static readonly WorldsTunables Defaults = new();

    /// <summary>
    /// Tunables for sim code. The first system of a new career generates before the run starts, and a sim can tick
    /// before Run.Start; those use the defaults.
    /// </summary>
    internal static WorldsTunables Tunables(SimRoot? sim) => Run.Current is { } run && sim is not null && run.Game.Sim == sim ? run.T.Worlds : Defaults;

    /// <summary>The flow direction index of the system of a sim. It comes from the map node seed, so it is the same after a load and on every client.</summary>
    public static int FlowIndex(SimRoot sim) =>
        WorldsModel.FlowIndex((sim.Mode as CareerSimModeManager ?? throw new InvalidOperationException("currents need a career sim")).MapNodeData.RandomSeed);

    public static Vector2 FlowVector(SimRoot sim)
    {
        var (x, y) = WorldsModel.Flow(FlowIndex(sim));
        return new Vector2(x, y);
    }

    /// <summary>Centers of the triangles of a nebula type, in world space. The nebula painting covers at most the unexplored radius.</summary>
    internal static List<Vector2> TriangleCenters(SimRoot sim, ID<NebulaTypeRules> type)
    {
        var tris = new HashSet<TriCoord>();
        sim.Nebulas.GetApproxInside(new Circle(Vector2.Zero, GameApp.Rules.CareerMode.Exploration.UnexploredRadius), tris);
        float size = sim.Nebulas.TriangleSize;
        return tris.Where(t => sim.Nebulas[t] == type).Order().Select(t => t.Center * size).ToList();
    }

    public static readonly ID<NebulaTypeRules> NebulaId = new("hc_current");

    /// <summary>
    /// The current of a sim for the push: its center (a point on the band axis) and a circle that holds all of it.
    /// Derived from the nebula, so it is the same after a load and on every client. Empty is true when the sim has no current.
    /// Limit: one band per system (the trade hub spawner runs once). With two bands the center lies between them and the
    /// outward side is wrong for one; revisit when a system can get a second current.
    /// </summary>
    internal readonly record struct Band(bool Empty, Vector2 Center, float Radius);

    /// <summary>Sim only: the GUI keeps its own triangle list, so a GUI frame never fills this cache.</summary>
    static readonly ConditionalWeakTable<SimRoot, StrongBox<Band>> Bands = new();

    internal static Band BandOf(SimRoot sim) => Bands.GetValue(sim, s =>
    {
        var centers = TriangleCenters(s, NebulaId);
        if (centers.Count == 0) return new StrongBox<Band>(new Band(true, Vector2.Zero, 0f));
        Vector2 sum = Vector2.Zero;
        foreach (var c in centers) sum += c;
        Vector2 center = sum / centers.Count;
        float radius = centers.Max(c => c.DistanceTo(center)) + s.Nebulas.TriangleSize;
        return new StrongBox<Band>(new Band(false, center, radius));
    }).Value;

    /// <summary>Drops the cached band of a sim after the current changes (a spawn). Called from the sim only.</summary>
    internal static void Forget(SimRoot sim) => Bands.Remove(sim);

    /// <summary>The outward direction of spec 221 at a point: across the flow, toward the nearer side of the band.</summary>
    internal static (float X, float Y) Outward(Vector2 flow, Band band, Vector2 at)
    {
        Vector2 offset = at - band.Center;
        return WorldsModel.Outward(flow.X, flow.Y, offset.X, offset.Y);
    }
}

/// <summary>
/// Rules type "HotCargoCurrent" (spec 221): paints a straight band of a nebula type, centered on the spawn location,
/// along the flow direction of the system. Length and Width are in world units. A triangle is painted when its center is
/// within Width / 2 of the band axis. With the default triangle (side 2000), a band needs a width of about 2300 or more to stay
/// one connected cloud in every flow direction.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoCurrent")]
[TrackMultipleReferences]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class CurrentSpawner : SimObjectSpawner
{
    [Serialize, PortableHash] public ID<NebulaTypeRules> NebulaType;
    [Serialize, PortableHash] public float Length;
    [Serialize, PortableHash] public float Width;
    [Serialize(Optional = true), PortableHash] public float MaxDistanceFromWorldOrigin = float.PositiveInfinity;

    public override void DoSpawn(SimRoot sim, Rand rand, int wholeSpawnSeed, int index, int totalCount, object? spawnData, Vector2 loc,
        List<SpawnedObject> parentTree, SpawnedObjectCollection spawnedObjects, float areaRadius, bool loadInstantly,
        List<(ObjectID? ObjectID, Ship? Ship, StasisSpawner? StasisSpawner, int? PlayerIndex)> spawned)
    {
        if (Length <= 0f || Width <= 0f) throw new InvalidDataException($"HotCargoCurrent needs Length and Width above 0 (got {Length}, {Width})");
        Vector2 half = Current.FlowVector(sim) * (Length / 2f);
        Vector2 a = loc - half, b = loc + half;
        var tris = new HashSet<TriCoord>();
        sim.Nebulas.GetApproxInside(new Circle(loc, Length / 2f + Width), tris);
        float size = sim.Nebulas.TriangleSize;
        int painted = 0;
        foreach (TriCoord tri in tris.Order())
        {
            Vector2 c = tri.Center * size;
            if (c.Length > MaxDistanceFromWorldOrigin) continue;
            if (WorldsModel.DistanceToSegment(c.X, c.Y, a.X, a.Y, b.X, b.Y) > Width / 2f) continue;
            sim.Nebulas[tri] = NebulaType;
            painted++;
        }
        Current.Forget(sim);
        Log.Info($"current painted: {painted} triangles at {loc}, flow {Current.FlowIndex(sim)}");
        // Like a None spawner: one spawned point, so tags and sub-spawners work.
        spawned.Add(default);
    }
}

/// <summary>
/// Nebula active effect "HotCargoCurrentPush" (spec 221). Each sim tick, it pushes the live ships in the nebula along the
/// flow of the system and toward the nearer side of the band (WorldsModel.Push). Stations and the ship types in
/// IgnoreShipTypes do not drift. The force scales with the ship's mass, so every ship gets the same acceleration.
/// Loose items (vanilla nuggets: resources and tech crates) get the same push.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[ReflectivePortableHashing(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoCurrentPush")]
internal sealed class CurrentPush : NebulaActiveEffectRules
{
    [Serialize(Optional = true), PortableHash] public HashSet<ID<ShipRules>>? IgnoreShipTypes;

    public override void DoEffects(SimRoot sim, SimNebulasManager nebulas, NebulaTypeRules nebulaType, ref object? effectsState)
    {
        IReadOnlyList<Ship> ships = nebulas.GetShipsInNebula(nebulaType.ID);
        if (ships.Count == 0 && sim.Nuggets.Count == 0) return;
        Errors.Guard("current push", () =>
        {
            var band = Current.BandOf(sim);
            if (band.Empty) return;
            var t = Current.Tunables(sim);
            Vector2 flowVector = Current.FlowVector(sim);
            (float X, float Y) flow = (flowVector.X, flowVector.Y);
            for (int i = 0; i < ships.Count; i++)
            {
                Ship ship = ships[i];
                if (IgnoreShipTypes?.Contains(ship.Rules.ID) == true) continue;
                if (ship.Physics.Body is not { } body) continue;
                if (SpaceStation.GetStation(ship, onlyOperating: false) is not null) continue;
                Vector2 velocity = body.LinearVelocity;
                var (ax, ay) = WorldsModel.Push(velocity.X, velocity.Y, flow, Current.Outward(flowVector, band, ship.DetWorldCenter),
                    t.CurrentFlowSpeed, t.CurrentAccel, t.CurrentSideShare, ship.Nebulas.GetNebulaStrength(nebulaType.ID));
                if (ax != 0f || ay != 0f) body.ApplyForce(new Vector2(ax, ay) * body.Mass);
            }
            // Items get the ship rule as a velocity change (their vanilla drag still applies, so they drift slower than ships).
            float dt = sim.FixedUpdater.Interval;
            for (int i = 0; i < sim.Nuggets.Count; i++)
            {
                Nugget nugget = sim.Nuggets[i];
                Vector2 at = nugget.DetWorldLocation;
                if (at.DistanceTo(band.Center) > band.Radius) continue;
                float strength = nebulas.GetInsideStrength(at, nebulaType.ID);
                if (strength <= 0f) continue;
                Vector2 v = nugget.LinearVelocity;
                var (ax, ay) = WorldsModel.Push(v.X, v.Y, flow, Current.Outward(flowVector, band, at),
                    t.CurrentFlowSpeed, t.CurrentAccel, t.CurrentSideShare, strength);
                if (ax != 0f || ay != 0f) nugget.LinearVelocity = v + new Vector2(ax, ay) * dt;
            }
        });
    }
}

/// <summary>
/// Spec 221: arrows that drift along the flow over each triangle of the current. GUI only: it draws in the sim overlay pass
/// and reads the clock of the app, never sim state that it changes.
/// </summary>
static class CurrentArrows
{
    static SimRoot? _sim;
    static List<Vector2>? _centers;

    /// <summary>Hooks the overlay of the current sim. Called each frame; it re-hooks after a jump or a load.</summary>
    internal static void Update(Run run)
    {
        var sim = run.Game.Sim;
        if (sim == _sim) return;
        if (_sim is not null) _sim.Overlays.DrawingCustomOverlays -= OnDraw;
        _sim = sim;
        _centers = null;
        sim.Overlays.DrawingCustomOverlays += OnDraw;
    }

    static void OnDraw(object? sender, EventArgs e) => Errors.Guard("current arrows", () =>
    {
        if (_sim is not { } sim || Run.Current is not { } run || run.Game.Sim != sim || !run.T.Worlds.CurrentArrows) return;
        // The nebula does not change after generation, so the triangles are found once per sim.
        _centers ??= Current.TriangleCenters(sim, Current.NebulaId);
        if (_centers.Count == 0) return;
        Vector2 flow = Current.FlowVector(sim);
        // The arrows lean toward the nearer side by the side share, like the drift (WorldsModel.Push).
        Vector2 middle = Vector2.Zero;
        foreach (Vector2 c in _centers) middle += c;
        middle /= _centers.Count;
        float share = run.T.Worlds.CurrentSideShare;
        float spacing = sim.Nebulas.TriangleSize / 3f;
        float phase = (float)(App.Clock.Time.Seconds * 120.0 % spacing);
        Rect view = sim.WorldViewRect.BoundingRect.Inflate(spacing);
        var line = sim.Rules.SimGui.ShipVelocityLine;
        var color = new Color(0.55f, 0.95f, 1f, 0.5f);
        foreach (Vector2 center in _centers)
        {
            if (!view.Contains(center)) continue;
            Vector2 offset = center - middle;
            var (ox, oy) = WorldsModel.Outward(flow.X, flow.Y, offset.X, offset.Y);
            Vector2 drift = (flow + new Vector2(ox, oy) * share).Normalize();
            for (int k = -1; k <= 1; k++)
            {
                Vector2 tip = center + drift * (k * spacing + phase - spacing / 2f);
                Vector2 tail = tip - drift * 260f;
                line.Draw(tail, tip, color, 30f, drawEndCap: false);
                line.Draw(tail, tip, color, 520f, drawLine: false);
            }
        }
    });
}

using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.Random;
using Halfling.Serialization;
using HotCargo.Core;

namespace HotCargo.Events;

/// <summary>A running rescue tow (spec 1127). Saved in EventsState.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class RescueTowState
{
    [Serialize] public ObjectID Towed;
    [Serialize] public ObjectID Tug;
    /// <summary>False while the tug flies to the ship; true once the tow line holds.</summary>
    [Serialize(Optional = true)] public bool Pulling;
    /// <summary>Game seconds at which a leaving tug leaves the sim, or -1 while it tows.</summary>
    [Serialize(Optional = true)] public double LeaveAt = -1;
    /// <summary>The station that the tow objective picked.</summary>
    [Serialize] public Vector2 Destination;
    [Serialize] public float ArrivalRadius;
    /// <summary>True once the rescue mission is in the log. The mission spawner adds it a step after the beacon fires.</summary>
    [Serialize(Optional = true)] public bool InLog;
}

/// <summary>
/// Spec 1127: the Rescue Beacon. Its item action (handler "rescue") starts a rescue tow mission (the tow objective of requirement 88,
/// with the firing ship as the towed ship), takes the fee from the player who fired it, adds heat and spawns a tug. The sim tick
/// flies the tug to the ship, then to the station, and drags the ship on a tow line. Everything runs in the sim, so co-op stays in step.
/// </summary>
public static class RescueBeacon
{
    static readonly ID<SimObjectSpawner> TowedTag = new("hc_rescue_towed");
    public static readonly ID<SimObjectSpawner> TugTag = new("hc_rescue_tug");
    const string MissionSpawn = "RescueMission", TugSpawn = "RescueTug";

    /// <summary>Test only: rescues that reached their station since the game started.</summary>
    internal static int Arrivals;

    static string Text(string key, params object?[] args) => Strings.FormatText("HotCargoItems/" + key, args);

    /// <summary>The fee in credits now: Events.RescueFee event money units at the current tier.</summary>
    public static int Fee(Run run) => EventRunner.Credits(run, run.T.Events.RescueFee, 0f, run.CurrentTier, 0);

    /// <summary>The rescue tow objectives of a ship, in log order.</summary>
    static List<TowObjective> TowsOf(Run run, ObjectID ship)
    {
        var found = new List<TowObjective>();
        foreach (Mission m in run.Mode.Missions) m.GetObjectives(found);
        return found.Where(o => o.Towed.ShipID == ship).ToList();
    }

    internal sealed class Handler : IItemActionHandler
    {
        public ItemActionResult Run(Run run, SimRoot sim, Part part, int playerIndex)
        {
            var ship = part.Ship ?? throw new InvalidOperationException("the beacon's part has no ship");
            var s = run.State.Events;
            var t = run.T.Events;
            string name = ship.Metadata.XmlShipName;
            if (s.Rescues.Any(r => r.Towed == ship.UniqueID && r.LeaveAt < 0)) return new(Text("RescueBusyFmt", name), Used: false);

            // The mission's tow objective finds the firing ship by a tag that only this spawn sees.
            TowObjective.LastCreated = null;
            ship.Tags ??= [];
            bool tagged = ship.Tags.Add(TowedTag);
            try { EventLibrary.Spawn(MissionSpawn).Generate(sim, new FastRand(sim.Rand.UInt64()), loadInstantly: true, rootLoc: ship.DetWorldCenter, addExistingSpawnTags: true); }
            finally { if (tagged) ship.Tags.Remove(TowedTag); }
            if (TowObjective.LastCreated is not { } tow || tow.Towed.ShipID != ship.UniqueID) return new(Text("RescueNoStationFmt", name), Used: false);

            // Too little money: the tug takes what there is and comes anyway. A rescue must not fail for money.
            var player = run.Game.GetPlayerState(playerIndex, deterministic: true);
            int paid = Math.Min(Fee(run), Math.Max(0, player.Money));
            player.AddMoney(-paid);
            float heatBefore = run.State.Heat;
            run.AddHeat(t.RescueHeat * t.HeatScale, sim);
            string heat = (run.State.Heat - heatBefore).ToString("+0;-0;0");

            var tugs = sim.Ships.Where(x => x.HasTag(TugTag)).Select(x => x.UniqueID).ToHashSet();
            EventLibrary.Spawn(TugSpawn).Generate(sim, new FastRand(sim.Rand.UInt64()), loadInstantly: true, rootLoc: ship.DetWorldCenter, addExistingSpawnTags: true);
            var tug = sim.Ships.Where(x => x.HasTag(TugTag) && !tugs.Contains(x.UniqueID)).MinBy(x => x.UniqueID)
                ?? throw new InvalidOperationException($"{TugSpawn} made no live tug");
            s.Rescues.Add(new RescueTowState { Towed = ship.UniqueID, Tug = tug.UniqueID, Destination = tow.Destination, ArrivalRadius = tow.ArrivalRadius });
            Move(tug, ship.DetWorldCenter);
            Log.Info($"rescue: {ship.Metadata.PlainShipName} fired the beacon; player {playerIndex} paid {paid}, heat {heat}, tug {tug.Metadata.PlainShipName}");
            return new(Text("RescueFiredFmt", name, paid.ToString("N0"), heat), Used: true);
        }

        public string? ToolTip(Run run, Part part) =>
            Text("RescueTipFmt", Fee(run).ToString("N0"), (run.T.Events.RescueHeat * run.T.Events.HeatScale).ToString("0.#"));
    }

    static void Move(Ship ship, Vector2 to) =>
        ship.Commands.EnqueueCommand(new MoveCommand(ship, ship.Commands.AllocGlobalCommandID(), to, null, CollisionAvoidanceFlags.All, isTentative: false), append: false, confirmed: true);

    /// <summary>The tug's next point to the station: around the heat zones of stars (RescueModel.Waypoint).</summary>
    static Vector2 Route(SimRoot sim, Vector2 from, Vector2 to, float margin)
    {
        var zones = Worlds.HeatZone.Suns(sim).Select(s => (new System.Numerics.Vector2(s.Center.X, s.Center.Y), s.EdgeRadius)).ToList();
        var p = RescueModel.Waypoint(new(from.X, from.Y), new(to.X, to.Y), zones, margin);
        return new Vector2(p.X, p.Y);
    }

    static float Distance(Ship a, Ship b) => a.DetWorldBoundingCircle.DistanceTo(b.DetWorldBoundingCircle);

    /// <summary>Game seconds between new move orders of a tug, so it follows a ship that drifts and keeps its course.</summary>
    const double OrderSeconds = 2;

    /// <summary>Sim tick (Run.Hooks): orders and the tow line of every rescue, in start order.</summary>
    internal static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("rescue tow tick", () =>
    {
        if (sim != run.Game.Sim) return;
        var s = run.State.Events;
        if (s.Rescues.Count == 0) return;
        var t = run.T.Events;
        double now = run.State.GameSeconds;
        bool order = Math.Floor(now / OrderSeconds) != Math.Floor((now - dt) / OrderSeconds);
        foreach (var r in s.Rescues.ToList())
        {
            var tug = sim.Ships[r.Tug];
            if (r.LeaveAt >= 0)
            {
                if (now < r.LeaveAt && tug is not null) continue;
                if (tug is not null) sim.Ships.Remove(tug, dispose: true, handleLostShip: false);
                s.Rescues.Remove(r);
                continue;
            }
            var towed = sim.Ships[r.Towed];
            var tow = TowsOf(run, r.Towed).LastOrDefault(o => o.Completion == CompletionState.InProgress);
            r.InLog |= tow is not null;
            if (tug is null || tug.IsJunk)
            {
                s.Rescues.Remove(r);
                Log.Info("rescue: the tug is gone");
                if (towed is not null) Say(run.Game, Text("RescueTugLostFmt", towed.Metadata.XmlShipName));
                continue;
            }
            if (towed is null || (tow is null && r.InLog))
            {
                // Done (arrived) or ended (the ship is gone, or the mission failed): the tug flies off and leaves the sim.
                // A completed mission leaves the log at once, so arrival is the tow objective's own rule: within its radius of the station.
                if (towed is not null && towed.DetWorldCenter.DistanceTo(r.Destination) <= r.ArrivalRadius)
                {
                    Arrivals++;
                    Say(run.Game, Text("RescueArrivedFmt", towed.Metadata.XmlShipName));
                }
                r.LeaveAt = now + t.RescueTugLeaveSeconds;
                Move(tug, tug.DetWorldCenter + (tug.DetWorldCenter - (towed?.DetWorldCenter ?? tug.DetWorldCenter) + new Vector2(1f, 0f)).Normalize() * 5000f);
                Log.Info($"rescue: tow of {r.Towed} ended (towed live: {towed is not null}); the tug leaves");
                continue;
            }
            float d = Distance(tug, towed);
            if (!r.Pulling)
            {
                if (d <= t.RescueTowLength)
                {
                    r.Pulling = true;
                    Move(tug, Route(sim, tug.DetWorldCenter, r.Destination, t.RescueSunMargin));
                    Log.Info($"rescue: the tug has {towed.Metadata.PlainShipName} on the line");
                }
                else if (order) Move(tug, towed.DetWorldCenter);
                continue;
            }
            if (order) Move(tug, Route(sim, tug.DetWorldCenter, r.Destination, t.RescueSunMargin));
            // The tow line: past its length, the towed ship takes the tug's velocity plus a pull toward it.
            if (d <= t.RescueTowLength) continue;
            Vector2 dir = (tug.DetWorldCenter - towed.DetWorldCenter).Normalize();
            towed.Physics.Body.LinearVelocity = tug.Physics.Body.LinearVelocity + dir * ((d - t.RescueTowLength) * t.RescueTowPull);
        }
    });

    /// <summary>UI only: every machine shows the same line in the center and in the chat.</summary>
    static void Say(GameRoot game, string xml)
    {
        Ux.UxKit.CenterText(game, xml);
        Ux.UxKit.ChatLine(game, xml);
    }
}

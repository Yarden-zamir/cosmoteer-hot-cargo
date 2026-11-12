using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling;
using Halfling.Geometry;
using HotCargo.Core;

namespace HotCargo.Hunters;

/// <summary>
/// Spawns hunter waves (spec 30-33). Runs inside the sim tick, from Run.Hooks.
/// Hunters are built-in combat ships with the hc_hunter tag, a shared hostile player index and the HotCargoHunter AI.
/// </summary>
public static class HunterSpawner
{
    public const string AiId = "HotCargoHunter";
    public static readonly ID<SimObjectSpawner> HunterTag = new("hc_hunter");

    /// <summary>Waves spawned since game start. For autotest fields only; not sim state.</summary>
    public static int WavesSpawned { get; private set; }

    /// <summary>Live hunters. Wreck pieces keep the tag but belong to the junk player, so they do not count.</summary>
    public static IEnumerable<Ship> Hunters(SimRoot sim) => sim.Ships.Where(s => s.HasTag(HunterTag) && !s.IsJunk);

    // A spawn error must not crash the sim tick. Errors.Guard skips the wave and shows the error.
    internal static void OnStageStarted(Run run, SimRoot sim, int stageIndex) => Errors.Guard("hunter spawn", () =>
        StartStage(run, sim, run.T.Heat.Stages[stageIndex].Name));

    internal static void OnStrikeWave(Run run, SimRoot sim) => Errors.Guard("hunter spawn", () =>
    {
        var stages = run.T.Heat.Stages;
        if (stages.Length == 0) throw new InvalidOperationException("strike wave without heat stages");
        StartStage(run, sim, stages[^1].Name);
    });

    /// <summary>Spec 191. Spawns the groups that are due. Runs inside the sim tick.</summary>
    internal static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("hunter waves", () =>
    {
        DropHeldFuel(run, sim);
        var pending = run.State.Hunters.Pending;
        if (!run.PressureActive) { pending.Clear(); return; }
        while (pending.Count > 0 && pending[0].Due <= run.State.GameSeconds)
        {
            var wave = pending[0];
            pending.RemoveAt(0);
            SpawnWave(run, sim, wave.Count, wave.Tier, wave.Stage, firstOfStage: false);
        }
    });

    /// <summary>Groups that did not arrive stay behind: a jump is how the players escape a strike.</summary>
    internal static void OnSystemEntered(Run run, bool firstVisit)
    {
        var pending = run.State.Hunters.Pending;
        if (pending.Count > 0) Log.Info($"hunters: {pending.Count} waiting groups dropped after the jump");
        pending.Clear();
        // The hunters of the old system stay there, with their held fuel.
        run.State.Hunters.HeldFuel.Clear();
    }

    /// <summary>Spec 198. A hunter call that completes brings the next waiting group now. Returns false when no group waits.</summary>
    public static bool PullNextWave(Run run)
    {
        var pending = run.State.Hunters.Pending;
        if (pending.Count == 0) return false;
        pending[0].Due = Math.Min(pending[0].Due, run.State.GameSeconds);
        return true;
    }

    static StageResponse Response(Run run, string stageName) =>
        run.T.Hunters.Stages.FirstOrDefault(s => s.Name == stageName)
        ?? throw new InvalidOperationException($"Hunters.Stages has no response for heat stage '{stageName}'");

    /// <summary>
    /// Spec 190-191. Scales the stage response to the run progress, spawns the first group now and queues the others.
    /// </summary>
    internal static void StartStage(Run run, SimRoot sim, string stageName)
    {
        if (sim != run.Game.Sim) return;
        var r = Response(run, stageName);
        float progress = WaveModel.Progress(run.State.SectorIndex, run.T.Journey.RunSectorCount);
        int ships = WaveModel.ShipCount(r.ShipCount, r.EarlyScale, progress);
        int tier = run.CurrentTier + WaveModel.TierOffset(r.TierOffset, r.EarlyTierReduction, progress);
        int[] groups = WaveModel.SplitWaves(ships, r.Waves);
        var later = Enumerable.Range(1, groups.Length - 1).Select(i => new PendingWave
        {
            Due = run.State.GameSeconds + i * r.WaveGapSeconds, Stage = stageName, Count = groups[i], Tier = tier,
        });
        // OrderBy is stable, so groups with the same due time keep their order on every client.
        run.State.Hunters.Pending = run.State.Hunters.Pending.Concat(later).OrderBy(w => w.Due).ToList();
        Log.Info($"hunters: stage {stageName} sends {ships} ships of tier {tier} in groups [{string.Join(", ", groups)}]");
        SpawnWave(run, sim, groups[0], tier, stageName, firstOfStage: true);
    }

    /// <summary>Spawns one group near the human fleet. Each ship spawns physically at once, so the AI owns it from the first tick.
    /// Also used by the contract informant sell-out (Contracts/ContractBook.SellOut).</summary>
    internal static void SpawnWave(Run run, SimRoot sim, int wanted, int tier, string stageName, bool firstOfStage)
    {
        string reason = $"stage {stageName}";
        if (sim != run.Game.Sim) return;
        if (sim.Mode is not CareerSimModeManager career) throw new InvalidOperationException("hunters need a career simulation");
        var t = run.T.Hunters;
        if (SimQueries.FleetCenter(sim) is not { } fleetCenter)
        {
            Log.Info($"hunters: {reason} skipped, no human ships in this system");
            return;
        }

        int allegiance = HunterAllegiance(sim, career);
        float waveDirection = sim.Rand.Direction();
        float halfArc = MathF.PI / 180f * t.WaveArcDegrees / 2f;
        // Spec 334: co-op scales the group and the live cap.
        int scaled = Scaling.EnemyScale.Count(wanted, sim);
        int maxLive = Scaling.EnemyScale.Cap(t.MaxLiveHunters, sim);
        int count = Math.Min(scaled, Math.Max(0, maxLive - Hunters(sim).Count()));
        if (count < scaled) Log.Info($"hunters: {reason} capped at {count} of {scaled} ships by MaxLiveHunters {maxLive}");
        for (int i = 0; i < count; i++)
        {
            var ship = PickShip(sim, t, tier);
            Vector2 loc = SpawnLocation(sim, fleetCenter, waveDirection, halfArc, t);
            var spawner = new StasisShipSpawner(sim, ship.ID, loc, SpawnedShipFeatures.None, sim.Rand.UInt64(), [HunterTag])
            {
                AI = new ID<ShipAIRules>(AiId),
                Direction = loc.DirectionTo(in fleetCenter),
                ObjectAllegiance = allegiance,
                OverrideName = t.ShipNamePrefix.Length > 0 ? $"{t.ShipNamePrefix} {ship.File.ShipName}" : null,
            };
            spawner.Spawn(sim);
            var spawned = sim.Ships[spawner.ReservedObjectID]
                ?? throw new InvalidOperationException($"hunter {ship.ID} did not spawn");
            _ = spawned.Renderer.AnimateFtlJumpIn(spawned.WorldFlightDirection);
            int fuel = EnsureFuel(run, spawned, t.MinFuel);
            Log.Info($"hunters: {reason} spawned {spawned.Metadata.PlainShipName} tier {ship.SpawnTier ?? ship.Tier} " +
                     $"(wanted {tier}) at {loc.DistanceTo(in fleetCenter):0} from the fleet, player index {allegiance}, fuel {fuel}");
        }
        WavesSpawned++;
        ShowAlert(run, firstOfStage ? Ux.UxKit.StageText(stageName) : Ux.UxKit.Text("StageGeneric"));
    }

    static int StoredFuel(Ship ship) => ship.Resources.GetResourceTotal(GameApp.Rules.FtlFuelResource, includeStored: true, includeCarried: false);

    /// <summary>Fuel that a hunter carries: in its storage, plus held fuel that drops when it dies (spec 192).</summary>
    public static int Fuel(Run run, Ship ship) =>
        StoredFuel(ship) + run.State.Hunters.HeldFuel.Where(h => h.Ship == ship.UniqueID).Sum(h => h.Amount);

    /// <summary>
    /// Spec 192. Built-in ships spawn with full hyperdrive tanks. A hunter with less fuel gets the rest in any storage that takes hyperium.
    /// Vanilla drop rates apply when it dies: hyperium in a destroyed storage drops at the StorageDestroyed rate, and salvage recovers all of it.
    /// Some built-in ships have no hyperdrive and no storage that takes hyperium. The fuel that does not fit is held in the run state
    /// and drops when the hunter dies (DropHeldFuel). Returns the fuel the hunter carries.
    /// </summary>
    static int EnsureFuel(Run run, Ship ship, int minFuel)
    {
        int have = StoredFuel(ship);
        if (have < minFuel) have += ship.Resources.AddResources(GameApp.Rules.FtlFuelResource, minFuel - have, excludeConstructionSinks: true);
        if (have < minFuel)
            run.State.Hunters.HeldFuel.Add(new HeldFuel { Ship = ship.UniqueID, Amount = minFuel - have, Location = ship.DetWorldCenter });
        return Math.Max(have, minFuel);
    }

    /// <summary>
    /// Spec 192. Drops the held fuel of hunters that died: a derelict hunter drops it at its hull, a hunter destroyed to the last part
    /// at its last location. Same drop roll as a destroyed enemy storage (NuggetDropSource.Storage). A hunter that jumped out
    /// (ForgetHeldFuel) or waits in stasis keeps it. Runs inside the sim tick.
    /// </summary>
    static void DropHeldFuel(Run run, SimRoot sim)
    {
        var held = run.State.Hunters.HeldFuel;
        if (held.Count == 0) return;
        var live = SimQueries.TaggedShips(sim, HunterTag).Select(s => s.Id).ToHashSet();
        for (int i = 0; i < held.Count; i++)
        {
            var h = held[i];
            var ship = sim.Ships[h.Ship];
            if (live.Contains(h.Ship))
            {
                if (ship is not null) h.Location = ship.DetWorldCenter;
                continue;
            }
            Vector2 at = ship?.DetWorldCenter ?? h.Location;
            var rect = new RotRect(at, new Vector2(4f, 4f), Direction.Zero);
            int dropped = sim.Nuggets.DropNuggets(GameApp.Rules.FtlFuelResource, h.Amount, rect, Cosmoteer.Modes.NuggetDropSource.Storage,
                salvageFraction: 0f, destroyed: true, isPlayer: false, sourceShip: ship, inheritVelocity: Vector2.Zero);
            Log.Info($"hunters: dead hunter {h.Ship} dropped {dropped} of {h.Amount} held fuel");
            held.RemoveAt(i--);
        }
    }

    /// <summary>A hunter that jumps out of the system takes its held fuel with it.</summary>
    public static void ForgetHeldFuel(Run run, Ship ship) => run.State.Hunters.HeldFuel.RemoveAll(h => h.Ship == ship.UniqueID);

    /// <summary>
    /// Picks a ship of the wanted tier. If the pool has no ship of that tier, it walks down to the nearest lower tier.
    /// </summary>
    static BuiltinShipRules PickShip(SimRoot sim, HunterTunables t, int tier)
    {
        var db = GameApp.Rules.BuiltinShips;
        ID<FactionRules>[]? factions = t.ShipFactions.Length == 0 ? null : t.ShipFactions.Select(f => new ID<FactionRules>(f)).ToArray();
        var tags = t.ShipTags.Select(s => new ID<BuiltinShipTag>(s)).ToArray();
        for (int wanted = Math.Max(1, tier); wanted >= 1; wanted--)
        {
            var ship = db.GetRandomMatchingShip(sim.Rand, factions, new Range<int>(wanted, wanted), null, tags);
            if (ship is not null) return ship;
        }
        throw new InvalidOperationException(
            $"no built-in ship matches hunter factions [{string.Join(", ", t.ShipFactions)}] and tags [{string.Join(", ", t.ShipTags)}] at tier {tier} or below");
    }

    static Vector2 SpawnLocation(SimRoot sim, Vector2 center, float waveDirection, float halfArc, HunterTunables t)
    {
        var wanted = GameApp.Rules.CareerMode.Wanted;
        var distance = new Range<float>(t.SpawnDistanceMin, Math.Max(t.SpawnDistanceMin, t.SpawnDistanceMax));
        Vector2 loc = center;
        // Same retry rule as police spawns (WantedManager.GetPoliceSpawnLoc): avoid suns, give up after 100 tries.
        for (int tries = 0; tries < 100; tries++)
        {
            float angle = waveDirection + sim.Rand.Single(new Range<float>(-halfArc, halfArc));
            loc = center + Vector2.FromAngle(angle) * sim.Rand.Single(distance);
            if (!sim.ShouldAIAvoidLocation(loc, wanted.PoliceSpawnAvoidableDoodadTags, wanted.PoliceSpawnAvoidableDoodadBuffer)) break;
        }
        return loc;
    }

    /// <summary>
    /// All hunters in one system share one hostile player index, so they are allies of each other across waves.
    /// The index comes from the hunters already in the sim, so it survives save and load without extra state.
    /// </summary>
    static int HunterAllegiance(SimRoot sim, CareerSimModeManager career) =>
        Hunters(sim).FirstOrDefault()?.Metadata.PlayerIndex ?? career.AllocUniquePlayerIndex();

    /// <summary>UI and audio only. It changes no sim state, so it is safe in the sim tick on every client.</summary>
    static void ShowAlert(Run run, string text)
    {
        Ux.UxKit.CenterText(run.Game, text);
        App.Audio.Play(GameApp.Rules.CareerMode.Wanted.WantedAlertSound, AudioGroups.Interface);
    }
}

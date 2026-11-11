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
    {
        string name = run.T.Heat.Stages[stageIndex].Name;
        SpawnWave(run, sim, Response(run, name), name);
    });

    internal static void OnStrikeWave(Run run, SimRoot sim) => Errors.Guard("hunter spawn", () =>
    {
        var stages = run.T.Heat.Stages;
        if (stages.Length == 0) throw new InvalidOperationException("strike wave without heat stages");
        SpawnWave(run, sim, Response(run, stages[^1].Name), stages[^1].Name);
    });


    static StageResponse Response(Run run, string stageName) =>
        run.T.Hunters.Stages.FirstOrDefault(s => s.Name == stageName)
        ?? throw new InvalidOperationException($"Hunters.Stages has no response for heat stage '{stageName}'");

    /// <summary>Spawns one wave near the human fleet. Each ship spawns physically at once, so the AI owns it from the first tick.</summary>
    public static void SpawnWave(Run run, SimRoot sim, StageResponse response, string stageName)
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

        int tier = run.CurrentTier + response.TierOffset;
        int allegiance = HunterAllegiance(sim, career);
        float waveDirection = sim.Rand.Direction();
        float halfArc = MathF.PI / 180f * t.WaveArcDegrees / 2f;
        int count = Math.Min(response.ShipCount, Math.Max(0, t.MaxLiveHunters - Hunters(sim).Count()));
        if (count < response.ShipCount) Log.Info($"hunters: {reason} capped at {count} ships by MaxLiveHunters {t.MaxLiveHunters}");
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
            Log.Info($"hunters: {reason} spawned {spawned.Metadata.PlainShipName} tier {ship.SpawnTier ?? ship.Tier} " +
                     $"(wanted {tier}) at {loc.DistanceTo(in fleetCenter):0} from the fleet, player index {allegiance}");
        }
        WavesSpawned++;
        ShowAlert(run, stageName);
    }

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
    static void ShowAlert(Run run, string stageName)
    {
        run.Game.Gui.ShowAreaName(Ux.UxKit.StageText(stageName));
        App.Audio.Play(GameApp.Rules.CareerMode.Wanted.WantedAlertSound, AudioGroups.Interface);
    }
}

using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Gui;
using Cosmoteer.Modes;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Geometry;
using Halfling.Random;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Scaling;

/// <summary>Entry point of the Scaling area (spec 330 to 336). Mod.Boot calls Register once at startup.</summary>
public static class ScalingArea
{
    static readonly ID<SimObjectSpawner> BountyShipTag = new("bounty_ship");
    static int _fleetShips, _fleetTargets, _tagCount;

    public static void Register()
    {
        Run.Hooks.NewRunStarted += OnNewRun;

        // enemyplayers <n|off>: counts n human players for enemy scaling, in single player. Test only.
        Autotest.Commands["enemyplayers"] = arg => EnemyScale.TestPlayers = arg == "off" ? null : int.Parse(arg, CultureInfo.InvariantCulture);
        // enemyscale <setting>: sets the run's setting, as if chosen on the new-game screen. Test only; use "mp enemyscale" in co-op.
        Autotest.Commands["enemyscale"] = arg =>
        {
            float setting = float.Parse(arg, CultureInfo.InvariantCulture);
            ScalingModel.Snap(setting);
            Autotest.RequireRun().State.EnemyScale = setting;
        };
        // enemysetting <setting>: the saved slider value, before the new-game screen opens (mphost, newgameui). Test only.
        Autotest.Commands["enemysetting"] = arg => App.Preferences.Set(NewGameUi.PreferenceKey, (int)Math.Round(float.Parse(arg, CultureInfo.InvariantCulture) * 10f));
        // newgameui: starts a career through the single-player new career screen, like its start button. Test only.
        Autotest.Commands["newgameui"] = _ =>
        {
            var gui = new CareerSPModeConfigGui();
            App.Director.PushState(gui.CreateGame());
            Autotest.Wait(5);
        };
        // newcareerscreen [notips]: opens the single-player new career screen, for a screenshot. "notips" hides the tooltip box,
        // which the resting cursor otherwise opens over the slider row. Test only.
        Autotest.Commands["newcareerscreen"] = arg =>
        {
            var mode = SPModes.ModesInOrder.Single(m => m.ConfigGuiType == typeof(CareerSPModeConfigGui));
            var dialog = new NewGameConfigDialog(new NewGameDialog(showFakeBackground: false), mode, showFakeBackground: false);
            if (arg == "notips") dialog.ToolTipDisplay = null;
            foreach (var open in App.Director.States.OfType<NewGameConfigDialog>().ToList()) App.Director.PopState(open);
            App.Director.PushState(dialog);
            Autotest.Wait(2);
        };
        // scalebounty fleet|single: spawns the vanilla fleet bounty (tier 3 fleet: 2 ships) or single bounty next to the fleet,
        // with its mission, and counts the ships and the mission targets. Test only.
        Autotest.Commands["scalebounty"] = arg => SpawnBounty(Autotest.RequireRun(), arg switch
        {
            "fleet" => true,
            "single" => false,
            _ => throw new InvalidOperationException("scalebounty needs: fleet | single"),
        });
        // counttag <tag>: counts the ships with the spawn tag, live and in stasis, into the field tagcount. Test only.
        Autotest.Commands["counttag"] = arg =>
        {
            _tagCount = SimQueries.TaggedShips(Game().Sim, new ID<SimObjectSpawner>(arg)).Count;
            Log.Info($"AUTOTEST {_tagCount} ships tagged {arg}");
        };

        Autotest.Fields["enemysetting"] = () => EnemyScale.Setting(Game());
        Autotest.Fields["enemyfactor"] = () => EnemyScale.Factor(Game().Sim);
        Autotest.Fields["fleetships"] = () => _fleetShips;
        Autotest.Fields["fleettargets"] = () => _fleetTargets;
        Autotest.Fields["tagcount"] = () => _tagCount;
        // 1 when the game holds a vanilla CareerInit, so a save does not need the mod type for the init.
        Autotest.Fields["vanillainit"] = () => Autotest.RequireRun().Mode.Init.GetType() == typeof(CareerInit) ? 1 : 0;
    }

    static Cosmoteer.Game.GameRoot Game() => Autotest.Game ?? throw new InvalidOperationException("no game");

    /// <summary>Takes the new-game setting into the run state and puts a vanilla init back, so the save needs no mod type for it.</summary>
    static void OnNewRun(Run run)
    {
        if (run.Mode.Init is HotCargoCareerInit init)
        {
            run.State.EnemyScale = init.EnemyScale;
            ((GameModeManager)run.Mode).Init = init.ToPlain();
        }
        Log.Info($"scaling: enemy setting {run.State.EnemyScale:0.0}, {EnemyScale.Players(run.Game)} players, factor {EnemyScale.Factor(run.Game.Sim):0.00}");
    }

    /// <summary>
    /// Runs a vanilla bounty spawner of the sector generator at tier 3, 2,500 from the fleet. The fleet bounty remaps tier 3 to
    /// a fleet [1, 1]. The system's tier field would give a single ship in the first system, so the test fixes the tier for this spawn.
    /// </summary>
    static void SpawnBounty(Run run, bool fleetBounty)
    {
        var sim = run.Game.Sim;
        var spawner = Missions.MissionArea.AllSpawners(((CareerSimModeManager)sim.Mode).SectorType.SystemGenerator.Spawners)
            .OfType<ShipSpawner>().Distinct().SingleOrDefault(s => s.Tags?.Contains(BountyShipTag) == true && (s.Criteria?.FleetTierRemapTable is not null) == fleetBounty)
            ?? throw new InvalidOperationException($"no vanilla {(fleetBounty ? "fleet" : "single")} bounty spawner in the sector generator");
        var criteria = spawner.Criteria!;
        var objects = Missions.MissionArea.ExistingSpawnTags(sim);
        int before = objects.TryGetValue(BountyShipTag, out var old) ? old.Count : 0;
        Vector2 at = (SimQueries.FleetCenter(sim) ?? throw new InvalidOperationException("no human ships")) + new Vector2(2500f, 0f);
        var parentTree = new List<SimSpawner.SpawnedObject> { new(at) };
        uint batch = 0, fleet = 0;
        (bool UseTierField, Range<int>? Tier) saved = (criteria.UseTierField, criteria.Tier);
        try
        {
            criteria.UseTierField = false;
            criteria.Tier = new Range<int>(3, 3);
            spawner.Spawn(sim, new FastRand(777UL), parentTree, 0, ref batch, ref fleet, loadInstantly: true, [], objects, null, 0f);
        }
        finally
        {
            (criteria.UseTierField, criteria.Tier) = saved;
        }
        var ships = objects[BountyShipTag].Skip(before).Select(o => o.UniqueID).ToHashSet();
        _fleetShips = ships.Count;
        var defeat = new List<DefeatShipsObjective>();
        foreach (var m in Missions.MissionArea.Offers(sim)) m.GetObjectives(defeat);
        var mission = defeat.FirstOrDefault(d => d.TargetShips.Any(t => ships.Contains(t.ShipID)))
            ?? throw new InvalidOperationException("the bounty mission does not target the spawned ships");
        _fleetTargets = mission.TargetShips.Count(t => ships.Contains(t.ShipID));
        Log.Info($"AUTOTEST {(fleetBounty ? "fleet" : "single")} bounty: {_fleetShips} ships, {_fleetTargets} mission targets, factor {EnemyScale.Factor(sim):0.00}");
    }
}

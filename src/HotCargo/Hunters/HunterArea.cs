using System.Globalization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Hunters;

/// <summary>Entry point of the Hunters area. Mod.Boot calls Register once at startup, before the game starts.</summary>
public static class HunterArea
{
    static double _markedDistance;

    public static void Register()
    {
        Run.Hooks.StageStarted += HunterSpawner.OnStageStarted;
        Run.Hooks.StrikeWave += HunterSpawner.OnStrikeWave;
        Run.Hooks.Tick += HunterSpawner.OnTick;
        Run.Hooks.SystemEntered += HunterSpawner.OnSystemEntered;
        // Hunter groups that wait to arrive in this system (spec 191).
        Autotest.Fields["hunterspending"] = () => Autotest.RequireRun().State.Hunters.Pending.Count;
        // Least FTL fuel on any live hunter (spec 192). -1 without hunters.
        Autotest.Fields["hunterfuelmin"] = () => HunterSpawner.Hunters(Sim()).Select(h => HunterSpawner.Fuel(Autotest.RequireRun(), h)).DefaultIfEmpty(-1).Min();

        Autotest.Fields["hunters"] = () => HunterSpawner.Hunters(Sim()).Count();
        Autotest.Fields["hunterdistmin"] = () => Distances().DefaultIfEmpty(double.NaN).Min();
        Autotest.Fields["hunterdistmax"] = () => Distances().DefaultIfEmpty(double.NaN).Max();
        Autotest.Fields["hunterdist"] = () => Distances().DefaultIfEmpty(double.NaN).Average();
        // Mean hunter distance now, minus the distance at the last "markhunters". Negative means the hunters closed in.
        Autotest.Fields["hunterdistdelta"] = () => Distances().DefaultIfEmpty(double.NaN).Average() - _markedDistance;
        Autotest.Fields["huntertiermin"] = () => Tiers().DefaultIfEmpty(-1).Min();
        Autotest.Fields["huntertiermax"] = () => Tiers().DefaultIfEmpty(-1).Max();
        // Number of player indices among hunters. 1 means all hunters are allies.
        Autotest.Fields["hunterindices"] = () => HunterSpawner.Hunters(Sim()).Select(h => h.Metadata.PlayerIndex).Distinct().Count();
        // 1 when every hunter is an enemy of every human ship.
        Autotest.Fields["huntershostile"] = () =>
        {
            var sim = Sim();
            var humans = sim.Ships.HumanPlayerSignificantShips.ToList();
            return humans.Count > 0 && HunterSpawner.Hunters(sim).All(h => humans.All(h.IsEnemiesWith)) ? 1 : 0;
        };
        Autotest.Fields["huntersattacking"] = () =>
            HunterSpawner.Hunters(Sim()).Count(h => h.Commands.CurConfirmedCommand is Cosmoteer.Ships.Commands.AttackCommand);

        Autotest.Fields["hunterwaves"] = () => HunterSpawner.WavesSpawned;
        // strikeinterval <seconds>: sets Hunters.StrikeWaveIntervalSeconds for this run. Test only.
        // maxhunters <count>: sets Hunters.MaxLiveHunters for this run. Test only.
        Autotest.Commands["maxhunters"] = arg => Autotest.RequireRun().T.Hunters.MaxLiveHunters = int.Parse(arg, CultureInfo.InvariantCulture);
        Autotest.Commands["strikeinterval"] = arg => Autotest.RequireRun().T.Hunters.StrikeWaveIntervalSeconds = float.Parse(arg, CultureInfo.InvariantCulture);
        Autotest.Commands["markhunters"] = _ =>
        {
            _markedDistance = Distances().DefaultIfEmpty(double.NaN).Average();
            Log.Info($"AUTOTEST hunters marked at mean distance {_markedDistance:0}");
        };
        Autotest.Commands["hunters"] = arg =>
        {
            var sim = Sim();
            var career = (CareerSimModeManager)sim.Mode;
            Vector2 center = FleetCenter(sim);
            foreach (var h in HunterSpawner.Hunters(sim))
                Log.Info($"AUTOTEST HUNTER {arg}: {h.Metadata.PlainShipName} id={h.UniqueID} tier={career.GetShipTier(h)} player={h.Metadata.PlayerIndex} " +
                         $"dist={h.DetWorldCenter.DistanceTo(in center):0} health={HunterOrders.HealthFraction(h):0.00} fuel={HunterSpawner.Fuel(Autotest.RequireRun(), h)} ai={h.AI?.Rules.ID} cmd={h.Commands.CurConfirmedCommand?.GetType().Name}");
        };
        // damagehunters <fraction>: sets every hunter part to this fraction of its health, without destroying parts. Test only.
        Autotest.Commands["damagehunters"] = arg =>
        {
            float fraction = float.Parse(arg, CultureInfo.InvariantCulture);
            foreach (var h in HunterSpawner.Hunters(Sim()).ToList())
                foreach (var part in h.Parts.ToList())
                    part.Health = Math.Max(1, (int)(part.Rules.MaxHealth * fraction));
        };
    }

    static SimRoot Sim() => Autotest.RequireRun().Game.Sim;

    static Vector2 FleetCenter(SimRoot sim) => SimQueries.FleetCenter(sim) ?? throw new InvalidOperationException("no human ships");

    static IEnumerable<double> Distances()
    {
        var sim = Sim();
        Vector2 center = FleetCenter(sim);
        return HunterSpawner.Hunters(sim).Select(h => (double)h.DetWorldCenter.DistanceTo(in center)).ToList();
    }

    static IEnumerable<int> Tiers()
    {
        var sim = Sim();
        var career = (CareerSimModeManager)sim.Mode;
        return HunterSpawner.Hunters(sim).Select(h => career.GetShipTier(h) ?? -1).ToList();
    }
}

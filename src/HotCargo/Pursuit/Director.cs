using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Hunters;

namespace HotCargo.Pursuit;

/// <summary>Spec 900 to 904: hunter waves come weaker and less often early, and the fleet value moves their strength a little.</summary>
public static class Director
{
    static float Progress(Run run) => WaveModel.Progress(run.State.SectorIndex, run.T.Journey.RunSectorCount);

    /// <summary>Spec 901: the ramp factor on hunter ships for the sector index.</summary>
    public static float RampFactor(Run run) => PursuitModel.Lerp(run.T.Pursuit.EarlyShipFactor, run.T.Pursuit.LateShipFactor, Progress(run));

    /// <summary>Spec 901: the factor on the repeat Strike wave interval.</summary>
    public static float IntervalFactor(Run run) =>
        run.T.Pursuit.Enabled ? PursuitModel.Lerp(run.T.Pursuit.EarlyIntervalFactor, run.T.Pursuit.LateIntervalFactor, Progress(run)) : 1f;

    /// <summary>
    /// Spec 902: the value of the human fleet in this system per human player (vanilla BasePhysicalValue, so damage counts), against
    /// FleetReferenceShare × the ship value maximum of the hunter tier. Co-op already scales the count (spec 334), so the value is per player.
    /// </summary>
    public static float FleetFactor(Run run, SimRoot sim)
    {
        var t = run.T.Pursuit;
        long value = 0;
        foreach (var s in sim.Ships.HumanPlayerSignificantShips) value += s.BasePhysicalValue;
        int players = Math.Max(1, Scaling.EnemyScale.Players(run.Game));
        double reference = GameData.TierValueMaximum(HunterSpawner.BaseTier(run)) * (double)t.FleetReferenceShare;
        return PursuitModel.FleetFactor(value / (double)players, reference, t.FleetFactorPerDoubling, t.FleetFactorMax);
    }

    /// <summary>Spec 901, 902: hunter ships of a stage response × ramp × fleet factor. Sim logic: it rolls sim.Rand for a fraction.</summary>
    internal static int ScaleShips(Run run, SimRoot sim, int ships)
    {
        if (!run.T.Pursuit.Enabled) return ships;
        float ramp = RampFactor(run), fleet = FleetFactor(run, sim);
        int scaled = PursuitModel.ScaleCount(ships, ramp * fleet, sim.Rand.Double);
        Log.Info($"pursuit: hunter ships {ships} x ramp {ramp:0.00} x fleet {fleet:0.00} = {scaled}");
        return scaled;
    }
}

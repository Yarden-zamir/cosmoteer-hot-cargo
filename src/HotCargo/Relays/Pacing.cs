using HotCargo.Core;

namespace HotCargo.Relays;

/// <summary>
/// Spec 951: the pacing of big events. The pursuit director (branch feat/pursuit, spec 42) owns this when it lands on main:
/// set <see cref="Pacing.Current"/> to its pacer. Both calls run in sim code only.
/// </summary>
public interface IBigEventPacer
{
    /// <summary>A big event of this kind may start now.</summary>
    bool MayStart(Run run, string kind);
    /// <summary>A big event of this kind started.</summary>
    void Started(Run run, string kind);
}

public static class Pacing
{
    /// <summary>Limit: a local rule (Relays.MaxPerRun and MinGapSectors) that knows only relay lockdowns. Replace it with the pursuit director's pacer when that is on main.</summary>
    public static IBigEventPacer Current = new LocalPacer();

    sealed class LocalPacer : IBigEventPacer
    {
        public bool MayStart(Run run, string kind)
        {
            var t = run.T.Relays;
            var s = run.State.Relays;
            return RelaysModel.MayStart(run.State.SectorIndex, t.MinSector, s.Started, t.MaxPerRun, s.LastSector, t.MinGapSectors);
        }

        public void Started(Run run, string kind)
        {
            run.State.Relays.Started++;
            run.State.Relays.LastSector = run.State.SectorIndex;
        }
    }
}

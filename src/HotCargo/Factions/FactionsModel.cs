namespace HotCargo.Factions;

/// <summary>The relation of the controlling faction of a system to the players, for heat (spec 641, 643).</summary>
public enum Zone { Neutral, Ally, Shelter }

/// <summary>
/// The kind of system switch that runs now (spec 650). View is a switch to a system where own ships wait, with no jump.
/// RelayWatched: a relay jump with hunters near that relay at the jump start.
/// </summary>
public enum JumpKind { View, RelayWatched, RelayClear, Blind }

/// <summary>Pure math of the Factions area. No game types, so unit tests run on the Mac without the game.</summary>
public static class FactionsModel
{
    public const float MaxHeat = 100f;

    /// <summary>Heat rounded down to whole steps. A price then holds between steps, so a trade input in flight sees the price of the click.</summary>
    public static float SteppedHeat(float heat, float step) => step <= 0 ? heat : MathF.Floor(heat / step) * step;

    /// <summary>Spec 655: 1 below startHeat, then a straight line to maxFactor at heat 100, on stepped heat.</summary>
    public static float MarkupFactor(float heat, float startHeat, float maxFactor, float step)
    {
        float h = SteppedHeat(heat, step);
        if (h <= startHeat) return 1f;
        return 1f + (maxFactor - 1f) * Math.Clamp((h - startHeat) / (MaxHeat - startHeat), 0f, 1f);
    }

    /// <summary>Spec 656: a straight line from 1 at heat 0 to factorAtMax at heat 100, on stepped heat.</summary>
    public static float CrewFactor(float heat, float factorAtMax, float step) =>
        1f + (factorAtMax - 1f) * Math.Clamp(SteppedHeat(heat, step) / MaxHeat, 0f, 1f);

    public static float TimeHeatFactor(Zone zone, float allyFactor, float shelterFactor) => zone switch
    {
        Zone.Ally => allyFactor,
        Zone.Shelter => shelterFactor,
        _ => 1f,
    };

    /// <summary>
    /// A price times a factor, rounded up, so a markup never rounds a price down. The product is first rounded to 3 decimals,
    /// so float noise in the factor (1.6f is 1.60000002) does not round 10 × 1.6 up to 17.
    /// </summary>
    public static int Scale(int price, float factor) => (int)Math.Clamp(Math.Ceiling(Math.Round(price * (double)factor, 3)), 0, int.MaxValue);

    /// <summary>Spec 640: two different faction indexes from a seed. Count must be 2 or more.</summary>
    public static (int Ally, int Rival) PickRoles(ulong seed, int count)
    {
        if (count < 2) throw new ArgumentOutOfRangeException(nameof(count), "needs 2 or more factions");
        int ally = (int)(seed % (ulong)count);
        int offset = 1 + (int)(seed / (ulong)count % (ulong)(count - 1));
        return (ally, (ally + offset) % count);
    }

    /// <summary>Spec 657: a station refuses when heat is at refuseHeat or more and its roll (0 to 1) is below the chance.</summary>
    public static bool Refuses(float heat, float refuseHeat, float chance, float roll) => heat >= refuseHeat && roll < chance;

    /// <summary>Spec 650: the share of heat that stays after a jump that removes the cut share.</summary>
    public static float HeatKept(float cut) => Math.Clamp(1f - cut, 0f, 1f);

    /// <summary>
    /// Spec 651: a blind spin-up is done when the spin time reaches factor × the time at which the drives were full (the vanilla spin-up).
    /// Null fullAt: the drives are not full yet.
    /// </summary>
    public static bool BlindSpinDone(float spinSeconds, float? fullAtSeconds, float factor) => fullAtSeconds is { } at && spinSeconds >= at * factor;

    /// <summary>Spec 651: the readiness bar of a blind spin-up, 0 to 1. Before the drives are full it is the vanilla progress ÷ factor.</summary>
    public static float BlindSpinProgress(float vanillaProgress, float spinSeconds, float? fullAtSeconds, float factor) => fullAtSeconds is { } at
        ? Math.Clamp(spinSeconds / Math.Max(1e-6f, at * factor), 0f, 1f)
        : Math.Clamp(vanillaProgress / factor, 0f, 1f);
}

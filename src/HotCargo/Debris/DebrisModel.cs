namespace HotCargo.Debris;

public enum DebrisPhase { Waiting, Glowing, Burning }

/// <summary>Pure rules of the debris decay (spec 1242, 1243). No game types, so the unit tests cover them.</summary>
public static class DebrisModel
{
    /// <summary>The phase of a junk piece that could decay for ageSeconds: it waits, glows for the last glowSeconds, then burns up.</summary>
    public static DebrisPhase Phase(double ageSeconds, double decaySeconds, double glowSeconds)
    {
        if (ageSeconds >= decaySeconds) return DebrisPhase.Burning;
        return ageSeconds >= decaySeconds - glowSeconds ? DebrisPhase.Glowing : DebrisPhase.Waiting;
    }

    /// <summary>Glow strength from 0 (the glow starts) to 1 (the burn-up), for the outline color. 0 outside the glow.</summary>
    public static float GlowLevel(double ageSeconds, double decaySeconds, double glowSeconds)
    {
        if (glowSeconds <= 0 || Phase(ageSeconds, decaySeconds, glowSeconds) != DebrisPhase.Glowing) return 0f;
        return (float)Math.Clamp((ageSeconds - (decaySeconds - glowSeconds)) / glowSeconds, 0, 1);
    }

    /// <summary>Sim ticks between two checks: at least 1, so a very short check time checks every tick.</summary>
    public static int CheckTicks(double checkSeconds, double tickSeconds) =>
        tickSeconds <= 0 ? 1 : Math.Max(1, (int)Math.Round(checkSeconds / tickSeconds));
}

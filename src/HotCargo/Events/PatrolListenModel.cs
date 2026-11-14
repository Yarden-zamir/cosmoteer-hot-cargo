namespace HotCargo.Events;

/// <summary>Pure rules of the patrol listen (spec 1122, 1123).</summary>
public static class PatrolListenModel
{
    /// <summary>Success chance: linear from atNoHeat at heat 0 to atMaxHeat at maxHeat. Heat outside 0 to maxHeat counts as the end.</summary>
    public static float SuccessChance(float heat, float maxHeat, float atNoHeat, float atMaxHeat)
    {
        if (maxHeat <= 0) throw new ArgumentOutOfRangeException(nameof(maxHeat), maxHeat, "must be above 0");
        float t = Math.Clamp(heat / maxHeat, 0f, 1f);
        return Math.Clamp(atNoHeat + (atMaxHeat - atNoHeat) * t, 0f, 1f);
    }

    /// <summary>Progress from 0 to 1 of a listen that started at start and lasts seconds.</summary>
    public static float Progress(double now, double start, float seconds) =>
        seconds <= 0 ? 1f : Math.Clamp((float)((now - start) / seconds), 0f, 1f);

    /// <summary>True when the last shot is within the window. A ship that never fired has a last shot time of 0.</summary>
    public static bool FiredRecently(double now, double lastShot, float windowSeconds) =>
        lastShot > 0 && now - lastShot <= windowSeconds;
}

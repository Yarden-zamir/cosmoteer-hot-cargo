namespace HotCargo.MultiChoice;

/// <summary>Pure math of multiselect pages (spec 590 and later). No game types, so the unit tests run on the Mac.</summary>
public static class MultiChoiceModel
{
    /// <summary>The chances of the selected options add up, capped at <paramref name="cap"/> and kept in 0..1.</summary>
    public static float Combine(IEnumerable<float> chances, float cap)
    {
        if (float.IsNaN(cap)) throw new ArgumentOutOfRangeException(nameof(cap));
        float sum = 0f;
        foreach (float c in chances)
        {
            if (float.IsNaN(c)) throw new ArgumentOutOfRangeException(nameof(chances), "a chance is NaN");
            sum += c;
        }
        return Math.Clamp(Math.Min(sum, cap), 0f, 1f);
    }

    /// <summary>A slider option's chance: 0 at <paramref name="min"/>, <paramref name="atMax"/> at <paramref name="max"/>, linear between.</summary>
    public static float Scaled(int value, int min, int max, float atMax)
    {
        if (max < min) throw new ArgumentOutOfRangeException(nameof(max));
        if (max == min) return value >= max ? atMax : 0f;
        return atMax * Math.Clamp((value - min) / (float)(max - min), 0f, 1f);
    }

    /// <summary>The value kept in min..max on the step grid, like the slider shows it.</summary>
    public static int Snap(int value, int min, int max, int step)
    {
        if (max < min || step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
        int v = Math.Clamp(value, min, max);
        return Math.Min(max, min + (v - min + step / 2) / step * step);
    }
}

namespace HotCargo.Dev;

/// <summary>Pure rules of the invariant checker (spec 992, 998). No game types, so unit tests cover them.</summary>
public static class InvariantsModel
{
    /// <summary>
    /// Damage steps that a health drop passes: the thresholds max − k × step (k ≥ 1, above 0) with newHealth ≤ threshold &lt; oldHealth.
    /// It counts thresholds one by one on purpose, apart from TechModel.UnitsLostToDamage, so a broken formula there shows as a broken rule.
    /// </summary>
    public static int StepsCrossed(int maxHealth, int oldHealth, int newHealth, float step)
    {
        if (step <= 0f) throw new ArgumentOutOfRangeException(nameof(step), "must be above 0");
        int crossed = 0;
        for (int k = 1; maxHealth - k * step > 0f; k++)
        {
            float threshold = maxHealth - k * step;
            if (threshold >= newHealth && threshold < oldHealth) crossed++;
        }
        return crossed;
    }

    /// <summary>Markup that shows as text: a tag in a plain renderer, an escaped tag in an XML renderer, or a {placeholder}. Null when clean.</summary>
    public static string? TextProblem(string text, bool xml)
    {
        if (!xml && HasTag(text, "<", ">")) return "raw markup in a plain text";
        if (xml && HasTag(text, "&lt;", "&gt;")) return "escaped markup in an XML text";
        for (int open = text.IndexOf('{'); open >= 0; open = text.IndexOf('{', open + 1))
        {
            int close = text.IndexOf('}', open + 1);
            if (close < 0) break;
            string inside = text[(open + 1)..close];
            if (inside.Length > 0 && inside.All(c => char.IsLetterOrDigit(c) || c == '_')) return "unfilled placeholder";
        }
        return null;
    }

    /// <summary>A tag start (a letter or '/' right after the opener) with the closer after it.</summary>
    static bool HasTag(string text, string opener, string closer)
    {
        for (int i = text.IndexOf(opener, StringComparison.Ordinal); i >= 0; i = text.IndexOf(opener, i + 1, StringComparison.Ordinal))
        {
            int next = i + opener.Length;
            if (next < text.Length && (char.IsLetter(text[next]) || text[next] == '/') && text.IndexOf(closer, next, StringComparison.Ordinal) > 0) return true;
        }
        return false;
    }
}

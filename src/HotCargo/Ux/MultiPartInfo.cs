using Cosmoteer.Game.Gui;
using Cosmoteer.Ships.Parts;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using HarmonyLib;

namespace HotCargo.Ux;

/// <summary>
/// V10. With several parts selected, vanilla hides the part info box (PartsCard.UpdateForMultipleParts, each frame).
/// This shows "3 × Laser" or "12 parts", the average health as a bar, and how many are damaged.
/// </summary>
static class MultiPartInfo
{
    /// <summary>Times the info box showed several parts. For autotests.</summary>
    internal static int Shown;

    /// <summary>The health text of the newest parts card info. For autotests.</summary>
    internal static Halfling.Gui.Label? HealthLabel;

    static bool Active(PartsCard card) => card.Sim.PlayerInput.SelectedParts.Count > 1 && UxKit.On(s => s.MultiPartInfo);

    /// <summary>
    /// Patches the per-frame update, not UpdateForMultipleParts: that method is one line, so the JIT inlines it into this caller
    /// and a patch on it stops running once the caller is recompiled. The update calls UpdateForMultipleParts when no single part is selected.
    /// </summary>
    [HarmonyPatch(typeof(PartsCard), nameof(PartsCard.OnUpdatingUIState))]
    static class Info
    {
        static void Postfix(PartsCard __instance)
        {
            if (!Active(__instance) || __instance.Sim.PlayerInput.GetSingleSelectedPart() is not null) return;
            UxKit.Guard("multi part info", () =>
            {
                var parts = __instance.Sim.PlayerInput.SelectedParts;
                var first = parts.First().Rules;
                bool sameType = parts.All(p => p.Rules == first);
                (long health, long max, int damaged) = Totals(parts);
                __instance._infoBox.SelfActive = true;
                Shown++;
                HealthLabel = __instance._partHealth;
                __instance._partName.Text = sameType ? UxKit.Format("PartsSameFmt", parts.Count, first.NameKey.Text) : UxKit.Format("PartsMixedFmt", parts.Count);
                // The vanilla health label has no markup, so it does not parse tags. The plain "{0} / {1}" text reads the same with them on.
                __instance._partHealth.TextRenderer.XmlFormatting = true;
                __instance._partHealth.Text = UxKit.Format("PartsHealthFmt", health.ToString("N0"), max.ToString("N0"), damaged);
            });
        }
    }

    /// <summary>The vanilla bar reads the single selected part. With several parts, it draws their summed health instead.</summary>
    [HarmonyPatch(typeof(PartsCard), nameof(PartsCard.OnDrawHealthBar))]
    static class Bar
    {
        static bool Prefix(PartsCard __instance, Rect uiScreenRect, Color colorScale)
        {
            if (!Active(__instance)) return true;
            (long health, long max, _) = Totals(__instance.Sim.PlayerInput.SelectedParts);
            float damage = max <= 0 ? 0f : 1f - (float)health / max;
            var r = uiScreenRect;
            r.Width *= 1f - damage;
            var color = new Color(Mathx.InverseLerp(0f, 0.5f, damage), Mathx.InverseLerp(1f, 0.5f, damage), 0f) * colorScale;
            App.Graphics.SetToMaterial(null);
            App.Graphics.Draw<Quad>(new Quad(r, color));
            return false;
        }
    }

    static (long Health, long Max, int Damaged) Totals(IEnumerable<Part> parts)
    {
        long health = 0, max = 0;
        int damaged = 0;
        foreach (var p in parts)
        {
            health += p.Health;
            max += p.Rules.MaxHealth;
            if (p.Health < p.Rules.MaxHealth) damaged++;
        }
        return (health, max, damaged);
    }
}

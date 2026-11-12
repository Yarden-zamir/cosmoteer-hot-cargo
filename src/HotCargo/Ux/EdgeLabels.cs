using Cosmoteer.Game.Gui;
using Halfling.Graphics.Text;
using HarmonyLib;

namespace HotCargo.Ux;

/// <summary>
/// V18. An off-screen blip at the left or right screen edge centers its label on the blip, so half of a long label
/// (a ship name, "CALLING HELP 30%") runs off the screen. This aligns the label toward the screen near those edges.
/// </summary>
[HarmonyPatch(typeof(ObjectIndicator), nameof(ObjectIndicator.Update))]
static class EdgeLabels
{
    static void Postfix(ObjectIndicator __instance, Halfling.Geometry.Rect guiBounds, bool isOffScreen)
    {
        if (!UxKit.On(s => s.EdgeLabels)) return;
        UxKit.Guard("edge labels", () =>
        {
            var r = __instance.ScreenRect;
            var align = !isOffScreen ? HAlignment.Center
                : r.Right > guiBounds.Right - 2f * r.Width ? HAlignment.Right
                : r.Left < guiBounds.Left + 2f * r.Width ? HAlignment.Left
                : HAlignment.Center;
            __instance._normalText.HAlignment = align;
            __instance._highlightedText.HAlignment = align;
        });
    }
}

using System.Runtime.CompilerServices;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Modes;
using Cosmoteer.Ships;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Rects;
using HarmonyLib;

namespace HotCargo.Ux;

/// <summary>
/// V1. Hull integrity: the health of all parts divided by the health the ship had with every part whole,
/// destroyed parts included. Vanilla shows damage only as red ghosts on ship icons and as a bar for one selected part.
/// </summary>
static class ShipIntegrity
{
    sealed class Cached { public double Time = double.MinValue; public float Value = 1f; }
    static readonly ConditionalWeakTable<Ship, Cached> Cache = new();

    /// <summary>Integrity 0..1, recomputed at most four times a second for each ship. GUI thread only.</summary>
    public static float Of(Ship ship)
    {
        var c = Cache.GetOrCreateValue(ship);
        double now = App.Clock.Time.Seconds;
        if (now - c.Time < 0.25) return c.Value;
        c.Time = now;
        long health = 0, max = 0;
        foreach (var part in ship.Parts) { health += part.Health; max += part.Rules.MaxHealth; }
        foreach (var part in ship.DestroyedParts) max += part.Rules.MaxHealth;
        c.Value = max <= 0 ? 1f : Math.Clamp((float)health / max, 0f, 1f);
        return c.Value;
    }

    /// <summary>Shown below this integrity only, so that whole ships do not get extra text.</summary>
    const float ShowBelow = 0.995f;

    /// <summary>A "hull" box first in the info row of the ships card, for one selected ship.</summary>
    [HarmonyPatch(typeof(ShipsCard), MethodType.Constructor, typeof(GameRoot), typeof(GameGui), typeof(IRectProvider))]
    static class CardBox
    {
        internal static readonly ConditionalWeakTable<ShipsCard, Tuple<SimpleBox, Label>> Boxes = new();

        static void Postfix(ShipsCard __instance) => UxKit.Guard("integrity box", () =>
        {
            var box = new SimpleBox { Width = 48f };
            box.NineSlice.Flags = NineSliceFlags.None;
            box.ToolTips.TextProvider = new LiveText(() => UxKit.Text("HullTip"));
            var value = new Label { Rect = new Rect(0f, 4f, 48f, 30f) };
            value.TextRenderer.Font = WidgetRules.Instance.ShadowFont;
            value.TextRenderer.FontSize = 20;
            value.TextRenderer.XmlFormatting = true;
            value.TextRenderer.HAlignment = HAlignment.Center;
            value.TextRenderer.VAlignment = VAlignment.Center;
            value.TextRenderer.OversizeMode = OversizeMode.None;
            value.TextRenderer.Clip = false;
            box.AddChild(value);
            var caption = new Label { AnchorRect = AnchorPresets.Fill, Top = 38f, Text = UxKit.Text("Hull") };
            caption.TextRenderer.HAlignment = HAlignment.Center;
            caption.TextRenderer.VAlignment = VAlignment.Top;
            box.AddChild(caption);
            __instance._infoIconsBox.Children.Insert(0, box);
            Boxes.AddOrUpdate(__instance, Tuple.Create(box, value));
        });
    }

    [HarmonyPatch(typeof(ShipsCard), nameof(ShipsCard.UpdateForSingleShip))]
    static class CardUpdate
    {
        static void Postfix(ShipsCard __instance, Ship ship)
        {
            if (!CardBox.Boxes.TryGetValue(__instance, out var b)) return;
            b.Item1.SelfActive = UxKit.On(s => s.ShipIntegrity) && ship.IsViewableByLocalPlayer;
            if (b.Item1.SelfActive) b.Item2.Text = UxKit.PercentXml(Of(ship));
        }
    }

    /// <summary>"Hull 63%" in the hover text of any damaged ship in sight, enemies too.</summary>
    [HarmonyPatch(typeof(SimModeManager), nameof(SimModeManager.GetShipMouseOverText))]
    static class HoverText
    {
        static void Postfix(Ship ship, ref string __result)
        {
            if (!ship.IsWithinSightOfLocalPlayer || !UxKit.On(s => s.ShipIntegrity)) return;
            float f = Of(ship);
            if (f < ShowBelow) __result += $"\n<s16>{UxKit.Format("HullFmt", UxKit.PercentXml(f))}</s16>";
        }
    }

    /// <summary>The hover text of a blip contains the blip text and the ship hover text. HoverText already adds the hull line.</summary>
    [HarmonyPatch(typeof(ShipIndicatorSource), nameof(ShipIndicatorSource.GetMouseoverText))]
    static class BlipHover
    {
        internal static bool Active;
        static void Prefix() => Active = true;
        static void Finalizer() => Active = false;
    }

    /// <summary>The same text on the off-screen and zoomed-out blips of your own damaged ships.</summary>
    [HarmonyPatch(typeof(ShipIndicatorSource), nameof(ShipIndicatorSource.GetNonMouseoverText))]
    static class BlipText
    {
        static void Postfix(ShipIndicatorSource __instance, ref string? __result)
        {
            if (BlipHover.Active) return;
            var ship = __instance.Ship;
            if (!ship.IsLocalPlayerShip || ship.IsDisposed) return;
            float f = UxKit.On(s => s.ShipIntegrity) ? Of(ship) : 1f;
            bool hit = DamagePings.UnderFire(ship);
            if (f >= ShowBelow && !hit) return;
            string line = (hit ? $"<bad>{UxKit.Text("UnderFire")}</bad>  " : "") + (f < ShowBelow ? UxKit.Format("HullFmt", UxKit.PercentXml(f)) : "");
            string text = $"<s14>{line.Trim()}</s14>";
            __result = __result is null ? text : __result + "\n" + text;
        }
    }

    /// <summary>V9. A thin hull bar under each damaged ship icon in the ships card (the icons that show red ghosts).</summary>
    [HarmonyPatch(typeof(ShipIconButton), nameof(ShipIconButton.OnRender))]
    static class IconBar
    {
        static void Postfix(ShipIconButton __instance, GraphicsManager gfx, Rect uiScreenRect, Color colorScale)
        {
            var ship = __instance.Ship;
            if (!__instance.ShowRedprints || ship is null || ship.IsDisposed || !ship.IsViewableByLocalPlayer || !UxKit.On(s => s.ShipIconHealth)) return;
            float f = Of(ship);
            if (f >= ShowBelow) return;
            var back = new Rect(uiScreenRect.X + 4f, uiScreenRect.Bottom - 7f, uiScreenRect.Width - 8f, 4f);
            var fill = new Rect(back.X, back.Y, back.Width * f, back.Height);
            // The same green-to-red ramp as the vanilla part health bar (PartsCard.OnDrawHealthBar).
            float damage = 1f - f;
            var color = new Color(Mathx.InverseLerp(0f, 0.5f, damage), Mathx.InverseLerp(1f, 0.5f, damage), 0f);
            if (DamagePings.UnderFire(ship)) color = Color.Lerp(color, Color.White, 0.5f * (0.5f + 0.5f * MathF.Sin((float)App.Clock.Time.Seconds * 10f)));
            gfx.SetToMaterial(null);
            gfx.Draw<Quad>(new Quad(back, new Color(0f, 0f, 0f, 0.6f) * colorScale));
            gfx.Draw<Quad>(new Quad(fill, color * colorScale));
        }
    }
}

using Cosmoteer.Gui;
using HarmonyLib;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using Halfling.Gui;
using HotCargo.Core;

namespace HotCargo.Ux;

/// <summary>
/// A big "HOT CARGO" title on the main menu, under the top button bar, so players see that the mod is active.
/// It shows only when the Hot Cargo mod is on in the game's mod list.
/// </summary>
[HarmonyPatch(typeof(TitleScreen), MethodType.Constructor)]
static class TitleBrand
{
    static readonly Color Ember = new(1f, 0.56f, 0.16f);

    static void Postfix(TitleScreen __instance)
    {
        try
        {
            if (!Activation.IsModEnabled()) return;
            __instance.AddChild(Build());
        }
        catch (Exception ex)
        {
            Log.Error("title brand failed; the main menu shows without it", ex);
        }
    }

    static Widget Build()
    {
        var box = new LayoutBox
        {
            AnchorRect = AnchorPresets.TopCenter,
            Y = 70f,
            InputBlockMode = InputBlockMode.Never,
        };
        box.NineSlice.Flags = NineSliceFlags.None;
        box.AutoSize.AutoWidthMode = AutoSizeMode.Enable;
        box.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        box.AutoSize.Anchor = new Vector2(0.5f, 0f);
        box.AddChild(Text("HOT CARGO", 96, Ember, y: 0f));
        box.AddChild(Text("You stole corporate tech. Get it to the Buyer.", 24, Color.White, y: 110f));
        box.AddChild(Text($"Hot Cargo {ModVersion()} is active  ·  New Game → Career", 16, new Color(1f, 1f, 1f, 0.6f), y: 146f));
        return box;
    }

    static Label Text(string text, int size, Color color, float y)
    {
        var label = new Label
        {
            Text = text,
            AnchorRect = AnchorPresets.TopCenter,
            Y = y,
            InputBlockMode = InputBlockMode.Never,
        };
        label.TextRenderer.Font = WidgetRules.Instance.ShadowGlowFont;
        label.TextRenderer.FontSize = size;
        label.TextRenderer.Color = color;
        label.TextRenderer.HAlignment = HAlignment.Center;
        label.TextRenderer.VAlignment = VAlignment.Top;
        label.TextRenderer.OversizeMode = OversizeMode.None;
        label.TextRenderer.Clip = false;
        label.AutoSize.AutoWidthMode = AutoSizeMode.Enable;
        label.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        label.AutoSize.Anchor = new Vector2(0.5f, 0f);
        return label;
    }

    /// <summary>The Version line of the installed mod.rules, or "dev" when it has none.</summary>
    static string ModVersion()
    {
        string path = Path.Combine(Mod.ModRoot, "mod.rules");
        if (!File.Exists(path)) return "dev";
        foreach (string line in File.ReadLines(path))
        {
            string t = line.Trim();
            if (!t.StartsWith("Version", StringComparison.Ordinal)) continue;
            int eq = t.IndexOf('=');
            if (eq > 0) return "v" + t[(eq + 1)..].Trim();
        }
        return "dev";
    }
}

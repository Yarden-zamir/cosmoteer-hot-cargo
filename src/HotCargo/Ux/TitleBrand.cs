using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Gui;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Blueprints;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Paint;
using Cosmoteer.Simulation;
using HarmonyLib;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using Halfling.Gui;
using HotCargo.Core;

namespace HotCargo.Ux;

/// <summary>
/// The Hot Cargo title on the main menu, only when the mod is on in the game's mod list.
/// A second logo ship spells "HOT CARGO" out of ship parts under the COSMOTEER logo ship, in the menu background's own sim.
/// A quiet "Hot Cargo vX.Y.Z" line sits above the game's version line.
/// </summary>
[HarmonyPatch(typeof(TitleScreen), MethodType.Constructor)]
static class TitleBrand
{
    /// <summary>The title ship of each menu sim. Clones of a MenuBackground share the sim, so they find it too.</summary>
    static readonly ConditionalWeakTable<SimRoot, Ship> TitleShips = new();

    static void Postfix(TitleScreen __instance)
    {
        if (!Activation.IsModEnabled()) return;
        UxKit.Guard("title version line", () => AddVersionLine(__instance));
        UxKit.Guard("title ship", () => AddTitleShip(__instance.Background));
    }

    static void AddTitleShip(MenuBackground background)
    {
        if (background.LogoShip is not { } logo) return;
        var ship = TitleShip.Build(logo);
        background.Sim.Ships.Add(ship);
        ship.InitializeToMax();
        Hide(ship);
        TitleShips.AddOrUpdate(background.Sim, ship);
    }

    /// <summary>Frames the logo ship and the title ship together, with the game's own framing rule.</summary>
    [HarmonyPatch(typeof(MenuBackground), nameof(MenuBackground.OnBeforeDraw))]
    static class Framing
    {
        static void Postfix(MenuBackground __instance)
        {
            if (__instance.LogoShip is not { } logo || !TitleShips.TryGetValue(__instance.Sim, out var title)) return;
            var sim = __instance.Sim;
            sim.Rendering.ForceShowRoofs[title] = __instance.ShowLogoShipRoof;
            Rect both = Rect.BoundRects(WorldRect(logo), WorldRect(title)).Inflate(2f);
            sim.Camera.WorldLocation = both.Center;
            sim.Camera.ViewController.FRZoomScale = Mathx.Max(both.Width / sim.Camera.Camera.LensSize.X, both.Height / sim.Camera.Camera.LensSize.Y);
            sim.Camera.ViewController.FRTargetZoomScale = sim.Camera.ViewController.FRZoomScale;
        }

        static Rect WorldRect(Ship ship) => ship.TransformShapeToWorld((Rect)ship.Parts.BoundingRect!.Value);
    }

    /// <summary>
    /// The title ship jumps in when the logo ship lands, so it arrives as the logo's subtitle.
    /// Until then it stays hidden, as the logo ship does before its own jump.
    /// </summary>
    [HarmonyPatch(typeof(TitleScreen), nameof(TitleScreen.AnimateJumpIn))]
    static class JumpIn
    {
        static void Postfix(TitleScreen __instance, Task __result) => JumpInAfter(__instance, __result);
    }

    [HarmonyPatch(typeof(TitleScreen), nameof(TitleScreen.AnimateJumpOut))]
    static class JumpOut
    {
        static void Prefix(TitleScreen __instance) => JumpOutWithLogo(__instance);
    }

    static async void JumpInAfter(TitleScreen screen, Task logoJump)
    {
        try
        {
            if (!TitleShips.TryGetValue(screen.Background.Sim, out var ship)) return;
            await logoJump;
            // The sim is gone when the player left the title screen during the jump.
            if (ship.Sim is null) return;
            ship.OnBeforeFtlJump();
            await ship.Renderer.AnimateFtlJumpIn(ship.FlightDirection);
        }
        catch (Exception ex)
        {
            Errors.Report("ux title ship jump", "the title ship jump-in failed", ex);
        }
    }

    static async void JumpOutWithLogo(TitleScreen screen)
    {
        try
        {
            if (!TitleShips.TryGetValue(screen.Background.Sim, out var ship) || ship.Sim is null) return;
            ship.OnBeforeFtlJump();
            await ship.Renderer.AnimateFtlJumpOut(ship.FlightDirection, null, GameApp.Rules.Menus.LogoShipFlyOutDelay);
        }
        catch (Exception ex)
        {
            Errors.Report("ux title ship jump", "the title ship jump-out failed", ex);
        }
    }

    /// <summary>A full FTL effect draws the ship as gone, the state that AnimateFtlJumpIn and AnimateFtlJumpOut leave it in.</summary>
    static void Hide(Ship ship) => ship.Renderer._ftlEffect = 1f;

    /// <summary>A line in the style of the game's version line (bottom left), one line above it.</summary>
    static void AddVersionLine(TitleScreen screen)
    {
        // The TitleScreen constructor adds the background first and then one fill LayoutBox that holds the version line.
        var box = screen.RootWidget.Children.OfType<LayoutBox>().FirstOrDefault()
            ?? throw new InvalidOperationException("the title screen has no layout box");
        const int fontSize = 16;
        var label = new Label { Text = $"Hot Cargo {ModVersion()}", AnchorRect = AnchorPresets.Fill };
        label.TextRenderer.Font = WidgetRules.Instance.ShadowFont;
        label.TextRenderer.HAlignment = HAlignment.Left;
        label.TextRenderer.VAlignment = VAlignment.Bottom;
        label.TextRenderer.OversizeMode = OversizeMode.None;
        label.TextRenderer.Padding = new Borders(4f, 4f, 4f, 4f + fontSize + 4f);
        label.TextRenderer.Color *= new Color(1f, 1f, 1f, 0.5f);
        label.TextRenderer.FontSize = fontSize;
        label.ComponentToggles[label] = screen._uiToggle;
        box.AddChild(label);
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

/// <summary>Builds the "HOT CARGO" ship from a drawing, in the paint of the logo ship.</summary>
static class TitleShip
{
    // The ship, one string per row, top to bottom. Each character is one cell; Legend says which part starts there.
    // '+' is a cell that a bigger part to its left or above covers. '.' is empty.
    // Structure lattice ('s') joins the letters into one ship, like the lattice between the COSMOTEER letters.
    // Build checks that the parts form one connected ship.
    static readonly string[] Art =
    [
        "..B..q.0##1.B##....0##1.0##1.B##1.0##1.0##1...",
        "<+#..+.q..#..#..VV.q....#..#.#..#sq....#..#>++",
        "..####s+..assqssVVs+....####s###2.+.#es#..a...",
        "<+q..#.#..#..+.....e....#..q.q.##.#..#.#..#>++",
        "..+..#.3##2..#.....3###s#..+.+..#.3##2.3##2...",
    ];

    // Armor wedges '0' to '3' take the rotation of their digit: 0 cuts the top-left corner, 1 the top-right,
    // 2 the bottom-right, 3 the bottom-left (as in the logo ship).
    static readonly Dictionary<char, (string Part, int Rotation)> Legend = new()
    {
        ['#'] = ("cosmoteer.corridor", 0),
        ['B'] = ("cosmoteer.corridor", 0),
        ['0'] = ("cosmoteer.armor_wedge", 0),
        ['1'] = ("cosmoteer.armor_wedge", 1),
        ['2'] = ("cosmoteer.armor_wedge", 2),
        ['3'] = ("cosmoteer.armor_wedge", 3),
        ['s'] = ("cosmoteer.structure", 0),
        ['e'] = ("cosmoteer.fire_extinguisher", 0),
        ['a'] = ("cosmoteer.airlock", 0),
        ['q'] = ("cosmoteer.crew_quarters_small", 0),
        ['<'] = ("cosmoteer.thruster_small", 1),
        ['>'] = ("cosmoteer.laser_blaster_small", 1),
        ['V'] = ("yarden_zamir.tech_vault", 0),
    };

    /// <summary>
    /// Heat orange in place of the logo's blue accent paint: the game's limited text color, which the heat panel uses for the
    /// stage name. The top-left corner of each letter gets it, like the blue corners of the logo: a corner wedge ('0') all of it,
    /// a square corner ('B') its top-left half.
    /// </summary>
    static Color Accent => WidgetRules.Instance.LimitedTextColor;

    /// <summary>Parts that keep their own look: the lattice and the parts that stick out of the letters.</summary>
    const string Unpainted = "s<>";

    const int GapUnderLogo = 2;
    static readonly ID<Decal> Box = new("box");
    static readonly ID<Decal> Triangle = new("triangle_half");

    public static Ship Build(Ship logo)
    {
        int width = Art[0].Length;
        if (Art.Any(row => row.Length != width)) throw new InvalidOperationException("the title ship rows differ in length");
        IntRect logoBounds = logo.Parts.BoundingRect ?? throw new InvalidOperationException("the logo ship has no parts");
        // The top-left cell of the drawing, in the cells of the logo ship. Both ships stay where the sim puts them
        // (the menu loads the logo ship at the world origin), so the cells line up.
        var origin = new IntVector2(logoBounds.IntCenter.X - width / 2, logoBounds.Bottom + GapUnderLogo);

        var ship = new Ship(logo.Rules, "Hot Cargo", 0);
        ship.Metadata.SetPaintScheme(logo.Metadata.GetPaintScheme());
        ship.Metadata.RoofDecalColors[1] = Accent;
        ship.Metadata.FlightDirection = logo.Metadata.FlightDirection;

        var parts = new List<PartSaveData>();
        var paint = new List<Decal>();
        var accent = new List<Decal>();
        for (int y = 0; y < Art.Length; y++)
            for (int x = 0; x < width; x++)
            {
                char c = Art[y][x];
                if (c is '.' or '+') continue;
                var entry = Legend.TryGetValue(c, out var e) ? e : throw new InvalidOperationException($"no part for '{c}'");
                var at = origin + new IntVector2(x, y);
                var part = Part(ship.Rules, entry, at, out var rules);
                parts.Add(part);
                if (!Unpainted.Contains(c))
                    foreach (var cell in rules.GetRect(at, entry.Rotation)) paint.Add(BoxAt(cell));
                if (c == '0') accent.Add(BoxAt(at));
                if (c == 'B') accent.Add(new Decal(Triangle, at, new IntVector2(1, 1), 0, false, false));
            }
        // One part at a time, so a part that the game refuses (it overlaps another) names itself.
        foreach (var part in parts)
        {
            int before = ship.BlueprintParts.Count;
            ship.BlueprintParts.LoadSaveData([part]);
            if (ship.BlueprintParts.Count != before + 1)
                throw new InvalidOperationException($"the title ship cannot place {part.ID} at {part.Location - origin} (rotation {part.Rotation})");
        }
        ship.MatchPhysicalToBlueprints();
        // A part joins its neighbors only through solid sides (not the cut side of a wedge, not a corner).
        // The sim splits a ship in pieces at its next update, so a broken drawing fails here instead.
        ship.StructureContiguity.UpdateContiguity();
        if (!ship.StructureContiguity.IsContiguous) throw new InvalidOperationException("the title ship drawing is not one connected ship");
        ship.DecalLayers[0].LoadSaveData([.. paint]);
        ship.DecalLayers[1].LoadSaveData([.. accent]);
        return ship;
    }

    static Decal BoxAt(IntVector2 at) => new(Box, at, new IntVector2(1, 1), 0, false, false);

    /// <summary>Save data for a part whose top-left cell is at loc.</summary>
    static PartSaveData Part(ShipRules shipRules, (string Part, int Rotation) entry, IntVector2 loc, out PartRules rules)
    {
        var id = new ID<PartRules>(entry.Part);
        rules = shipRules.PartsByID.TryGetValue(id, out var r) ? r : throw new InvalidOperationException($"part {id} not found");
        return new PartSaveData(id, loc + rules.GetSaveLocationOffset(entry.Rotation, false), entry.Rotation, false);
    }
}

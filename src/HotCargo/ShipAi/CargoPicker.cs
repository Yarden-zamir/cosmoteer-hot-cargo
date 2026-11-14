using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Resources;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using HotCargo.Core;

namespace HotCargo.ShipAi;

/// <summary>
/// Spec 1062: a check list of resource types for one cargo list (loot, sell, mine, buy). Each click changes the list at once
/// through the editor, which sends the tactics input. UI only.
/// </summary>
internal sealed class CargoPicker : WindowBox
{
    /// <summary>The resources that a cargo list offers, in rules order. Mine: the resources of asteroid deposit parts.</summary>
    internal static IReadOnlyList<ResourceRules> Offered(bool mine)
    {
        IEnumerable<ResourceRules> all = GameApp.Rules.Resources.Where(r => Autonomy.Cargo.IsCargoResource(r.ID.ToString()));
        if (!mine) return all.ToList();
        var ores = GameApp.Rules.Ships.Where(s => s.IsAsteroid).SelectMany(s => s.Parts).SelectMany(p => p.Resources).Select(r => r.Type).ToHashSet();
        return all.Where(r => ores.Contains(r.ID)).ToList();
    }

    public CargoPicker(string title, bool mine, IReadOnlyCollection<string> selected, Action<string[]> changed)
    {
        TitleText = title;
        Size = new Vector2(420f, 560f);
        var box = new ScrollBox();
        box.AnchorRect = AnchorPresets.Fill;
        box.NineSlice.Flags = NineSliceFlags.None;
        box.ScrollBarController.ShowHorizontal = ScrollBarShowMode.Never;
        box.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        box.Children.BorderPadding = new Borders(12f);
        box.Children.WidgetPadding = new Vector2(4f);
        AddChild(box);
        var chosen = selected.ToList();
        foreach (var r in Offered(mine))
        {
            string id = r.ID.ToString();
            var toggle = new ToggleButton(ToggleButton.Style.Check) { Text = r.NameKey.Text, IsSelected = chosen.Contains(id), Height = 30f };
            toggle.PercentileWidth = 100f;
            toggle.SelectionChanged += (_, _) => Errors.Guard("cargo picker", () =>
            {
                if (toggle.IsSelected && !chosen.Contains(id)) chosen.Add(id);
                if (!toggle.IsSelected) chosen.Remove(id);
                // Rules order, so the same choice always gives the same text.
                changed(Offered(mine).Select(o => o.ID.ToString()).Where(chosen.Contains).ToArray());
            });
            box.Children.Add(toggle);
        }
        var done = new Button();
        done.CopySettingsFrom(WidgetRules.Instance.FlatButton);
        done.Height = 34f;
        done.Text = Strings.GetText("HotCargoAutonomy/PickDone");
        done.PercentileWidth = 100f;
        done.Clicked += (_, _) => this.Unparent();
        box.Children.Add(done);
        Parented += (_, _) =>
        {
            var parent = Parent ?? throw new InvalidOperationException("cargo picker parented without a parent");
            ScreenRect = Rect.CenteredOn(parent.RectProvider.ScreenRect.Size / 2f, Size);
            BoundsProvider = parent.RectProvider;
        };
        Unparenting += (_, _) => BoundsProvider = null;
        WidgetRules.AddDialogTweens(this);
        WidgetRules.AddBlur(this);
    }
}

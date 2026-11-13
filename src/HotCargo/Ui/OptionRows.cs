using Cosmoteer;
using Cosmoteer.Gui;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Rects;

namespace HotCargo.Ui;

/// <summary>
/// What one option row shows. Toggle rows belong to a multiselect page (selected or not); other rows are action buttons.
/// The slider, when there is one, belongs to the option: it sits in the same row and works only when the option can use it.
/// </summary>
public sealed record OptionRowView(string Key, string Text, bool Active, string? Tooltip, bool Toggle, bool Selected, SliderSpec? Slider, int Value, bool SliderActive);

/// <summary>
/// A reusable list of option rows for dialogs (spec 591): each row is a button with its own slider on the right.
/// UI only: it raises <see cref="Clicked"/> and <see cref="ValueChanged"/>, and the dialog sends the inputs.
/// </summary>
public sealed class OptionRows
{
    sealed class Row
    {
        public required LayoutBox Box;
        public required ImageButton Button;
        public required SliderRow Slider;
        public OptionRowView? View;
    }

    readonly List<Row> _rows = [];
    const float RowHeight = 72f;

    public LayoutBox Box { get; }
    public event Action<OptionRowView>? Clicked;
    public event Action<OptionRowView, int>? ValueChanged;

    /// <summary>The button of each shown row, in order. The dialog hands them to the shared choice panel for hover markers.</summary>
    public IEnumerable<(ImageButton Button, OptionRowView View)> Shown =>
        _rows.Where(r => r.View is not null && r.Box.SelfActive).Select(r => (r.Button, r.View!));

    public OptionRows()
    {
        Box = new LayoutBox { PercentileWidth = 100f };
        Box.NineSlice.Flags = NineSliceFlags.None;
        Box.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        Box.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        Box.Children.WidgetPadding = new Halfling.Geometry.Vector2(10f);
        Box.Children.BorderPadding = new Borders(50f, 0f, 0f, 0f);
    }

    /// <summary>Shows the views, reusing rows. Call each frame.</summary>
    public void Show(IReadOnlyList<OptionRowView> views)
    {
        while (_rows.Count < views.Count) _rows.Add(NewRow());
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            if (i >= views.Count) { r.Box.SelfActive = false; r.View = null; continue; }
            var v = views[i];
            r.View = v;
            r.Box.SelfActive = true;
            string mark = v.Toggle ? (v.Selected ? "<good>[X]</good> " : "<gray>[ ]</gray> ") : "";
            r.Button.Text = mark + v.Text;
            r.Button.ToolTips.Text = v.Tooltip;
            r.Button.SelfInputActive = v.Active;
            r.Button.PercentileWidth = v.Slider is null ? 100f : 62f;
            r.Slider.Row.SelfActive = v.Slider is not null;
            if (v.Slider is { } spec)
            {
                r.Slider.Enabled = v.SliderActive;
                r.Slider.Show(spec, v.Value);
            }
        }
    }

    /// <summary>Clicks shown row <paramref name="index"/> (0-based) as a player would. False when there is no such row or it takes no input.</summary>
    public bool Click(int index)
    {
        var shown = Shown.ToList();
        if (index < 0 || index >= shown.Count || !shown[index].Button.IsInputActive) return false;
        Clicked?.Invoke(shown[index].View);
        return true;
    }

    /// <summary>Moves the slider of the row with this key as a player would. For autotests and screenshots.</summary>
    public void SetValue(string key, int value) => _rows.FirstOrDefault(r => r.View?.Key == key)?.Slider.Set(value);

    Row NewRow()
    {
        // Fixed row height: two lines of option text fit. An auto height grew without limit when a row first showed before the
        // window had its width (the text wrapped at width 0).
        var box = new LayoutBox { PercentileWidth = 100f, Height = RowHeight };
        box.NineSlice.Flags = NineSliceFlags.None;
        box.Children.LayoutAlgorithm = LayoutAlgorithms.StretchLeftToRight;
        box.Children.WidgetPadding = new Halfling.Geometry.Vector2(10f);
        var button = new ImageButton();
        button.CopySettingsFrom(WidgetRules.Instance.FlatButton);
        foreach (var t in new[] { button.StateNormalTextRenderer, button.StateHighlightedTextRenderer, button.StatePressedTextRenderer, button.StateDisabledTextRenderer })
        {
            t.XmlFormatting = true;
            t.FontSize = 20;
            t.HAlignment = HAlignment.Left;
            t.Padding = new Borders(10f);
        }
        var icon = GameApp.Rules.CareerMode.Gui.Comms.DefaultDialogueLineIcon;
        button.ImageSprite.CopySpriteFrom(icon);
        button.ImageRectController.InnerRectMode = InnerRectMode.Left;
        button.ImageRectController.BorderPadding = new Borders(10f);
        button.ImageRectController.InnerRectSize = button.ImageSprite.DesiredSize;
        var slider = new SliderRow(labelPercent: 0f) { };
        slider.Row.PercentileWidth = 38f;
        box.AddChild(button);
        box.AddChild(slider.Row);
        Box.AddChild(box);
        var row = new Row { Box = box, Button = button, Slider = slider };
        button.Clicked += (_, _) => { if (row.View is { Active: true } v) Clicked?.Invoke(v); };
        slider.Changed += value => { if (row.View is { } v) ValueChanged?.Invoke(v, value); };
        return row;
    }
}

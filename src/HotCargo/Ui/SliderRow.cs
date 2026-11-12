using Cosmoteer.Gui;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Label = Halfling.Gui.Label;

namespace HotCargo.Ui;

/// <summary>
/// What a dialog slider picks: whole numbers from Min to Max in steps of Step (amounts, minutes, prices).
/// Format turns the value into the XML text right of the slider. Key names the slider, so a value survives page rebuilds.
/// </summary>
public sealed record SliderSpec(string Key, string Label, int Min, int Max, int Step, int Default, Func<int, string> Format)
{
    /// <summary>The value snapped into Min..Max on the step grid.</summary>
    public int Snap(int value)
    {
        if (Max < Min || Step <= 0) throw new InvalidOperationException($"slider {Key}: bad range {Min}..{Max} step {Step}");
        int v = Math.Clamp(value, Min, Max);
        return Math.Min(Max, Min + (v - Min + Step / 2) / Step * Step);
    }
}

/// <summary>
/// A reusable dialog row: a label, a horizontal slider and its value text, styled like the game's setting sliders.
/// UI only: it holds a local value and never changes the sim. A dialog reads <see cref="Value"/> and sends the value in its MP input.
/// Used by the comms dialogue tab (<see cref="CommsHail"/>); any window can add <see cref="Row"/> to its layout.
/// </summary>
public sealed class SliderRow
{
    public LayoutBox Row { get; }
    readonly Label _label;
    readonly ScrollBar _slider;
    readonly Label _value;
    SliderSpec? _spec;

    /// <summary>Raised when the player moves the slider. Not raised by <see cref="Show"/>.</summary>
    public event Action<int>? Changed;

    public int Value => _spec?.Snap((int)MathF.Round(_slider.Value)) ?? throw new InvalidOperationException("slider has no spec");

    public SliderRow()
    {
        Row = new LayoutBox { Height = 40f, PercentileWidth = 100f };
        Row.NineSlice.Flags = NineSliceFlags.None;
        Row.Children.LayoutAlgorithm = LayoutAlgorithms.StretchLeftToRight;
        _label = new Label { PercentileWidth = 30f };
        _label.TextRenderer.XmlFormatting = true;
        _label.TextRenderer.FontSize = 20;
        Row.AddChild(_label);
        _slider = new ScrollBar(WidgetRules.Instance.HorizontalSlider) { PercentileWidth = 50f };
        Row.AddChild(_slider);
        _value = new Label { PercentileWidth = 20f };
        _value.TextRenderer.XmlFormatting = true;
        _value.TextRenderer.FontSize = 20;
        _value.TextRenderer.HAlignment = HAlignment.Center;
        Row.AddChild(_value);
        _slider.ValueChanged += (_, _) =>
        {
            if (_spec is null || _showing) return;
            int v = Value;
            _value.Text = _spec.Format(v);
            Changed?.Invoke(v);
        };
    }

    bool _showing;

    /// <summary>Shows a spec with a value. Safe to call every frame: it changes the widgets only when something differs.</summary>
    public void Show(SliderSpec spec, int value)
    {
        int v = spec.Snap(value);
        _showing = true;
        try
        {
            if (_spec is null || _spec.Min != spec.Min || _spec.Max != spec.Max || _spec.Step != spec.Step)
            {
                _slider.MinValue = spec.Min;
                // A one-value range still needs a track: the game slider divides by the range.
                _slider.MaxValue = Math.Max(spec.Max, spec.Min + 1);
                _slider.Interval = spec.Step;
                _slider.IncrementAmount = spec.Step;
                _slider.PageSize = 0f;
            }
            if ((int)MathF.Round(_slider.Value) != v) _slider.Value = v;
            _slider.SelfInputActive = spec.Max > spec.Min;
            _spec = spec;
            _label.Text = spec.Label;
            _value.Text = spec.Format(v);
        }
        finally { _showing = false; }
    }

    /// <summary>Moves the slider as a player would. For autotests and keys.</summary>
    public void Set(int value)
    {
        if (_spec is null) throw new InvalidOperationException("slider has no spec");
        _slider.Value = _spec.Snap(value);
    }
}

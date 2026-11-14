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
/// Option rows (<see cref="OptionRows"/>) hold one each; any window can add <see cref="Row"/> to its layout.
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

    /// <param name="labelPercent">Width of the label in percent of the row. 0 hides the label (the option text is the label).</param>
    public SliderRow(float labelPercent = 30f)
    {
        Row = new LayoutBox { Height = 40f, PercentileWidth = 100f };
        Row.NineSlice.Flags = NineSliceFlags.None;
        Row.Children.LayoutAlgorithm = LayoutAlgorithms.StretchLeftToRight;
        _label = new Label { PercentileWidth = labelPercent, SelfActive = labelPercent > 0 };
        _label.TextRenderer.XmlFormatting = true;
        _label.TextRenderer.FontSize = 20;
        Row.AddChild(_label);
        // Without a label (an option row, where the slider is a narrow part of the row) the value gets half, so "40 Hyperium" fits on one line.
        float valuePercent = labelPercent > 0 ? 20f : 50f;
        _slider = new ScrollBar(WidgetRules.Instance.HorizontalSlider) { PercentileWidth = 100f - labelPercent - valuePercent };
        Row.AddChild(_slider);
        _value = new Label { PercentileWidth = valuePercent };
        _value.TextRenderer.XmlFormatting = true;
        _value.TextRenderer.FontSize = 20;
        _value.TextRenderer.HAlignment = HAlignment.Center;
        Row.AddChild(_value);
        _slider.ValueChanged += (_, _) =>
        {
            if (_spec is null || _showing) return;
            int v = Value;
            _value.Text = _spec.Format(v);
            _localAt = Halfling.App.Clock.Time.Seconds;
            Changed?.Invoke(v);
        };
    }

    bool _showing;
    double _localAt = double.MinValue;
    /// <summary>Real seconds after a local move in which <see cref="Show"/> keeps the local value: a shared value lags by the MP input delay.</summary>
    const double LocalHoldSeconds = 0.8;

    /// <summary>False greys the slider out (an option that is not selected).</summary>
    public bool Enabled { get; set; } = true;

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
            bool held = Halfling.App.Clock.Time.Seconds - _localAt < LocalHoldSeconds;
            if (!held && (int)MathF.Round(_slider.Value) != v) _slider.Value = v;
            else if (held) v = spec.Snap((int)MathF.Round(_slider.Value));
            _slider.SelfInputActive = Enabled && spec.Max > spec.Min;
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

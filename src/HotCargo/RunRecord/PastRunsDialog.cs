using Cosmoteer.Gui;
using Cosmoteer.Input;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Dialogs;
using HotCargo.Core;

namespace HotCargo.RunRecord;

/// <summary>
/// Spec 1227 to 1232: the Past Runs window of the main menu. A table of every entry of the run record, sorted by score; a click on
/// a column header sorts by that column. It reads the record through Recorder.Load, like the career log block. UI only.
/// </summary>
internal sealed class PastRunsDialog : DialogBase
{
    /// <summary>The open window, for the test commands.</summary>
    internal static PastRunsDialog? Open { get; private set; }

    /// <summary>Column widths in percent of the table, in RunColumn order. Layout only.</summary>
    static readonly float[] Widths = [15f, 10f, 16f, 11f, 10f, 12f, 13f, 10f];

    readonly WindowBox _window;
    readonly Label _summary;
    readonly SimpleBox _header;
    readonly ScrollBox _rows;
    readonly List<RunEntry> _runs;

    internal RunColumn Column { get; private set; } = RunSort.DefaultColumn;
    internal bool Descending { get; private set; } = true;

    /// <summary>The entries in the order that the table shows them. For the test commands.</summary>
    internal IReadOnlyList<RunEntry> Shown { get; private set; } = [];

    /// <summary>The table row of the best run (spec 1231), or -1.</summary>
    internal int BestRow { get; private set; } = -1;

    internal PastRunsDialog()
    {
        if (Widths.Length != Enum.GetValues<RunColumn>().Length) throw new InvalidOperationException("Past Runs: one width for each column");
        _runs = Recorder.Load() ?? [];

        _window = new WindowBox();
        _window.Size = new Vector2(1100f, 640f);
        _window.ResizeController.MinSize = new Vector2(860f, 360f);
        _window.BoundsProvider = RootWidget;
        _window.TitleText = RecordRows.Text("Title");
        _window.AutoClose = false;
        _window.CloseButton.Clicked += (_, _) => Close();
        _window.ScrollBarController.ShowHorizontal = ScrollBarShowMode.Never;
        _window.ScrollBarController.ShowVertical = ScrollBarShowMode.Never;
        AddChild(_window);

        var layout = new LayoutBox();
        layout.NineSlice.Flags = NineSliceFlags.None;
        layout.AnchorRect = AnchorPresets.Fill;
        layout.DockedChildren.BorderPadding = new Borders(10f);
        layout.DockedChildren.WidgetPadding = new Vector2(6f);
        _window.AddChild(layout);

        _summary = new Label { DockMode = DockMode.Top };
        _summary.TextRenderer.XmlFormatting = true;
        _summary.TextRenderer.OversizeMode = OversizeMode.Wrap;
        _summary.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        layout.DockedChildren.Add(_summary);

        _header = new SimpleBox { Height = 34f, DockMode = DockMode.Top };
        _header.NineSlice.Flags = NineSliceFlags.None;
        layout.DockedChildren.Add(_header);

        var bottom = new SimpleBox { Height = 44f, DockMode = DockMode.Bottom };
        bottom.NineSlice.Flags = NineSliceFlags.None;
        var close = new Button(WidgetRules.Instance.OkayButton)
        {
            AnchorRect = new Rect(1f, 0f, 0f, 1f),
            Rect = Rect.FromEdges(-200f, 0f, 0f, 0f),
            Text = RecordRows.Text("Close"),
            Hotkey = Inputs.General.Cancel,
        };
        close.Clicked += (_, _) => Close();
        bottom.AddChild(close);
        layout.DockedChildren.Add(bottom);

        _rows = new ScrollBox();
        _rows.AnchorRect = AnchorPresets.Fill;
        _rows.NineSlice.Flags = NineSliceFlags.None;
        _rows.ScrollBarController.ShowHorizontal = ScrollBarShowMode.Never;
        _rows.ScrollBarController.ShowVertical = ScrollBarShowMode.WhenNeeded;
        _rows.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        _rows.Children.WidgetPadding = new Vector2(2f);
        layout.AddChild(_rows);

        Fill();
        WidgetRules.AddDialogTweens(this, _window);
        WidgetRules.AddBlur(_window);
    }

    public override void Pushed()
    {
        base.Pushed();
        _window.Location = RootWidget.ScreenRect.Center - _window.Size / 2f;
        Open = this;
    }

    public override void PoppedDelayed()
    {
        base.PoppedDelayed();
        if (Open == this) Open = null;
    }

    internal void Close()
    {
        if (App.Director.States.Contains(this)) App.Director.PopState(this);
    }

    /// <summary>Spec 1230: a click on a column header.</summary>
    internal void SortBy(RunColumn column)
    {
        (Column, Descending) = RunSort.Click(Column, Descending, column);
        Fill();
    }

    /// <summary>The percent rect of a column inside a row.</summary>
    static Rect CellRect(RunColumn column)
    {
        float left = Widths.Take((int)column).Sum();
        return new Rect(left, 0f, Widths[(int)column], 100f);
    }

    void Fill()
    {
        var best = RunRecordModel.Best(_runs);
        Shown = RunSort.Sort(_runs, Column, Descending);
        BestRow = best is null ? -1 : Shown.ToList().IndexOf(best);
        // Spec 1232: an unreadable file shows its error; the table is then empty.
        _summary.Text = Recorder.LoadError is { } error ? RecordRows.Format("ErrorFmt", Halfling.StringTools.EscapeForXml(error))
            : _runs.Count == 0 ? RecordRows.Text("Empty") : RecordRows.Format("SummaryFmt", _runs.Count);

        _header.Children.Clear();
        foreach (var column in Enum.GetValues<RunColumn>())
        {
            string arrow = column == Column ? (Descending ? " ▼" : " ▲") : "";
            var button = new Button(WidgetRules.Instance.FlatButton) { PercentileRect = CellRect(column), Text = RecordRows.Text("Col" + column) + arrow };
            var clicked = column;
            button.Clicked += (_, _) => Errors.Guard("past runs sort", () => SortBy(clicked));
            _header.AddChild(button);
        }

        _rows.Children.Clear();
        for (int i = 0; i < Shown.Count; i++) _rows.AddChild(Row(Shown[i], i == BestRow));
    }

    /// <summary>One table row. Spec 1231: the best run gets a BEST mark next to its score.</summary>
    static SimpleBox Row(RunEntry e, bool best)
    {
        var row = new SimpleBox { Height = 28f };
        row.NineSlice.Flags = best ? NineSliceFlags.All : NineSliceFlags.None;
        foreach (var column in Enum.GetValues<RunColumn>())
        {
            string text = RecordRows.Cell(e, column);
            if (best && column == RunColumn.Score) text += "  " + RecordRows.Text("BestMark");
            var label = new Label { PercentileRect = CellRect(column), Text = text };
            label.TextRenderer.XmlFormatting = true;
            label.TextRenderer.HAlignment = HAlignment.Center;
            label.TextRenderer.VAlignment = VAlignment.Center;
            label.TextRenderer.OversizeMode = OversizeMode.Ellipses;
            row.AddChild(label);
        }
        return row;
    }
}

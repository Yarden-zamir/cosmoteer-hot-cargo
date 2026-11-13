using System.Collections;
using System.Reflection;
using Cosmoteer.Gui;
using Cosmoteer.Gui.SettingsConfig;
using Cosmoteer.Input;
using Cosmoteer.Localization;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Dialogs;
using HotCargo.Core;
using HotCargo.Ux;

namespace HotCargo.TunablesMenu;

/// <summary>
/// The "Hot Cargo settings" window (spec 500). It builds one section for each field of Tunables and one row for each tunable,
/// by reflection, so new tunables show without a change here. Each row edits through TunablesEditor.
/// </summary>
internal sealed class TunablesDialog : DialogBase
{
    /// <summary>The open window, for the test commands.</summary>
    internal static TunablesDialog? Open { get; private set; }

    /// <summary>The last error that the window shows. Null when the last change worked.</summary>
    internal string? Error { get; private set; }

    internal bool ReadOnly => !_canEdit;

    readonly Run _run;
    readonly bool _canEdit;
    readonly WindowBox _window;
    readonly TextSearchField _search;
    readonly Label _status;
    readonly ScrollBox _page;
    readonly List<Section> _sections = [];
    readonly Dictionary<string, Row> _rows = [];
    string _shownHash = "";
    string _structure = "";
    /// <summary>True while rows show new values, so that the change events of the controls do not send the same values back.</summary>
    bool _showing;

    sealed record Section(string Name, Widget Outer, ToggleButton Toggle, List<Row> Rows);

    /// <summary>One editable value. Enter sets the control from text and commits it, like a player edit.</summary>
    internal sealed class Row
    {
        public required string Path;
        public required FieldInfo Field;
        public required Widget Box;
        public required Label Name;
        public required string Search;
        public required Action<object?> Show;
        /// <summary>The value that the control shows, as text. For the test commands.</summary>
        public required Func<string> Shown;
        public required Func<string, string?> Enter;
        public required Button Reset;
    }

    internal TunablesDialog(Run run)
    {
        _run = run;
        _canEdit = TunablesEditor.CanEdit(run);
        ToolTipBox toolTipBox = new();
        AddDropTarget(toolTipBox);
        ToolTipDisplay = toolTipBox;

        _window = new WindowBox();
        _window.Size = new Vector2(980f, 760f);
        _window.ResizeController.MinSize = new Vector2(720f, 480f);
        _window.BoundsProvider = RootWidget;
        _window.TitleText = Text("Title");
        _window.AutoClose = false;
        _window.CloseButton.Clicked += (_, _) => Close();
        _window.ScrollBarController.ShowHorizontal = ScrollBarShowMode.Never;
        _window.ScrollBarController.ShowVertical = ScrollBarShowMode.Never;
        AddChild(_window);

        var layout = new LayoutBox();
        layout.NineSlice.Flags = NineSliceFlags.None;
        layout.AnchorRect = AnchorPresets.Fill;
        layout.DockedChildren.BorderPadding = new Borders(10f);
        layout.DockedChildren.WidgetPadding = new Vector2(8f);
        _window.AddChild(layout);

        var top = new SimpleBox { Height = 32f, DockMode = DockMode.Top };
        top.NineSlice.Flags = NineSliceFlags.None;
        _search = new TextSearchField();
        _search.PercentileRect = new Rect(0f, 0f, 60f, 100f);
        _search.TextChanged += (_, _) => Filter();
        top.AddChild(_search);
        var note = new Label { PercentileRect = new Rect(62f, 0f, 38f, 100f) };
        note.TextRenderer.XmlFormatting = true;
        note.TextRenderer.FontSize = 14;
        note.TextRenderer.HAlignment = HAlignment.Right;
        note.Text = Text(_canEdit ? "SavedNote" : "ReadOnlyNote");
        top.AddChild(note);
        layout.DockedChildren.Add(top);

        _status = new Label { DockMode = DockMode.Top };
        _status.TextRenderer.XmlFormatting = true;
        _status.TextRenderer.OversizeMode = OversizeMode.Wrap;
        _status.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        layout.DockedChildren.Add(_status);

        var bottom = new SimpleBox { Height = 44f, DockMode = DockMode.Bottom };
        bottom.NineSlice.Flags = NineSliceFlags.None;
        var close = new Button(WidgetRules.Instance.OkayButton)
        {
            AnchorRect = new Rect(1f, 0f, 0f, 1f),
            Rect = Rect.FromEdges(-200f, 0f, 0f, 0f),
            Text = Text("Close"),
            Hotkey = Inputs.General.Cancel,
        };
        close.Clicked += (_, _) => Close();
        bottom.AddChild(close);
        layout.DockedChildren.Add(bottom);

        _page = new ScrollBox();
        _page.AnchorRect = AnchorPresets.Fill;
        _page.NineSlice.Flags = NineSliceFlags.None;
        _page.ScrollBarController.ShowHorizontal = ScrollBarShowMode.Never;
        _page.ScrollBarController.ShowVertical = ScrollBarShowMode.WhenNeeded;
        _page.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        _page.Children.WidgetPadding = new Vector2(10f);
        _page.Children.BorderPadding = new Borders(4f, 4f, 14f, 4f);
        layout.AddChild(_page);

        Build();
        WidgetRules.AddDialogTweens(this, _window);
        WidgetRules.AddBlur(_window);
        BeforeFrameInput += (_, _) => Errors.Guard("tunables menu frame", OnFrame);
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

    internal IReadOnlyDictionary<string, Row> Rows => _rows;

    internal int SectionCount => _sections.Count;

    internal void ResetSection(string name)
    {
        var section = _sections.FirstOrDefault(s => s.Name == name) ?? throw new InvalidOperationException($"no section '{name}'");
        Commit(() => TunablesEditor.Reset(_run, section.Name));
    }

    internal void Search(string text) => _search.Text = text;

    void OnFrame()
    {
        if (Run.Current != _run) { Close(); return; }
        // The game mode's frame hook does not run while this window covers a paused game, so the window watches the file too (spec 52).
        if (_canEdit && !_run.HostTunablesPending && TunablesFile.ChangedSinceLastRead()) _run.ReloadTunables();
        TuneRequest.Poll(_run);
        if (TunablesEditor.CurrentHash(_run) != _shownHash) Refresh();
    }

    // ---------------- build

    void Build()
    {
        var expanded = _sections.Where(s => s.Toggle.IsSelected).Select(s => s.Name).ToHashSet();
        _page.Children.Clear();
        _sections.Clear();
        _rows.Clear();
        var values = TunablesEditor.Current(_run);
        var defaults = new Tunables();
        // Areas declare their sections in their own files (partial Tunables), so declaration order follows file order. Sort by label instead.
        foreach (var field in TunablePath.Fields(typeof(Tunables)).OrderBy(f => TunableDocs.Label(f.Name), StringComparer.CurrentCulture))
        {
            var outer = new LayoutBox();
            outer.CopySettingsFrom(WidgetRules.Instance.HollowWidget);
            outer.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
            outer.Children.BorderPadding = new Borders(10f);
            outer.Children.WidgetPadding = new Vector2(10f);
            outer.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
            outer.AddChild(SettingsTab.CreateToggledCategory(new LiveText(() => TunableDocs.Label(field.Name)), expanded.Contains(field.Name), out LayoutBox box, out ToggleButton toggle));
            foreach (var r in new[] { toggle.StateNormalTextRenderer, toggle.StateHighlightedTextRenderer, toggle.StatePressedTextRenderer, toggle.StateDisabledTextRenderer })
                r.FontSize = 20;
            var section = new Section(field.Name, outer, toggle, []);

            var header = new SimpleBox { Height = 32f };
            header.NineSlice.Flags = NineSliceFlags.None;
            header.AddChild(SmallLabel(TunableDocs.Describe(field.FieldType), new Rect(0f, 0f, 80f, 100f)));
            var reset = SmallButton(Text("ResetSection"), new Rect(82f, 0f, 18f, 100f));
            reset.Clicked += (_, _) => ResetSection(field.Name);
            header.AddChild(reset);
            box.AddChild(header);

            AddFields(section, box, field.FieldType, field.Name, field.GetValue(values)!, field.GetValue(defaults));
            _sections.Add(section);
            _page.AddChild(outer);
        }
        _structure = Structure(values);
        Refresh();
        Filter();
    }

    void AddFields(Section section, LayoutBox parent, Type type, string path, object values, object? defaults)
    {
        foreach (var f in TunablePath.Fields(type))
        {
            string p = TunablePath.Child(path, f.Name);
            object? v = f.GetValue(values);
            object? d = defaults is null ? null : f.GetValue(defaults);
            Type? element = TunablePath.ElementType(f.FieldType);
            if (TunablePath.IsScalar(f.FieldType) || (element is not null && TunablePath.IsScalar(element))) AddRow(section, parent, f, p, d);
            else if (element is not null) AddList(section, parent, f, p, (IList)v!, d as IList);
            else
            {
                var group = Group(parent, $"<b>{Xml(TunableDocs.Label(f.Name))}</b>  {Info(f, d)}");
                AddFields(section, group, f.FieldType, p, v!, d);
            }
        }
    }

    /// <summary>A list of objects (a stage table): one group per item with Remove, and Add and Reset for the list.</summary>
    void AddList(Section section, LayoutBox parent, FieldInfo f, string path, IList items, IList? defaults)
    {
        var group = Group(parent, $"<b>{Xml(TunableDocs.Label(f.Name))}</b>  {Info(f, null)}");
        var bar = new SimpleBox { Height = 32f };
        bar.NineSlice.Flags = NineSliceFlags.None;
        var add = SmallButton(Text("Add"), new Rect(0f, 0f, 18f, 100f));
        add.Clicked += (_, _) => Commit(() => TunablesEditor.AddItem(_run, path));
        bar.AddChild(add);
        var reset = SmallButton(Text("ResetList"), new Rect(20f, 0f, 18f, 100f));
        reset.Clicked += (_, _) => Commit(() => TunablesEditor.Reset(_run, path));
        bar.AddChild(reset);
        group.AddChild(bar);
        Type element = TunablePath.ElementType(f.FieldType)!;
        for (int i = 0; i < items.Count; i++)
        {
            int index = i;
            object item = items[i]!;
            string name = element.GetField("Name")?.GetValue(item) as string ?? "";
            var itemBox = Group(group, $"{Xml(TunableDocs.Label(f.Name))} {i + 1}{(name.Length > 0 ? ": " + Xml(name) : "")}");
            var remove = SmallButton(Text("Remove"), new Rect(0f, 0f, 18f, 100f));
            remove.Clicked += (_, _) => Commit(() => TunablesEditor.RemoveItem(_run, path, index));
            var removeBar = new SimpleBox { Height = 28f };
            removeBar.NineSlice.Flags = NineSliceFlags.None;
            removeBar.AddChild(remove);
            itemBox.AddChild(removeBar);
            AddFields(section, itemBox, element, TunablePath.Index(path, i), item, defaults is not null && i < defaults.Count ? defaults[i] : null);
        }
    }

    void AddRow(Section section, LayoutBox parent, FieldInfo f, string path, object? defaultValue)
    {
        var box = new LayoutBox();
        box.NineSlice.Flags = NineSliceFlags.None;
        box.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        box.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        box.Children.WidgetPadding = new Vector2(2f);
        var line = new SimpleBox { Height = 32f };
        line.NineSlice.Flags = NineSliceFlags.None;
        var name = new Label { PercentileRect = new Rect(0f, 0f, 38f, 100f) };
        name.TextRenderer.XmlFormatting = true;
        name.TextRenderer.HAlignment = HAlignment.Left;
        name.TextRenderer.OversizeMode = OversizeMode.Shrink;
        line.AddChild(name);

        var (control, show, shown, enter) = Control(f, path);
        control.PercentileRect = new Rect(39f, 0f, 46f, 100f);
        control.SelfInputActive = _canEdit;
        line.AddChild(control);
        var reset = SmallButton(Text("Reset"), new Rect(86f, 0f, 14f, 100f));
        reset.SelfInputActive = _canEdit && defaultValue is not null;
        reset.Clicked += (_, _) => Commit(() => TunablesEditor.Reset(_run, path));
        line.AddChild(reset);
        box.AddChild(line);

        var info = new Label { PercentileWidth = 100f };
        info.TextRenderer.XmlFormatting = true;
        info.TextRenderer.FontSize = 14;
        info.TextRenderer.HAlignment = HAlignment.Left;
        info.TextRenderer.OversizeMode = OversizeMode.Wrap;
        info.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        info.Text = Info(f, defaultValue);
        box.AddChild(info);
        parent.AddChild(box);

        string label = TunableDocs.Label(f.Name);
        var row = new Row
        {
            Path = path, Field = f, Box = box, Name = name, Reset = reset, Enter = enter, Shown = shown,
            Search = $"{path} {label} {TunableDocs.Describe(f)}".ToLowerInvariant(),
            // A value that differs from its default shows its name highlighted. A list item without a default counts as changed.
            Show = value =>
            {
                bool changed = defaultValue is null || TunablesEditor.Format(value) != TunablesEditor.Format(defaultValue);
                name.Text = changed ? $"<fame_color>{Xml(label)}</fame_color>" : Xml(label);
                show(value);
            },
        };
        section.Rows.Add(row);
        _rows[path] = row;
    }

    /// <summary>The control for one value: a check box, a drop list (enums and stage names) or a text field.</summary>
    (Widget Control, Action<object?> Show, Func<string> Shown, Func<string, string?> Enter) Control(FieldInfo f, string path)
    {
        Type type = path.EndsWith(']') ? TunablePath.ElementType(f.FieldType)! : f.FieldType;
        if (type == typeof(bool))
        {
            var check = new ToggleButton(ToggleButton.Style.Check) { Height = 32f };
            check.SelectionChanged += (_, _) => { if (!_showing) Commit(() => TunablesEditor.Set(_run, path, check.IsSelected ? "true" : "false")); };
            return (check, v => check.IsSelected = v is true, () => check.IsSelected ? "true" : "false", text =>
            {
                check.IsSelected = (bool)TunablesEditor.Parse(typeof(bool), text);
                return Error;
            });
        }
        if (Choices(f, type) is { } choices)
        {
            var drop = new DropList { Height = 32f };
            var items = new Dictionary<SelectableButton, string>();
            foreach (string c in choices)
            {
                var item = new ListItem { Text = c.Length == 0 ? Text("NoStage") : c };
                items[item] = c;
                drop.AddChild(item);
            }
            drop.ListBox.SelectionManager.WidgetSelected += (_, e) =>
            {
                if (!_showing && e.Widget is SelectableButton b && items.TryGetValue(b, out string? value)) Commit(() => TunablesEditor.Set(_run, path, value));
            };
            return (drop, v => drop.SelectedWidget = items.FirstOrDefault(kv => kv.Value == (v as string ?? v?.ToString())).Key,
                () => drop.SelectedWidget is SelectableButton b && items.TryGetValue(b, out string? shown) ? shown : "", text =>
            {
                var item = items.FirstOrDefault(kv => kv.Value == text).Key ?? throw new InvalidOperationException($"'{text}' is not a choice of {path}");
                drop.SelectedWidget = item;
                return Error;
            });
        }
        var field = new TextEditField(TextEditField.Style.SingleLine) { Height = 32f };
        field.FocusController.Defocused += (_, _) => { if (!_showing) Commit(() => TunablesEditor.Set(_run, path, field.Text)); };
        Cosmoteer.Game.Gui.Extensions.BeforeInputWhileFocused(field, (_, _) =>
        {
            var filter = App.Input.InputFilter;
            App.Input.InputFilter = null;
            bool enter = Inputs.General.Confirm.WasPressed;
            App.Input.InputFilter = filter;
            if (enter) field.FocusController.RelinquishFocus();
        });
        return (field, v => { if (!field.FocusController.IsFocused) field.Text = TunablesEditor.Format(v); }, () => field.Text, text =>
        {
            field.Text = text;
            Commit(() => TunablesEditor.Set(_run, path, text));
            return Error;
        });
    }

    /// <summary>
    /// Fixed choices: the names of an enum, or the heat stage names for a string that names a stage.
    /// A string names a stage when its field ends with "MinStage" (tactic gates, "" = no minimum) or it is the Name of a
    /// Hunters.Stages response (the response must match a Heat.Stages name, TunablesFile.Validate).
    /// </summary>
    IReadOnlyList<string>? Choices(FieldInfo f, Type type)
    {
        if (type.IsEnum) return Enum.GetNames(type);
        if (type != typeof(string)) return null;
        var stages = TunablesEditor.Current(_run).Heat.Stages.Select(s => s.Name).ToList();
        if (f.Name.EndsWith("MinStage")) return ["", .. stages];
        if (f.DeclaringType == typeof(Hunters.StageResponse) && f.Name == nameof(Hunters.StageResponse.Name)) return stages;
        return null;
    }

    // ---------------- refresh and filter

    void Refresh()
    {
        var values = TunablesEditor.Current(_run);
        if (Structure(values) != _structure) { Build(); return; }
        _showing = true;
        try
        {
            foreach (var row in _rows.Values) row.Show(TunablePath.Get(values, row.Path));
        }
        finally { _showing = false; }
        _shownHash = TunablesEditor.CurrentHash(_run);
        ShowStatus();
    }

    /// <summary>Changes that need new rows: list lengths and the stage names that the drop lists offer.</summary>
    static string Structure(Tunables values)
    {
        var parts = new List<string>(values.Heat.Stages.Select(s => s.Name));
        void Walk(object obj, string path)
        {
            foreach (var f in TunablePath.Fields(obj.GetType()))
            {
                object? v = f.GetValue(obj);
                if (v is null || TunablePath.IsScalar(f.FieldType)) continue;
                if (v is IList list)
                {
                    if (TunablePath.IsScalar(TunablePath.ElementType(f.FieldType)!)) continue;
                    parts.Add($"{path}.{f.Name}#{list.Count}:" + string.Join(",", list.Cast<object>().Select(i => i.GetType().GetField("Name")?.GetValue(i))));
                    foreach (var item in list) Walk(item!, path + "." + f.Name);
                }
                else Walk(v, path + "." + f.Name);
            }
        }
        Walk(values, "");
        return string.Join("|", parts);
    }

    void Filter()
    {
        string q = _search.Text.Trim().ToLowerInvariant();
        foreach (var section in _sections)
        {
            bool sectionMatch = q.Length > 0 && section.Name.ToLowerInvariant().Contains(q);
            int shown = 0;
            foreach (var row in section.Rows)
            {
                row.Box.SelfActive = q.Length == 0 || sectionMatch || row.Search.Contains(q);
                if (row.Box.SelfActive) shown++;
            }
            section.Outer.SelfActive = q.Length == 0 || shown > 0;
            if (q.Length > 0 && shown > 0) section.Toggle.IsSelected = true;
        }
    }

    // ---------------- status

    /// <summary>Runs one change and shows its result. A failure that is not a rejected value is a bug: it reports like any mod error.</summary>
    void Commit(Func<string?> change)
    {
        string? error = null;
        Errors.Guard("tunables menu change", () => error = change());
        Error = error;
        if (error is not null) Log.Info($"tunables menu: rejected: {error}");
        Refresh();
    }

    void ShowStatus()
    {
        _status.Text = Error is { } e ? $"<bad>{Xml(e)}</bad>"
            : Strings.FormatText("HotCargoTunablesMenu/StatusFmt", TunablesEditor.CurrentHash(_run));
    }

    // ---------------- small widgets

    static LayoutBox Group(LayoutBox parent, string titleXml)
    {
        var group = new LayoutBox(WidgetRules.Instance.CategoryBox);
        group.NineSlice.Flags = NineSliceFlags.None;
        group.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        group.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        group.Children.WidgetPadding = new Vector2(8f);
        group.Children.BorderPadding = new Borders(20f, 4f, 0f, 4f);
        var title = new Label { PercentileWidth = 100f };
        title.TextRenderer.XmlFormatting = true;
        title.TextRenderer.HAlignment = HAlignment.Left;
        title.TextRenderer.OversizeMode = OversizeMode.Wrap;
        title.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        title.Text = titleXml;
        group.AddChild(title);
        parent.AddChild(group);
        return group;
    }

    static Label SmallLabel(string text, Rect percent)
    {
        var label = new Label { PercentileRect = percent };
        label.TextRenderer.FontSize = 14;
        label.TextRenderer.HAlignment = HAlignment.Left;
        label.TextRenderer.OversizeMode = OversizeMode.Shrink;
        label.Text = text;
        return label;
    }

    static Button SmallButton(string text, Rect percent)
    {
        var b = new Button { PercentileRect = percent, Text = text };
        b.Left = 2f;
        return b;
    }

    /// <summary>The second line of a row: the description, the default and when a change applies.</summary>
    static string Info(FieldInfo f, object? defaultValue)
    {
        var parts = new List<string>();
        string description = TunableDocs.Describe(f);
        if (description.Length > 0) parts.Add(Xml(description));
        if (defaultValue is not null && (TunablePath.IsScalar(f.FieldType) || TunablePath.ElementType(f.FieldType) is { } e && TunablePath.IsScalar(e)))
            parts.Add($"<gray>{Strings.FormatText("HotCargoTunablesMenu/DefaultFmt", Xml(TunablesEditor.Format(defaultValue)))}</gray>");
        var applies = TunableTiming.Of(f);
        if (applies != Applies.Now) parts.Add($"<limited_color>{Text("Applies" + applies)}</limited_color>");
        return string.Join("  ", parts);
    }

    static string Xml(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    static string Text(string key) => Strings.GetText("HotCargoTunablesMenu/" + key);
}

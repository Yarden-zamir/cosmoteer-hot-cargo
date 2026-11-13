using System.Globalization;
using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Input;
using HotCargo.Core;

namespace HotCargo.ShipAi;

/// <summary>
/// Spec 410, 419 to 422. The tactics editor of one ship: Auto switch, presets (built-in and personal), copy and paste, apply to the
/// selected ships, and one block per row: one line per condition term, then the action and its parameter. Ctrl+K opens it for the selected ship (or the first own ship). It only reads sim state and sends TacticsInput:
/// each edit sends the whole row list, and the window shows the sim's rows again when another player changes them.
/// </summary>
internal sealed class TacticsEditor : WindowBox
{
    static TacticsEditor? _open;
    /// <summary>In-game copy buffer, for platforms without a text clipboard (headless test games).</summary>
    static string? _copied;

    readonly Run _run;
    readonly Ship _ship;
    readonly ToggleButton _autoToggle;
    readonly DropList _presets;
    readonly TextEditField _presetName;
    readonly Button _deletePreset;
    readonly Button _toSelected;
    /// <summary>The entries of the preset list: the built-ins, then the personal presets (Personal = their name).</summary>
    readonly List<(string Text, string? Personal)> _presetEntries = [];
    readonly Label _status;
    readonly LayoutBox _list;
    readonly Button _add;
    readonly List<Label> _markers = [];

    List<TacticRow> _rows = [];
    bool _auto;
    string _shownText = "";
    bool _shownAuto;
    string? _sentText;
    bool _sentAuto;
    double _sentAt;
    bool _dirty = true;
    bool _updating;
    double _nextRefresh;
    string? _note;
    double _noteUntil;

    /// <summary>Real seconds that a sent edit waits for the sim before the window takes the sim's rows again. Longer than MP input delay.</summary>
    const double SendWaitSeconds = 3.0;

    static string Text(string key) => Strings.GetText("HotCargoShipAi/" + key);

    static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoShipAi/" + key, args);

    internal static bool IsOpen => _open?.Parent is not null;

    /// <summary>Frame hook: the hotkey, and the refresh of the open window.</summary>
    internal static void OnFrame(Run run)
    {
        if (App.Keyboard.HotkeyPressed(ViKey.Control, ViKey.K))
        {
            if (IsOpen) Close();
            else if (DefaultShip(run) is { } ship) Open(run, ship);
            else Ux.UxKit.CenterText(run.Game, Text("NoShip"));
        }
        if (_open is null) return;
        if (_open.Parent is null || _open._run != run || !_open._ship.ExistsInSim(run.Game.Sim, falseIfNoParts: false)) { Close(); return; }
        _open.Refresh();
    }

    /// <summary>The first selected ship that the local player commands, else the local player's first ship.</summary>
    static Ship? DefaultShip(Run run)
    {
        var sim = run.Game.Sim;
        return sim.PlayerInput.SelectedCommandableShips.OrderBy(s => s.UniqueID).FirstOrDefault()
            ?? sim.Ships.HumanPlayerShips.Where(s => s.Commands.IsCommandableByLocalPlayer).OrderBy(s => s.UniqueID).FirstOrDefault();
    }

    internal static void Open(Run run, Ship ship)
    {
        Close();
        _open = new TacticsEditor(run, ship);
        run.Game.Gui.FloatingWindows.AddChild(_open);
    }

    internal static void Close()
    {
        _open?.Unparent();
        _open = null;
    }

    /// <summary>
    /// Autotest: does what a click does. open | close | auto on|off | apply &lt;preset or personal name&gt; | add | remove &lt;row&gt; |
    /// up &lt;row&gt; | cond &lt;row&gt; &lt;Cond&gt; [term] | param &lt;row&gt; &lt;n&gt; [term] | act &lt;row&gt; &lt;Act&gt; | actparam &lt;row&gt; &lt;n&gt; |
    /// addterm &lt;row&gt; | removeterm &lt;row&gt; &lt;term&gt; | not &lt;row&gt; &lt;term&gt; | or &lt;row&gt; &lt;term&gt; | copy | paste [text] |
    /// savepreset &lt;name&gt; | deletepreset &lt;name&gt; | toselected. Rows and terms count from 1, like the window shows them.
    /// </summary>
    internal static void TestCommand(Run run, Ship ship, string arg)
    {
        var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length == 0) throw new InvalidOperationException("tactics_editor needs a verb");
        if (a[0] == "open") { Open(run, ship); return; }
        if (a[0] == "close") { Close(); return; }
        var e = _open ?? throw new InvalidOperationException("the tactics editor is not open");
        int Num(int i) => int.Parse(a[i], CultureInfo.InvariantCulture);
        int Row() => Num(1) - 1;
        int TermAt(int i) => a.Length > i ? Num(i) - 1 : 0;
        string Rest() => string.Join(' ', a.Skip(1));
        switch (a[0])
        {
            case "auto": e._autoToggle.IsSelected = a[1] == "on"; break;
            case "apply":
                int index = e._presetEntries.FindIndex(p => p.Personal == Rest());
                if (index < 0) index = Array.IndexOf(Enum.GetValues<Preset>(), Enum.Parse<Preset>(a[1]));
                e._presets.SelectedWidget = e._presets.ListBox.Children[index];
                e.ApplyPreset();
                break;
            case "add": e.AddRow(); break;
            case "remove": e._rows.RemoveAt(Row()); e.SendRows(); break;
            case "up": e.Swap(Row(), Row() - 1); break;
            case "cond": e.EditTerm(Row(), TermAt(3), t => ShipAiModel.WithCond(t, Enum.Parse<Cond>(a[2]))); break;
            case "param": e.EditTerm(Row(), TermAt(3), t => t with { Param = Num(2) }); break;
            case "not": e.EditTerm(Row(), TermAt(2), t => t with { Not = !t.Not }); break;
            case "or": e.EditTerm(Row(), TermAt(2), t => t with { Or = !t.Or }); break;
            case "addterm": e.Edit(Row(), ShipAiModel.AddTerm(e._rows[Row()])); break;
            case "removeterm": e.Edit(Row(), ShipAiModel.RemoveTerm(e._rows[Row()], TermAt(2))); break;
            case "act": e.Edit(Row(), ShipAiModel.WithAct(e._rows[Row()], Enum.Parse<Act>(a[2]))); break;
            case "actparam": e.Edit(Row(), e._rows[Row()] with { ActParam = Num(2) }); break;
            case "copy": e.Copy(); break;
            case "paste":
                if (a.Length > 1) _copied = a[1];
                e.Paste();
                break;
            case "savepreset": e._presetName.Text = Rest(); e.SavePreset(); break;
            case "deletepreset":
                e._presets.SelectedWidget = e._presets.ListBox.Children[e._presetEntries.FindIndex(p => p.Personal == Rest())];
                e.DeletePreset();
                break;
            case "toselected": e.ApplyToSelected(); break;
            default: throw new InvalidOperationException($"unknown tactics_editor verb '{a[0]}'");
        }
    }

    /// <summary>The own ships that "Apply to selected" changes: the selected ships that the local player commands, without this one.</summary>
    IEnumerable<Ship> SelectedOthers() =>
        _run.Game.Sim.PlayerInput.SelectedCommandableShips.Where(s => s != _ship && s.IsHumanPlayerShip).OrderBy(s => s.UniqueID);

    TacticsEditor(Run run, Ship ship)
    {
        _run = run;
        _ship = ship;
        TitleText = Format("TitleFmt", ship.Metadata.PlainShipName);
        TitleLabel.TextRenderer.HAlignment = HAlignment.Left;
        TitleLabel.TextRenderer.Padding = new Borders(10f, 0f, 0f, 0f);
        Size = new Vector2(1280f, 720f);
        ResizeController.MinSize = new Vector2(1100f, 420f);

        var page = new ScrollBox();
        page.AnchorRect = AnchorPresets.Fill;
        page.NineSlice.Flags = NineSliceFlags.None;
        page.ScrollBarController.ShowHorizontal = ScrollBarShowMode.Never;
        page.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        page.Children.BorderPadding = new Borders(16f, 12f, 16f, 12f);
        page.Children.WidgetPadding = new Vector2(8f);
        AddChild(page);

        var bar = Strip(40f);
        _autoToggle = new ToggleButton(ToggleButton.Style.Check) { Text = Text("Auto") };
        _autoToggle.ToolTips.TextProvider = new Ux.LiveText(() => Text("AutoTip"));
        _autoToggle.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            _auto = _autoToggle.IsSelected;
            Send();
        };
        Place(bar, _autoToggle, 0f, 26f);
        _presets = new DropList();
        _presets.ToolTips.TextProvider = new Ux.LiveText(() => Text("PresetTip"));
        _presets.ListBox.SelectionManager.WidgetSelected += (_, _) => UpdatePresetButtons();
        Place(bar, _presets, 26f, 22f);
        Place(bar, NewButton("Apply", ApplyPreset), 48f, 9f);
        _deletePreset = NewButton("DeletePreset", DeletePreset);
        Place(bar, _deletePreset, 57f, 9f);
        Place(bar, NewButton("Copy", Copy), 66f, 11f);
        Place(bar, NewButton("Paste", Paste), 77f, 11f);
        Place(bar, NewButton("Clear", () => { _rows.Clear(); SendRows(); }), 88f, 12f);
        page.Children.Add(bar);

        var bar2 = Strip(36f);
        _presetName = new TextEditField(TextEditField.Style.SingleLine) { MaxTextLength = ShipAiModel.MaxPresetName };
        _presetName.ToolTips.TextProvider = new Ux.LiveText(() => Text("PresetNameTip"));
        Place(bar2, _presetName, 26f, 22f);
        Place(bar2, NewButton("SavePreset", SavePreset), 48f, 18f);
        _toSelected = NewButton("ToSelected", ApplyToSelected);
        _toSelected.ToolTips.TextProvider = new Ux.LiveText(() => Text("ToSelectedTip"));
        Place(bar2, _toSelected, 66f, 34f);
        page.Children.Add(bar2);
        FillPresets(null);

        _status = NewLabel(18);
        page.Children.Add(_status);

        _list = new LayoutBox();
        _list.NineSlice.Flags = NineSliceFlags.None;
        _list.PercentileWidth = 100f;
        _list.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        _list.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        _list.Children.WidgetPadding = new Vector2(4f);
        page.Children.Add(_list);

        _add = NewButton("AddRow", AddRow);
        _add.Height = 34f;
        _add.PercentileWidth = 30f;
        page.Children.Add(_add);

        var help = NewLabel(15);
        help.Text = Text("Help");
        page.Children.Add(help);

        Parented += (_, _) =>
        {
            var parent = Parent ?? throw new InvalidOperationException("tactics editor parented without a parent");
            ScreenRect = Rect.CenteredOn(parent.RectProvider.ScreenRect.Size / 2f, Size);
            BoundsProvider = parent.RectProvider;
        };
        Unparenting += (_, _) => BoundsProvider = null;
        WidgetRules.AddDialogTweens(this);
        WidgetRules.AddBlur(this);
        LoadFromSim();
    }

    // ---- Widgets

    static SimpleBox Strip(float height)
    {
        var box = new SimpleBox();
        box.NineSlice.Flags = NineSliceFlags.None;
        box.PercentileWidth = 100f;
        box.Height = height;
        return box;
    }

    static void Place(SimpleBox box, Widget widget, float left, float width)
    {
        widget.PercentileRect = new Rect(left, 0f, width, 100f);
        widget.Right = -6f;
        box.AddChild(widget);
    }

    static Label NewLabel(int fontSize)
    {
        var label = new Label();
        label.NineSlice.Flags = NineSliceFlags.None;
        label.PercentileWidth = 100f;
        label.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        label.TextRenderer.XmlFormatting = true;
        label.TextRenderer.FontSize = fontSize;
        label.TextRenderer.HAlignment = HAlignment.Left;
        return label;
    }

    static Button NewButton(string key, Action click)
    {
        var b = new Button();
        b.CopySettingsFrom(WidgetRules.Instance.FlatButton);
        b.Text = Text(key);
        b.Clicked += (_, _) => Errors.Guard("tactics editor " + key, click);
        return b;
    }

    /// <summary>A drop list of values with display texts. The current value is selected; a value outside the list is added first.</summary>
    DropList Choices(IEnumerable<int> values, int current, Func<int, string> name, Action<int> picked)
    {
        var list = values.ToList();
        if (!list.Contains(current)) list.Insert(0, current);
        var drop = new DropList();
        foreach (int v in list) drop.ListBox.Children.Add(new ListItem { Text = name(v) });
        drop.SelectedWidget = drop.ListBox.Children[list.IndexOf(current)];
        drop.ListBox.SelectionManager.WidgetSelected += (_, _) =>
        {
            if (_updating || drop.SelectedWidget is not { } w) return;
            int v = list[drop.ListBox.Children.IndexOf(w)];
            if (v != current) Errors.Guard("tactics editor pick", () => picked(v));
        };
        return drop;
    }

    static string ParamName(ParamKind kind, int value) => kind switch
    {
        ParamKind.Percent => Format("PercentFmt", value),
        ParamKind.Distance => Format("DistanceFmt", value),
        ParamKind.Count => Format("CountFmt", value),
        ParamKind.PartKind => Text("PartKind_" + (PartKind)value),
        ParamKind.ShipClass => Text("ShipClass_" + (ShipClass)value),
        ParamKind.GuardWho => Text("GuardWho_" + (GuardWho)value),
        ParamKind.Group => value == 0 ? Text("GroupAll") : Format("GroupFmt", value),
        ParamKind.Stage => Format("StageFmt", value),
        ParamKind.Seconds => Format("SecondsFmt", value),
        ParamKind.Side => Text("Side_" + (Side)value),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    void BuildRows()
    {
        _updating = true;
        try
        {
            _list.Children.Clear();
            _markers.Clear();
            for (int i = 0; i < _rows.Count; i++) _list.Children.Add(BuildRow(i));
            if (_rows.Count == 0)
            {
                var empty = NewLabel(16);
                empty.Text = Text("NoRows");
                _list.Children.Add(empty);
            }
            _add.SelfInputActive = _rows.Count < ShipAiModel.MaxRows;
        }
        finally { _updating = false; }
    }

    /// <summary>One block per row: a line per condition term; the first line also holds the action and the row buttons.</summary>
    LayoutBox BuildRow(int i)
    {
        var row = _rows[i];
        var block = new LayoutBox();
        block.NineSlice.Flags = NineSliceFlags.None;
        block.PercentileWidth = 100f;
        block.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        block.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        block.Children.WidgetPadding = new Vector2(2f);
        for (int j = 0; j < row.Terms.Length; j++)
        {
            var line = Strip(34f);
            BuildTerm(line, i, j);
            if (j == 0) BuildAction(line, i);
            block.Children.Add(line);
        }
        return block;
    }

    void BuildTerm(SimpleBox line, int i, int j)
    {
        var row = _rows[i];
        var term = row.Terms[j];
        if (j == 0)
        {
            var marker = new Label();
            marker.TextRenderer.XmlFormatting = true;
            marker.TextRenderer.FontSize = 18;
            marker.TextRenderer.HAlignment = HAlignment.Center;
            _markers.Add(marker);
            Place(line, marker, 0f, 4f);
            var when = new Label { Text = Text("If") };
            when.TextRenderer.HAlignment = HAlignment.Center;
            Place(line, when, 4f, 5f);
        }
        else Place(line, XmlButton(Text(term.Or ? "Or" : "And"), Text("ConnectorTip"), () => EditTerm(i, j, t => t with { Or = !t.Or })), 4f, 5f);
        Place(line, XmlButton(Text(term.Not ? "NotOn" : "NotOff"), Text("NotTip"), () => EditTerm(i, j, t => t with { Not = !t.Not })), 9f, 5f);
        Place(line, Choices(Enum.GetValues<Cond>().Select(c => (int)c), (int)term.Cond, v => Text("Cond_" + (Cond)v),
            v => EditTerm(i, j, t => ShipAiModel.WithCond(t, (Cond)v))), 14f, 18f);
        var kind = ShipAiModel.ParamOf(term.Cond);
        if (kind != ParamKind.None)
            Place(line, Choices(ShipAiModel.ChoicesOf(kind, _run.T.Heat.Stages.Length), term.Param, v => ParamName(kind, v), v => EditTerm(i, j, t => t with { Param = v })), 32f, 9f);
        if (row.Terms.Length > 1) Place(line, NewButton("RemoveTerm", () => Edit(i, ShipAiModel.RemoveTerm(_rows[i], j))), 41f, 3f);
    }

    void BuildAction(SimpleBox line, int i)
    {
        var row = _rows[i];
        var arrow = new Label { Text = "->" };
        arrow.TextRenderer.HAlignment = HAlignment.Center;
        Place(line, arrow, 44f, 2f);
        Place(line, Choices(Enum.GetValues<Act>().Select(a => (int)a), (int)row.Act, v => Text("Act_" + (Act)v),
            v => Edit(i, ShipAiModel.WithAct(_rows[i], (Act)v))), 46f, 20f);
        var actKind = ShipAiModel.ParamOf(row.Act);
        if (actKind != ParamKind.None)
            Place(line, Choices(ShipAiModel.ChoicesOf(actKind, _run.T.Heat.Stages.Length), row.ActParam, v => ParamName(actKind, v), v => Edit(i, _rows[i] with { ActParam = v })), 66f, 10f);
        var addTerm = NewButton("AddTerm", () => Edit(i, ShipAiModel.AddTerm(_rows[i])));
        addTerm.SelfInputActive = row.Terms.Length < ShipAiModel.MaxTerms;
        Place(line, addTerm, 76f, 9f);
        var up = NewButton("Up", () => Swap(i, i - 1));
        up.SelfInputActive = i > 0;
        Place(line, up, 85f, 5f);
        var down = NewButton("Down", () => Swap(i, i + 1));
        down.SelfInputActive = i < _rows.Count - 1;
        Place(line, down, 90f, 5f);
        Place(line, NewButton("Remove", () => { _rows.RemoveAt(i); SendRows(); }), 95f, 5f);
    }

    /// <summary>A flat button whose text uses markup (the NOT and AND/OR toggles).</summary>
    static Button XmlButton(string text, string tip, Action click)
    {
        var b = new Button();
        b.CopySettingsFrom(WidgetRules.Instance.FlatButton);
        b.StateNormalTextRenderer.XmlFormatting = true;
        b.StateHighlightedTextRenderer.XmlFormatting = true;
        b.StatePressedTextRenderer.XmlFormatting = true;
        b.Text = text;
        b.ToolTips.TextProvider = new Ux.LiveText(() => tip);
        b.Clicked += (_, _) => Errors.Guard("tactics editor toggle", click);
        return b;
    }

    // ---- Edits

    void AddRow()
    {
        if (_rows.Count >= ShipAiModel.MaxRows) return;
        _rows.Add(new TacticRow(Cond.Always, 0, Act.TargetNearest, 0));
        SendRows();
    }

    void EditTerm(int i, int j, Func<Term, Term> change) => Edit(i, ShipAiModel.WithTerm(_rows[i], j, change(_rows[i].Terms[j])));

    void Edit(int i, TacticRow row)
    {
        _rows[i] = row;
        SendRows();
    }

    void Swap(int a, int b)
    {
        if (b < 0 || b >= _rows.Count) return;
        (_rows[a], _rows[b]) = (_rows[b], _rows[a]);
        SendRows();
    }

    void SendRows()
    {
        _dirty = true;
        Send();
    }

    void Send()
    {
        string text = ShipAiModel.Encode(_rows);
        (_sentText, _sentAuto, _sentAt) = (text, _auto, App.Clock.Time.Seconds);
        (_shownText, _shownAuto) = (text, _auto);
        _run.Game.NetManager.EnqueueInput(new TacticsInput(_ship, text, _auto));
    }

    /// <summary>
    /// Autotests never touch the system clipboard: test games run on a PC that a person uses. They use the in-game buffer only.
    /// </summary>
    static bool UseSystemClipboard => !Dev.Autotest.IsActive;

    void Copy()
    {
        _copied = ShipAiModel.Encode(_rows);
        if (UseSystemClipboard) App.Platform.SetClipboardText(_copied);
        Note(Text("Copied"));
    }

    /// <summary>Takes tactics text from the system clipboard, else from the in-game buffer (the last copy in this game).</summary>
    void Paste()
    {
        string? text = UseSystemClipboard ? App.Platform.GetClipboardText()?.Trim() : null;
        if (text is null || !text.StartsWith(ShipAiModel.Prefix, StringComparison.Ordinal)) text = _copied;
        if (text is null) { Note(Text("PasteEmpty")); return; }
        try { _rows = ShipAiModel.Decode(text); }
        catch (FormatException ex) { Note(Format("PasteBadFmt", ex.Message)); return; }
        SendRows();
        Note(Text("Pasted"));
    }

    // ---- Presets (spec 406, 422) and fleet apply (spec 421)

    /// <summary>Fills the preset list: the built-ins, then the personal presets. select: the personal preset to select, else the first entry.</summary>
    void FillPresets(string? select)
    {
        _presetEntries.Clear();
        _presets.ListBox.Children.Clear();
        foreach (var p in Enum.GetValues<Preset>())
        {
            _presetEntries.Add((ShipAiModel.Encode(ShipAiModel.PresetRows(p)), null));
            _presets.ListBox.Children.Add(new ListItem { Text = Text("Preset_" + p) });
        }
        foreach (var (name, text) in TacticsPresets.Load())
        {
            _presetEntries.Add((text, name));
            _presets.ListBox.Children.Add(new ListItem { Text = Format("PersonalFmt", name) });
        }
        int index = Math.Max(0, _presetEntries.FindIndex(p => p.Personal is not null && p.Personal == select));
        _presets.SelectedWidget = _presets.ListBox.Children[index];
        UpdatePresetButtons();
    }

    (string Text, string? Personal) SelectedPreset =>
        _presetEntries[_presets.ListBox.Children.IndexOf(_presets.SelectedWidget ?? throw new InvalidOperationException("no preset selected"))];

    void UpdatePresetButtons()
    {
        if (_deletePreset is null || _presets.SelectedWidget is null) return;
        _deletePreset.SelfInputActive = SelectedPreset.Personal is not null;
    }

    void ApplyPreset()
    {
        _rows = ShipAiModel.Decode(SelectedPreset.Text);
        SendRows();
    }

    void SavePreset()
    {
        if (_rows.Count == 0) { Note(Text("PresetNoRows")); return; }
        if (ShipAiModel.CleanPresetName(_presetName.Text).Length == 0) { Note(Text("PresetNoName")); return; }
        string name = TacticsPresets.Save(_presetName.Text, ShipAiModel.Encode(_rows));
        FillPresets(name);
        Note(Format("PresetSavedFmt", name));
    }

    void DeletePreset()
    {
        if (SelectedPreset.Personal is not { } name) return;
        TacticsPresets.Delete(name);
        FillPresets(null);
        Note(Format("PresetDeletedFmt", name));
    }

    /// <summary>Spec 421: one tactics input per selected own ship, with this window's rows and Auto switch. The sim checks permission.</summary>
    void ApplyToSelected()
    {
        var ships = SelectedOthers().ToList();
        if (ships.Count == 0) { Note(Text("ToSelectedNone")); return; }
        string text = ShipAiModel.Encode(_rows);
        foreach (var ship in ships) _run.Game.NetManager.EnqueueInput(new TacticsInput(ship, text, _auto));
        Note(Format("ToSelectedFmt", ships.Count));
    }

    void Note(string text) => (_note, _noteUntil) = (text, App.Clock.Time.Seconds + 3.0);

    // ---- Sim state

    void LoadFromSim()
    {
        var t = ShipTactics.Of(_ship);
        (_shownText, _shownAuto) = (t?.Text ?? "", t?.Auto ?? false);
        _rows = t?.Rows.ToList() ?? [];
        _auto = _shownAuto;
        _updating = true;
        _autoToggle.IsSelected = _auto;
        _updating = false;
        _dirty = true;
    }

    void Refresh()
    {
        double now = App.Clock.Time.Seconds;
        if (now >= _nextRefresh)
        {
            _nextRefresh = now + 0.25;
            var t = ShipTactics.Of(_ship);
            string simText = t?.Text ?? "";
            bool simAuto = t?.Auto ?? false;
            if (_sentText is not null && ((simText == _sentText && simAuto == _sentAuto) || now > _sentAt + SendWaitSeconds)) _sentText = null;
            if (_sentText is null && (simText != _shownText || simAuto != _shownAuto)) LoadFromSim();
            bool commandable = _ship.Commands.IsCommandableByLocalPlayer;
            if (_list.SelfInputActive != commandable) { _list.SelfInputActive = commandable; _autoToggle.SelfInputActive = commandable; }
            int others = SelectedOthers().Count();
            _toSelected.Text = Format("ToSelectedButtonFmt", others);
            _toSelected.SelfInputActive = others > 0;
            if (!_dirty) UpdateStatus(t);
        }
        if (_dirty)
        {
            _dirty = false;
            BuildRows();
            UpdateStatus(ShipTactics.Of(_ship));
        }
    }

    void UpdateStatus(ShipTactics? t)
    {
        string status;
        if (_note is not null && App.Clock.Time.Seconds < _noteUntil) status = _note;
        else if (!_run.T.ShipAi.Enabled) status = Text("StatusDisabled");
        else if (t is not { Auto: true }) status = Text("StatusOff");
        else if (t.Waiting) status = Text("StatusWaiting");
        else if (t.Target is { } id && _run.Game.Sim[id] is Ship target) status = Format("StatusTargetFmt", target.Metadata.XmlShipName);
        else status = Text("StatusActive");
        _status.Text = status;
        // Row markers: the rows in effect now (the target row, the move row and the matched weapons rows) are bright.
        bool live = t is { Auto: true, Waiting: false } && t.Text == ShipAiModel.Encode(_rows);
        for (int i = 0; i < _markers.Count; i++)
        {
            bool on = live && t is not null && (i == t.TargetRow || i == t.MoveRow || i == t.FacingRow ||
                ((t.Matched & (1 << i)) != 0 && ShipAiModel.KindOf(_rows[i].Act) == ActKind.Weapons));
            _markers[i].Text = on ? $"<good>{i + 1}</good>" : $"<gray>{i + 1}</gray>";
        }
    }
}

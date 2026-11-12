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
/// Spec 410. The tactics editor of one ship: Auto switch, presets, copy and paste, and one line per row with a condition, an action
/// and their parameters. Ctrl+K opens it for the selected ship (or the first own ship). It only reads sim state and sends TacticsInput:
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
    /// Autotest: does what a click does. open | close | auto on|off | apply &lt;preset&gt; | add | remove &lt;row&gt; | up &lt;row&gt; |
    /// cond &lt;row&gt; &lt;Cond&gt; | act &lt;row&gt; &lt;Act&gt; | copy | paste [text]. Rows count from 1, like the window shows them.
    /// </summary>
    internal static void TestCommand(Run run, Ship ship, string arg)
    {
        var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length == 0) throw new InvalidOperationException("tactics_editor needs a verb");
        if (a[0] == "open") { Open(run, ship); return; }
        if (a[0] == "close") { Close(); return; }
        var e = _open ?? throw new InvalidOperationException("the tactics editor is not open");
        int Row() => int.Parse(a[1], CultureInfo.InvariantCulture) - 1;
        switch (a[0])
        {
            case "auto": e._autoToggle.IsSelected = a[1] == "on"; break;
            case "apply":
                e._presets.SelectedWidget = e._presets.ListBox.Children[Array.IndexOf(Enum.GetValues<Preset>(), Enum.Parse<Preset>(a[1]))];
                e._rows = ShipAiModel.PresetRows(Enum.Parse<Preset>(a[1])).ToList();
                e.SendRows();
                break;
            case "add": e.AddRow(); break;
            case "remove": e._rows.RemoveAt(Row()); e.SendRows(); break;
            case "up": e.Swap(Row(), Row() - 1); break;
            case "cond": e.Edit(Row(), ShipAiModel.WithCond(e._rows[Row()], Enum.Parse<Cond>(a[2]))); break;
            case "act": e.Edit(Row(), ShipAiModel.WithAct(e._rows[Row()], Enum.Parse<Act>(a[2]))); break;
            case "copy": e.Copy(); break;
            case "paste":
                if (a.Length > 1) _copied = a[1];
                e.Paste();
                break;
            default: throw new InvalidOperationException($"unknown tactics_editor verb '{a[0]}'");
        }
    }

    TacticsEditor(Run run, Ship ship)
    {
        _run = run;
        _ship = ship;
        TitleText = Format("TitleFmt", ship.Metadata.PlainShipName);
        TitleLabel.TextRenderer.HAlignment = HAlignment.Left;
        TitleLabel.TextRenderer.Padding = new Borders(10f, 0f, 0f, 0f);
        Size = new Vector2(1180f, 640f);
        ResizeController.MinSize = new Vector2(1000f, 420f);

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
        Place(bar, _autoToggle, 0f, 30f);
        _presets = new DropList();
        foreach (var p in Enum.GetValues<Preset>()) _presets.ListBox.Children.Add(new ListItem { Text = Text("Preset_" + p) });
        _presets.SelectedWidget = _presets.ListBox.Children[0];
        _presets.ToolTips.TextProvider = new Ux.LiveText(() => Text("PresetTip"));
        Place(bar, _presets, 30f, 22f);
        Place(bar, NewButton("Apply", () =>
        {
            int index = _presets.ListBox.Children.IndexOf(_presets.SelectedWidget!);
            _rows = ShipAiModel.PresetRows(Enum.GetValues<Preset>()[index]).ToList();
            SendRows();
        }), 52f, 12f);
        Place(bar, NewButton("Copy", Copy), 64f, 12f);
        Place(bar, NewButton("Paste", Paste), 76f, 12f);
        Place(bar, NewButton("Clear", () => { _rows.Clear(); SendRows(); }), 88f, 12f);
        page.Children.Add(bar);

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

    SimpleBox BuildRow(int i)
    {
        var row = _rows[i];
        var box = Strip(34f);
        var marker = new Label();
        marker.TextRenderer.XmlFormatting = true;
        marker.TextRenderer.FontSize = 18;
        marker.TextRenderer.HAlignment = HAlignment.Center;
        _markers.Add(marker);
        Place(box, marker, 0f, 4f);
        Place(box, Choices(Enum.GetValues<Cond>().Select(c => (int)c), (int)row.Cond, v => Text("Cond_" + (Cond)v),
            v => Edit(i, ShipAiModel.WithCond(_rows[i], (Cond)v))), 4f, 26f);
        var condKind = ShipAiModel.ParamOf(row.Cond);
        if (condKind != ParamKind.None)
            Place(box, Choices(ShipAiModel.ChoicesOf(condKind), row.CondParam, v => ParamName(condKind, v), v => Edit(i, _rows[i] with { CondParam = v })), 30f, 11f);
        var arrow = new Label { Text = "->" };
        arrow.TextRenderer.HAlignment = HAlignment.Center;
        Place(box, arrow, 41f, 3f);
        Place(box, Choices(Enum.GetValues<Act>().Select(a => (int)a), (int)row.Act, v => Text("Act_" + (Act)v),
            v => Edit(i, ShipAiModel.WithAct(_rows[i], (Act)v))), 44f, 28f);
        var actKind = ShipAiModel.ParamOf(row.Act);
        if (actKind != ParamKind.None)
            Place(box, Choices(ShipAiModel.ChoicesOf(actKind), row.ActParam, v => ParamName(actKind, v), v => Edit(i, _rows[i] with { ActParam = v })), 72f, 13f);
        var up = NewButton("Up", () => Swap(i, i - 1));
        up.SelfInputActive = i > 0;
        Place(box, up, 85f, 5f);
        var down = NewButton("Down", () => Swap(i, i + 1));
        down.SelfInputActive = i < _rows.Count - 1;
        Place(box, down, 90f, 5f);
        Place(box, NewButton("Remove", () => { _rows.RemoveAt(i); SendRows(); }), 95f, 5f);
        return box;
    }

    // ---- Edits

    void AddRow()
    {
        if (_rows.Count >= ShipAiModel.MaxRows) return;
        _rows.Add(new TacticRow(Cond.Always, 0, Act.TargetNearest, 0));
        SendRows();
    }

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
            bool on = live && t is not null && (i == t.TargetRow || i == t.MoveRow ||
                ((t.Matched & (1 << i)) != 0 && ShipAiModel.KindOf(_rows[i].Act) == ActKind.Weapons));
            _markers[i].Text = on ? $"<good>{i + 1}</good>" : $"<gray>{i + 1}</gray>";
        }
    }
}

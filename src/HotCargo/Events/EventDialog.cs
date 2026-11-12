using Cosmoteer;
using Cosmoteer.Game.Gui;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Gui;
using Cosmoteer.Modes.Career.Comms;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Hierarchy;
using Halfling.Gui.Components.Rects;
using HotCargo.Core;

namespace HotCargo.Events;

/// <summary>
/// The event window: a comms-style window with the event text and one button per choice, like the game's comms dialogue tab.
/// It only reads sim state and sends EventChoiceInput. The window cannot close while a choice waits, like FTL.
/// </summary>
internal sealed class EventDialog : WindowBox
{
    static EventDialog? _open;
    static Run? _run;
    /// <summary>Serial of the last result page that this client closed. Local UI state, not saved.</summary>
    static int _resultShown;
    static bool _pausedByUs;
    static bool _broken;

    /// <summary>True while the window shows. For autotests.</summary>
    internal static bool IsShown => _open is { Parent: not null };

    /// <summary>The window rect and the screen rect, for autotests.</summary>
    internal static string Rects => _open is { Parent: not null } w ? $"window {w.ScreenRect} screen {w.Parent.RectProvider.ScreenRect}" : "no event window";

    static CommsGuiRules Comms => GameApp.Rules.CareerMode.Gui.Comms;

    readonly Run _forRun;
    readonly Label _body;
    readonly ScrollBox _pageBox;
    readonly LayoutBox _header;
    readonly LayoutBox<ImageButton, Widget> _buttons;
    EventPage? _page;
    bool _sent;
    double _sentAt;
    double _nextRefresh;
    /// <summary>Real seconds after a sent choice before the buttons work again on the same page. Longer than MP input delay.</summary>
    const double ResendSeconds = 3.0;

    /// <summary>Called each frame. Opens, updates or closes the window to match the sim state.</summary>
    internal static void Update(Run run)
    {
        if (_broken) return;
        if (_run != run) { _run = run; _resultShown = run.State.Events.LastResult?.Serial ?? 0; _open = null; _pausedByUs = false; }
        var sim = run.Game.Sim;
        if (sim is null) return;
        // After a win or a loss, the run-end dialog owns the screen and events no longer apply.
        var page = run.State.Result == RunResult.Running ? EventView.Current(run, sim, run.Game.LocalPlayerIndex, _resultShown) : null;
        try
        {
            if (page is null) { Close(run); return; }
            if (_open is null || _open.Parent is null)
            {
                // The run intro shows first. The event waits in the sim until the player closes the intro.
                if (Ui.CenterPanel.IsOpen(run.Game)) return;
                _open = new EventDialog(run);
                run.Game.Gui.FloatingWindows.AddChild(_open);
                PauseIfWanted(run);
            }
            _open.Show(page);
        }
        catch
        {
            // A broken window must not throw every frame. Events still work through the inputs; the test commands can pick choices.
            _broken = true;
            throw;
        }
    }

    static void Close(Run run)
    {
        if (_open is not null) { _open.Unparent(); _open = null; }
        if (_pausedByUs)
        {
            _pausedByUs = false;
            run.Game.NetManager.EnqueueInput(new MPSetPausedInput(paused: false));
        }
    }

    /// <summary>Single player only: pauses while the window is open. Headless test games have no viewer and stay unpaused.</summary>
    static void PauseIfWanted(Run run)
    {
        if (run.IsMultiplayer || GameApp.IsHeadlessMode || !run.T.Events.PauseOnEvent || run.Game.Sim.IsPaused.Displayed) return;
        _pausedByUs = true;
        run.Game.NetManager.EnqueueInput(new MPSetPausedInput(paused: true));
    }

    EventDialog(Run run)
    {
        _forRun = run;
        TitleLabel.TextRenderer.HAlignment = HAlignment.Left;
        TitleLabel.TextRenderer.Padding = new Borders(10f, 0f, 0f, 0f);
        CloseButton.SelfActive = false;
        Size = new Vector2(WindowWidth, 400f);
        ResizeController.MinSize = new Vector2(600f, MinHeight);

        var page = _pageBox = new ScrollBox();
        page.AnchorRect = AnchorPresets.Fill;
        page.NineSlice.Flags = NineSliceFlags.None;
        page.ScrollBarController.ShowHorizontal = ScrollBarShowMode.Never;
        page.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        page.Children.BorderPadding = new Borders(30f, 24f, 30f, 24f);
        page.Children.WidgetPadding = new Vector2(20f);
        AddChild(page);

        // Text with the comms dialogue icon on its left, as in CommDialogueTab.
        var header = _header = new LayoutBox();
        header.NineSlice.Flags = NineSliceFlags.None;
        header.PercentileWidth = 100f;
        header.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        header.DockedChildren.ClipChildren = false;
        header.Children.ClipChildren = false;
        header.Children.BorderPadding = new Borders(10f, 0f, 0f, 0f);
        var icon = new Image(Comms.DefaultDialogueLineIcon);
        icon.DockMode = DockMode.Left;
        icon.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        icon.AutoSize.AutoWidthMode = AutoSizeMode.Enable;
        icon.Sprite.RectProvider = new InnerRectProvider(icon) { InnerRectSize = Comms.DefaultDialogueLineIcon.Size, InnerRectMode = InnerRectMode.TopLeft };
        header.DockedChildren.Add(icon);
        _body = new Label();
        _body.NineSlice.Flags = NineSliceFlags.None;
        _body.PercentileWidth = 100f;
        _body.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        _body.TextRenderer.XmlFormatting = true;
        _body.TextRenderer.FontSize = 20;
        _body.TextRenderer.HAlignment = HAlignment.Left;
        header.Children.Add(_body);
        page.Children.Add(header);

        _buttons = new LayoutBox<ImageButton, Widget>();
        _buttons.NineSlice.Flags = NineSliceFlags.None;
        _buttons.PercentileWidth = 100f;
        _buttons.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        _buttons.Children.BorderPadding = new Borders(30f, 0f, 0f, 0f);
        _buttons.Children.WidgetPadding = new Vector2(12f);
        _buttons.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        _buttons.Children.ClipChildren = false;
        page.Children.Add(_buttons);

        Parented += OnParented;
        Unparenting += (_, _) => BoundsProvider = null;
        BeforeFrameDraw += (_, _) => FitHeight();
        WidgetRules.AddDialogTweens(this);
        WidgetRules.AddBlur(this);
    }

    const float WindowWidth = 760f;
    const float MinHeight = 240f;

    void OnParented(object? sender, ParentEventArgs e)
    {
        ScreenRect = Rect.CenteredOn(Parent.RectProvider.ScreenRect.Size / 2f, Size);
        BoundsProvider = Parent.RectProvider;
    }

    /// <summary>
    /// Fits the window height to the page: text, choices and paddings, at most 80 % of the screen (the page scrolls past that).
    /// It keeps the window center, so a short result page does not leave a tall empty window over the HUD.
    /// </summary>
    void FitHeight()
    {
        if (Parent is null) return;
        // Screen rects: the page fills the window through its anchors, so its Height property stays 0.
        float chrome = ScreenRect.Height - _pageBox.ScreenRect.Height;
        // Measured from the header top to the last button bottom: both move together when the page scrolls. The layout
        // stretches the button box to fill the page, so its own height would grow the window each frame.
        var last = _buttons.Children.Count > 0 ? _buttons.Children[_buttons.Children.Count - 1].ScreenRect : _header.ScreenRect;
        float content = last.Bottom - _header.ScreenRect.Top + _pageBox.Children.BorderPadding.Top + _pageBox.Children.BorderPadding.Bottom;
        float h = Math.Clamp(chrome + content, MinHeight, Parent.RectProvider.ScreenRect.Height * 0.8f);
        if (Math.Abs(h - ScreenRect.Height) < 1f) return;
        ScreenRect = Rect.CenteredOn(ScreenRect.Center, new Vector2(WindowWidth, h));
    }

    /// <summary>Rebuilds on a new page. Refreshes the enabled states once a second, because money and cargo change while the window is open.</summary>
    void Show(EventPage page)
    {
        bool newPage = _page?.Key != page.Key;
        double now = App.Clock.Time.Seconds;
        if (!newPage && now < _nextRefresh) return;
        _nextRefresh = now + 1.0;
        // The sim can reject a sent choice (the chooser can no longer pay). The page then stays, so the buttons come back.
        if (newPage || now > _sentAt + ResendSeconds) _sent = false;
        _page = page;
        TitleText = page.Title;
        _body.Text = page.Body;
        while (_buttons.Children.Count > page.Choices.Count) _buttons.Children.RemoveAt(_buttons.Children.Count - 1);
        for (int i = 0; i < page.Choices.Count; i++)
        {
            if (_buttons.Children.Count <= i) _buttons.Children.Add(NewButton(i));
            var b = _buttons.Children[i];
            b.Text = page.Choices[i].Text;
            b.SelfInputActive = page.Choices[i].Enabled && !_sent;
        }
    }

    ImageButton NewButton(int slot)
    {
        var b = new ImageButton();
        b.CopySettingsFrom(WidgetRules.Instance.FlatButton);
        b.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        b.AutoSize.MinHeight = 52f;
        foreach (var r in new[] { b.StateNormalTextRenderer, b.StateHighlightedTextRenderer, b.StatePressedTextRenderer, b.StateDisabledTextRenderer })
        {
            r.XmlFormatting = true;
            r.FontSize = 18;
            r.HAlignment = HAlignment.Left;
            r.Padding = new Borders(10f);
        }
        b.ImageSprite.CopySpriteFrom(Comms.DefaultDialogueLineIcon);
        b.ImageRectController.InnerRectMode = InnerRectMode.Left;
        b.ImageRectController.BorderPadding = new Borders(10f);
        b.ImageRectController.InnerRectSize = b.ImageSprite.DesiredSize;
        b.Clicked += (_, _) => OnClicked(slot);
        return b;
    }

    void OnClicked(int slot)
    {
        if (_page is not { } page || slot >= page.Choices.Count || _sent) return;
        var choice = page.Choices[slot];
        if (!choice.Enabled) return;
        if (choice.Index < 0)
        {
            _resultShown = Math.Max(_resultShown, page.Serial);
            return;
        }
        _sent = true;
        _sentAt = App.Clock.Time.Seconds;
        foreach (var b in _buttons.Children) b.SelfInputActive = false;
        _forRun.Game.NetManager.EnqueueInput(new EventChoiceInput(page.Serial, choice.Index));
    }
}

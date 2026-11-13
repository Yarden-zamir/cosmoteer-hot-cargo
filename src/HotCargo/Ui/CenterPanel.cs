using Cosmoteer.Game;
using Cosmoteer.Gui;
using Cosmoteer.Input;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using Halfling.Gui;
using HotCargo.Core;

namespace HotCargo.Ui;

/// <summary>
/// A centered HUD panel with a title, a body and one or two buttons, in the game's blurred panel style.
/// It does not pause the game and does not block input outside its own rectangle. Used by the run intro and the win panel.
/// </summary>
static class CenterPanel
{
    static readonly List<(Cosmoteer.Game.GameRoot Game, LayoutBox Box)> Open = [];

    /// <summary>Closes every open panel. For autotests.</summary>
    public static void CloseAll()
    {
        foreach (var (game, box) in Open) game.Gui.RootWidget.Children.Remove(box);
        Open.Clear();
    }

    /// <summary>Space kept between a panel and each side of the screen.</summary>
    const float ScreenMargin = 20f;

    /// <summary>
    /// A blurred panel box centered on the screen, with an automatic height. Its width is <paramref name="width"/>,
    /// or the screen width minus the margins on a smaller screen; it is checked each frame, so a resize keeps it on screen.
    /// The edges are set on both sides of the center anchor; a Width alone starts the box at the center and runs it off the right edge.
    /// </summary>
    public static LayoutBox NewBox(float width)
    {
        var box = new LayoutBox
        {
            AnchorRect = AnchorPresets.Center,
            Left = -width / 2f,
            Right = width / 2f,
            InputBlockMode = InputBlockMode.Auto,
        };
        box.BeforeFrameDraw += (_, _) =>
        {
            if (box.Root?.RootWidget is not { } root) return;
            float w = Math.Min(width, root.ScreenRect.Width - 2f * ScreenMargin);
            if (w <= 0f || Math.Abs(box.Right - box.Left - w) < 0.5f) return;
            box.Left = -w / 2f;
            box.Right = w / 2f;
        };
        box.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        box.AutoSize.Anchor = new Vector2(0.5f, 0.5f);
        box.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        box.Children.BorderPadding = new Borders(30f, 24f, 30f, 24f);
        WidgetRules.AddBlur(box);
        return box;
    }

    /// <summary>True while a panel is open in the game. The event window waits for it, so the two never stack.</summary>
    public static bool IsOpen(GameRoot game) => Open.Any(o => o.Game == game);

    /// <summary>The newest panel. For autotests that check the layout.</summary>
    internal static LayoutBox? Newest => Open.Count > 0 ? Open[^1].Box : null;

    /// <summary>
    /// Shows a panel. <paramref name="button"/> closes it. With <paramref name="primary"/>, that button comes first as the primary
    /// (Enter) button: it closes the panel and then runs its action, and <paramref name="button"/> becomes the secondary button.
    /// </summary>
    public static LayoutBox Show(GameRoot game, string title, Color titleColor, string bodyXml, string button, float width = 620f,
        (string Text, Action OnClick)? primary = null)
    {
        var box = NewBox(width);
        box.Children.WidgetPadding = new Vector2(16f);

        var titleLabel = Text(title, 44, center: true);
        titleLabel.TextRenderer.Font = WidgetRules.Instance.ShadowGlowFont;
        titleLabel.TextRenderer.Color = titleColor;
        box.AddChild(titleLabel);
        box.AddChild(Text(bodyXml, 18, center: false));

        var row = new LayoutBox { Height = 50f };
        row.NineSlice.Flags = NineSliceFlags.None;
        row.Children.LayoutAlgorithm = LayoutAlgorithms.CenterTop;
        row.Children.WidgetPadding = new Vector2(16f);
        void Close() { game.Gui.RootWidget.Children.Remove(box); Open.Remove((game, box)); }
        if (primary is { } p)
        {
            var main = new Button(WidgetRules.Instance.OkayButton) { Size = new Vector2(220f, 46f), Text = p.Text, Hotkey = Inputs.General.Confirm };
            main.Clicked += (_, _) => { Close(); Errors.Guard("center panel button", p.OnClick); };
            row.AddChild(main);
        }
        var close = new Button(primary is null ? WidgetRules.Instance.OkayButton : WidgetRules.Instance.FlatButton) { Size = new Vector2(220f, 46f), Text = button };
        if (primary is null) close.Hotkey = Inputs.General.Confirm;
        close.Clicked += (_, _) => Close();
        row.AddChild(close);
        box.AddChild(row);

        game.Gui.RootWidget.Children.InsertBelow(game.Gui.FloatingWindows, box);
        Open.Add((game, box));
        box.Animator.TweenColorFrom(Color.TransparentWhite, 0.4f, Halfling.Mathx.EaseOut);
        return box;
    }

    static Label Text(string xml, int size, bool center)
    {
        var l = new Label { Text = xml };
        l.TextRenderer.Font = WidgetRules.Instance.ShadowFont;
        l.TextRenderer.FontSize = size;
        l.TextRenderer.XmlFormatting = true;
        l.TextRenderer.HAlignment = center ? HAlignment.Center : HAlignment.Left;
        l.TextRenderer.VAlignment = VAlignment.Top;
        l.TextRenderer.OversizeMode = OversizeMode.Wrap;
        l.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        return l;
    }
}

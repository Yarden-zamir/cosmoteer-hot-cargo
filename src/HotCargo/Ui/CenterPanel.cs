using Cosmoteer.Game;
using Cosmoteer.Gui;
using Cosmoteer.Input;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Text;
using Halfling.Gui;

namespace HotCargo.Ui;

/// <summary>
/// A centered HUD panel with a title, a body and one button, in the game's blurred panel style.
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

    public static LayoutBox Show(GameRoot game, string title, Color titleColor, string bodyXml, string button)
    {
        var box = new LayoutBox
        {
            AnchorRect = AnchorPresets.Center,
            Width = 620f,
            InputBlockMode = InputBlockMode.Auto,
        };
        box.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
        box.AutoSize.Anchor = new Vector2(0.5f, 0.5f);
        box.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
        box.Children.BorderPadding = new Borders(30f, 24f, 30f, 24f);
        box.Children.WidgetPadding = new Vector2(16f);
        WidgetRules.AddBlur(box);

        var titleLabel = Text(title, 44, center: true);
        titleLabel.TextRenderer.Font = WidgetRules.Instance.ShadowGlowFont;
        titleLabel.TextRenderer.Color = titleColor;
        box.AddChild(titleLabel);
        box.AddChild(Text(bodyXml, 18, center: false));

        var row = new LayoutBox { Height = 50f };
        row.NineSlice.Flags = NineSliceFlags.None;
        row.Children.LayoutAlgorithm = LayoutAlgorithms.CenterTop;
        var ok = new Button(WidgetRules.Instance.OkayButton)
        {
            Size = new Vector2(220f, 46f),
            Text = button,
            Hotkey = Inputs.General.Confirm,
        };
        ok.Clicked += (_, _) => { game.Gui.RootWidget.Children.Remove(box); Open.Remove((game, box)); };
        row.AddChild(ok);
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

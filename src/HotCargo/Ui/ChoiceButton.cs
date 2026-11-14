using Cosmoteer.Gui;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Rects;

namespace HotCargo.Ui;

/// <summary>An option button of a choice page in the comms dialogue style: the dialogue icon on the left, left-aligned XML text.</summary>
static class ChoiceButton
{
    public static ImageButton New()
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
        b.ImageSprite.CopySpriteFrom(Cosmoteer.GameApp.Rules.CareerMode.Gui.Comms.DefaultDialogueLineIcon);
        b.ImageRectController.InnerRectMode = InnerRectMode.Left;
        b.ImageRectController.BorderPadding = new Borders(10f);
        b.ImageRectController.InnerRectSize = b.ImageSprite.DesiredSize;
        return b;
    }
}

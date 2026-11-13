using HotCargo.Dev;
using Xunit;

namespace HotCargo.Tests;

public class InvariantsModelTests
{
    [Fact]
    public void Shown_text_flags_markup_that_shows_and_placeholders_that_were_not_filled()
    {
        Assert.Equal("raw markup in a plain text", InvariantsModel.TextProblem("<color=#ff0>Rook's Debt</color>", xml: false));
        Assert.Null(InvariantsModel.TextProblem("<color=#ff0>Rook's Debt</color>", xml: true));
        Assert.Equal("escaped markup in an XML text", InvariantsModel.TextProblem("&lt;color=#ff0&gt;Rook", xml: true));
        Assert.Equal("unfilled placeholder", InvariantsModel.TextProblem("Escort {0} to the jump point", xml: true));
        Assert.Equal("unfilled placeholder", InvariantsModel.TextProblem("The freighter {escort_ship} waits", xml: false));
        Assert.Null(InvariantsModel.TextProblem("heat < 25 and > 10", xml: false));
        Assert.Null(InvariantsModel.TextProblem("Fish & chips &amp; {not a placeholder}", xml: true));
        Assert.Null(InvariantsModel.TextProblem("{}", xml: false));
    }
}

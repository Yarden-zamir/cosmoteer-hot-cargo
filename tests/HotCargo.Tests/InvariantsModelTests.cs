using HotCargo.Dev;
using HotCargo.Tech;
using Xunit;

namespace HotCargo.Tests;

public class InvariantsModelTests
{
    [Fact]
    public void A_small_hit_counts_a_step_only_when_it_passes_the_threshold()
    {
        // The playtest hits: 8 damage from 1,508 to 1,500 passes the half-health step of a 3,000 vault; 8 damage at 1,600 does not.
        Assert.Equal(1, InvariantsModel.StepsCrossed(3000, 1508, 1500, 1500f));
        Assert.Equal(0, InvariantsModel.StepsCrossed(3000, 1608, 1600, 1500f));
        Assert.Equal(0, InvariantsModel.StepsCrossed(3000, 1500, 1400, 1500f));
        Assert.Equal(2, InvariantsModel.StepsCrossed(12000, 12000, 8280, 1500f));
        Assert.Equal(0, InvariantsModel.StepsCrossed(3000, 2000, 2500, 1500f));
        Assert.Throws<ArgumentOutOfRangeException>(() => InvariantsModel.StepsCrossed(3000, 3000, 0, 0f));
    }

    [Fact]
    public void The_step_count_agrees_with_the_vault_damage_formula()
    {
        foreach (int max in new[] { 3000, 12000 })
            foreach (float share in new[] { 0.5f, 0.25f, 0.3f })
                for (int oldHealth = max; oldHealth > 0; oldHealth -= 97)
                    foreach (int damage in new[] { 1, 8, 120, 1499, 1500, 2600 })
                    {
                        int newHealth = Math.Max(1, oldHealth - damage);
                        Assert.Equal(TechModel.UnitsLostToDamage(max, oldHealth, newHealth, share * max),
                            InvariantsModel.StepsCrossed(max, oldHealth, newHealth, share * max));
                    }
    }

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

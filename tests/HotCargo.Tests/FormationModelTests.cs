using HotCargo.ShipAi;
using Xunit;

namespace HotCargo.Tests;

public class FormationModelTests
{
    [Fact]
    public void Slots_fill_a_v_left_then_right_one_rank_further_out_each_time()
    {
        Assert.Equal(new Slot(-FormationModel.ArmAngle, 1), FormationModel.SlotOf(0));
        Assert.Equal(new Slot(FormationModel.ArmAngle, 1), FormationModel.SlotOf(1));
        Assert.Equal(new Slot(-FormationModel.ArmAngle, 2), FormationModel.SlotOf(2));
        Assert.Equal(new Slot(FormationModel.ArmAngle, 3), FormationModel.SlotOf(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => FormationModel.SlotOf(-1));
    }

    [Theory]
    [InlineData(50f, 0f, 10f, true)]    // on the line, between the ship and the target
    [InlineData(50f, 9f, 10f, true)]    // the hull touches the line
    [InlineData(50f, 11f, 10f, false)]  // beside the line
    [InlineData(-20f, 0f, 10f, false)]  // behind the ship
    [InlineData(120f, 0f, 10f, false)]  // beyond the target
    public void An_ally_blocks_the_line_only_between_the_ship_and_its_target(float cx, float cy, float r, bool blocks) =>
        Assert.Equal(blocks, FormationModel.BlocksLine(0f, 0f, 100f, 0f, cx, cy, r));

    [Fact]
    public void Keep_spacing_moves_straight_away_from_the_ally_to_the_spacing_plus_the_margin()
    {
        var (x, y) = FormationModel.SpacingTarget(0f, 0f, -10f, 0f, edgeDistance: 30f, spacing: 150f, margin: 50f);
        Assert.Equal(170f, x, 3);   // 150 - 30 + 50 along +x, away from the ally on the left
        Assert.Equal(0f, y, 3);
        var (sx, sy) = FormationModel.SpacingTarget(5f, 5f, 5f, 5f, edgeDistance: 0f, spacing: 100f, margin: 50f);
        Assert.Equal(155f, sx, 3);  // two ships on one point: along +x
        Assert.Equal(5f, sy, 3);
    }

    [Fact]
    public void Formation_terms_and_actions_read_their_facts_and_kinds()
    {
        var f = new ShipFacts(1f, null, new HashSet<PartKind>(), float.PositiveInfinity, false, false, null, null, new HashSet<ShipClass>(), 0,
            false, false, NearestAlly: 120f, AllyInLineOfFire: true);
        Assert.True(ShipAiModel.Holds(new Term(Cond.AllyWithin, 150), f));
        Assert.False(ShipAiModel.Holds(new Term(Cond.AllyWithin, 100), f));
        Assert.True(ShipAiModel.Holds(new Term(Cond.AllyInLineOfFire, 0), f));
        Assert.Equal(ActKind.Move, ShipAiModel.KindOf(Act.HoldFormation));
        Assert.Equal(ActKind.Move, ShipAiModel.KindOf(Act.KeepSpacing));
        Assert.Equal(ActKind.Weapons, ShipAiModel.KindOf(Act.FireAtWill));
        Assert.Equal(ParamKind.Distance, ShipAiModel.ParamOf(Act.KeepSpacing));
    }
}

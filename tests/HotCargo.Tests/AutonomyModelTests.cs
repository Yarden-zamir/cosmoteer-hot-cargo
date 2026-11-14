using HotCargo.Autonomy;
using HotCargo.ShipAi;
using Xunit;

namespace HotCargo.Tests;

public class AutonomyModelTests
{
    static readonly HashSet<string> Known = ["iron", "copper", "gold", "steel", "diamond"];
    static bool IsKnown(string id) => Known.Contains(id);

    [Fact]
    public void Cargo_text_round_trips_and_default_settings_are_empty_text()
    {
        Assert.Equal("", AutonomyModel.Encode(CargoSettings.Default));
        Assert.Equal(CargoSettings.Default, AutonomyModel.Decode("", IsKnown));
        var byValue = new CargoSettings(50, [], ["iron"], [], ["steel", "gold"]);
        var byTypes = new CargoSettings(0, ["gold", "diamond"], [], ["iron", "copper"], []);
        foreach (var c in new[] { byValue, byTypes })
            Assert.Equal(c, AutonomyModel.Decode(AutonomyModel.Encode(c), IsKnown));
        Assert.Equal("HCC1:loot=v50|sell=iron|mine=|buy=steel,gold", AutonomyModel.Encode(byValue));
    }

    [Theory]
    [InlineData("HCC1:loot=v50|sell=uranium|mine=|buy=")]      // unknown resource
    [InlineData("HCC1:loot=v50|sell=iron,iron|mine=|buy=")]    // repeated
    [InlineData("HCC1:loot=|sell=|mine=|buy=")]               // loot needs a value or types
    [InlineData("HCC1:loot=vx|sell=|mine=|buy=")]             // bad value
    [InlineData("HCC1:loot=v-5|sell=|mine=|buy=")]            // negative value
    [InlineData("HCC1:loot=v0|sell=|mine=")]                  // missing part
    [InlineData("HCC1:loot=v0|sell=|mine=|buy=|buy=")]        // repeated part
    [InlineData("HCX:loot=v0|sell=|mine=|buy=")]              // wrong prefix
    public void Bad_cargo_text_is_refused(string text) => Assert.Throws<FormatException>(() => AutonomyModel.Decode(text, IsKnown));

    [Fact]
    public void Loot_filter_takes_a_type_list_or_a_minimum_unit_value_and_empty_lists_mean_all_or_any()
    {
        var value = new CargoSettings(100, [], [], [], []);
        Assert.True(value.LootAccepts("gold", 250));
        Assert.False(value.LootAccepts("iron", 10));
        var types = new CargoSettings(100, ["iron"], ["gold"], ["copper"], []);
        Assert.True(types.LootAccepts("iron", 10));
        Assert.False(types.LootAccepts("gold", 250));
        Assert.True(types.Sells("gold"));
        Assert.False(types.Sells("iron"));
        Assert.True(CargoSettings.Default.Sells("iron"));
        Assert.True(CargoSettings.Default.Mines("gold"));
        Assert.False(types.Mines("gold"));
    }

    [Theory]
    [InlineData(FunnelChoice.All, "iron", 1, true)]
    [InlineData(FunnelChoice.Off, "diamond", 9999, false)]
    [InlineData(FunnelChoice.Value50, "coil", 50, true)]
    [InlineData(FunnelChoice.Value50, "iron", 10, false)]
    [InlineData(FunnelChoice.Value200, "copper", 199, false)]
    [InlineData(FunnelChoice.Value1000, "diamond", 2000, true)]
    [InlineData(FunnelChoice.ShipFilter, "iron", 10, true)]
    [InlineData(FunnelChoice.ShipFilter, "gold", 250, false)]
    public void Funnel_filter_choices(FunnelChoice choice, string type, int value, bool takes)
    {
        var ship = new CargoSettings(0, ["iron"], [], [], []);
        Assert.Equal(takes, AutonomyModel.FunnelAccepts(choice, ship, type, value));
    }

    [Fact]
    public void Front_cells_follow_the_arrow_and_start_at_the_left_front_tile()
    {
        // A 2x1 Computer at (4, 6). Rotation 0: arrow up; rotation 1: right (rect 1x2); 2: down; 3: left.
        Assert.Equal([new Cell(4, 5), new Cell(5, 5)], AutonomyModel.FrontCells(new Box(4, 6, 2, 1), 0));
        Assert.Equal([new Cell(5, 6), new Cell(5, 7)], AutonomyModel.FrontCells(new Box(4, 6, 1, 2), 1));
        Assert.Equal([new Cell(5, 7), new Cell(4, 7)], AutonomyModel.FrontCells(new Box(4, 6, 2, 1), 2));
        Assert.Equal([new Cell(3, 7), new Cell(3, 6)], AutonomyModel.FrontCells(new Box(4, 6, 1, 2), 3));
    }

    [Fact]
    public void A_computer_mans_exactly_one_part_the_left_front_one_first()
    {
        var box = new Box(0, 1, 2, 1);
        // Two different parts in front: the one on the left front tile wins.
        Assert.Equal(7, AutonomyModel.MannedPart(box, 0, c => c.X == 0 ? 7 : 9));
        // The left front tile holds nothing that can be manned: the right one.
        Assert.Equal(9, AutonomyModel.MannedPart(box, 0, c => c.X == 0 ? null : 9));
        Assert.Null(AutonomyModel.MannedPart(box, 0, _ => null));
    }

    [Fact]
    public void Edge_cells_go_top_right_bottom_left_without_corners()
    {
        Assert.Equal(
            [new Cell(1, 0), new Cell(2, 0), new Cell(3, 1), new Cell(3, 2), new Cell(1, 3), new Cell(2, 3), new Cell(0, 1), new Cell(0, 2)],
            AutonomyModel.EdgeCells(new Box(1, 1, 2, 2)));
    }

    [Theory]
    [InlineData(2f, 2f, true)]     // on the Funnel
    [InlineData(0.1f, 1f, true)]   // within the reach of 1 tile
    [InlineData(-0.5f, 1f, false)] // outside the reach
    [InlineData(5.1f, 2f, false)]
    public void Funnel_area_is_its_tiles_grown_by_the_reach(float x, float y, bool inside) =>
        Assert.Equal(inside, AutonomyModel.InFunnelArea(new Box(1, 1, 2, 2), x, y, reach: 1f));

    [Theory]
    [InlineData(10000, 5000, 100, 999, 999, 50)]   // money above the reserve buys 50
    [InlineData(10000, 5000, 100, 20, 999, 20)]    // room limits
    [InlineData(10000, 5000, 100, 999, 7, 7)]      // stock limits
    [InlineData(4000, 5000, 100, 999, 999, 0)]     // below the reserve: nothing
    public void Buy_amount(long money, long reserve, int cost, int room, int stock, int expected) =>
        Assert.Equal(expected, AutonomyModel.BuyAmount(money, reserve, cost, room, stock));

    [Fact]
    public void Pick_prefers_the_score_then_the_nearest_then_the_lowest_id()
    {
        var items = new List<(float, int, double?)> { (500f, 3, 10), (100f, 2, 10), (100f, 1, 10), (50f, 4, null) };
        Assert.Equal(2, AutonomyModel.Pick(items, useScore: true));   // equal score and distance: lowest ID
        Assert.Equal(3, AutonomyModel.Pick(items, useScore: false));  // nearest
        Assert.Null(AutonomyModel.Pick(new List<(float, int, double?)> { (1f, 1, null) }, useScore: true));
        Assert.Null(AutonomyModel.Pick(new List<(float, int, double?)>(), useScore: false));
    }

    [Theory]
    [InlineData("cosmoteer.asteroid_iron_s", "iron")]
    [InlineData("cosmoteer.asteroid_hyperium_xxl", "hyperium")]
    [InlineData("cosmoteer.asteroid_plain_m", "plain")]
    [InlineData("cosmoteer.nebula_cloud_s", null)]
    [InlineData("cosmoteer.asteroid", null)]
    public void Ore_of_a_stasis_asteroid_comes_from_its_doodad_id(string id, string? ore) => Assert.Equal(ore, AutonomyModel.OreOfDoodad(id));

    [Fact]
    public void New_conditions_read_their_facts_and_cargo_above_is_a_percent_threshold()
    {
        var f = new ShipFacts(1f, null, new HashSet<PartKind>(), float.PositiveInfinity, false, false, null, null, new HashSet<ShipClass>(), 0, false, false,
            Cargo: 0.97f, CargoToSell: true, NearestLoot: 900f, StationInRange: true, NearestAsteroid: 15000f, NearestWreck: 2500f, NearestFleet: 4000f);
        Assert.True(ShipAiModel.Holds(new Term(Cond.CargoAbove, 95), f));
        Assert.False(ShipAiModel.Holds(new Term(Cond.CargoAbove, 98), f));
        Assert.True(ShipAiModel.Holds(new Term(Cond.CargoToSell, 0), f));
        Assert.True(ShipAiModel.Holds(new Term(Cond.LootWithin, 1000), f));
        Assert.True(ShipAiModel.Holds(new Term(Cond.StationInRange, 0), f));
        Assert.True(ShipAiModel.Holds(new Term(Cond.AsteroidWithin, 20000), f));
        Assert.False(ShipAiModel.Holds(new Term(Cond.WreckWithin, 2000), f));
        Assert.True(ShipAiModel.Holds(new Term(Cond.FleetBeyond, 3000), f));
        Assert.True(ShipAiModel.IsThreshold(ShipAiModel.ParamOf(Cond.CargoAbove)));
        // No storage bays: cargo above never holds.
        Assert.False(ShipAiModel.Holds(new Term(Cond.CargoAbove, 0), f with { Cargo = null }));
    }

    [Fact]
    public void Trade_rows_are_their_own_kind_and_the_first_matching_one_wins()
    {
        Assert.Equal(ActKind.Trade, ShipAiModel.KindOf(Act.Sell));
        Assert.Equal(ActKind.Trade, ShipAiModel.KindOf(Act.Unload));
        Assert.Equal(ActKind.Move, ShipAiModel.KindOf(Act.ReturnToFleet));
        Assert.Equal(ActKind.Facing, ShipAiModel.KindOf(Act.Face));
        TacticRow[] rows = [new(Cond.Always, 0, Act.Sell, 0), new(Cond.Always, 0, Act.Buy, 0), new(Cond.Always, 0, Act.CollectLoot, 0)];
        var d = ShipAiModel.Evaluate(rows, new ShipFacts(1f, null, new HashSet<PartKind>(), float.PositiveInfinity, false, false, null, null,
            new HashSet<ShipClass>(), 0, false, false), r => r.Act != Act.Sell);
        Assert.Equal(1, d.TradeRow);
        Assert.Equal(2, d.MoveRow);
    }

    [Fact]
    public void Every_preset_round_trips_through_the_text_form()
    {
        foreach (var p in Enum.GetValues<Preset>())
        {
            var rows = ShipAiModel.PresetRows(p);
            Assert.Equal(rows, ShipAiModel.Decode(ShipAiModel.Encode(rows)));
        }
    }

    [Theory]
    [InlineData(0, 1, true)]    // a 1-crew slot: the Computer alone fills it
    [InlineData(0, 2, false)]   // a 2-crew slot: one crew member short
    [InlineData(1, 2, true)]    // a 2-crew slot with 1 crew member there
    [InlineData(2, 4, false)]   // a 4-crew slot with 2 there: one short
    public void A_computer_is_one_crew_member_in_the_slot_it_serves(int crew, int needed, bool full)
    {
        Assert.Equal(full, AutonomyModel.CrewMet(crew, needed, AutonomyModel.ComputerCrew));
        Assert.Equal(Math.Max(0, needed - 1), AutonomyModel.CrewStillWanted(needed, AutonomyModel.ComputerCrew));
    }
}

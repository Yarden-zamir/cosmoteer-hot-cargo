using HotCargo.Chains;
using Xunit;

namespace HotCargo.Tests;

public class ChainModelTests
{
    static ResultRow Row(string on, string next = "", string[]? ifFlags = null, string[]? notFlags = null) => new(on, next, ifFlags ?? [], notFlags ?? []);

    static readonly HashSet<string> NoFlags = [];

    static StageNode Mission(string id, StageWhere where, params ResultRow[] rows) => new(id, StageKind.Mission, where, rows, []);

    static StageNode Page(string id, string[] results, params ResultRow[] rows) => new(id, StageKind.Page, StageWhere.Station, rows, results);

    /// <summary>A valid chain: a mission, then a choice page whose branches end, loop or lead to a later mission.</summary>
    static ChainNode Valid() => new("test",
    [
        Mission("job", StageWhere.Station, Row("success", "choice"), Row("fail")),
        Page("choice", ["stay", "leave", "fight"], Row("stay", "choice"), Row("leave"), Row("fight", "revenge")),
        Mission("revenge", StageWhere.Arrival, Row("success"), Row("any")),
    ]);

    [Fact]
    public void Failure_kinds_fall_back_to_fail_then_any()
    {
        Assert.Equal(["timeout", "fail", "any"], ChainModel.Keys("timeout"));
        Assert.Equal(["lost", "fail", "any"], ChainModel.Keys("lost"));
        Assert.Equal(["abandoned", "fail", "any"], ChainModel.Keys("abandoned"));
        Assert.Equal(["success", "any"], ChainModel.Keys("success"));
        Assert.Equal(["paid", "any"], ChainModel.Keys("paid"));
    }

    [Fact]
    public void Match_prefers_the_exact_result_over_the_fallback()
    {
        ResultRow[] rows = [Row("fail", "a"), Row("timeout", "b"), Row("any", "c")];
        Assert.Equal(1, ChainModel.Match(rows, "timeout", NoFlags));
        Assert.Equal(0, ChainModel.Match(rows, "lost", NoFlags));
        Assert.Equal(2, ChainModel.Match(rows, "success", NoFlags));
        Assert.Equal(-1, ChainModel.Match([Row("success")], "fail", NoFlags));
    }

    [Fact]
    public void Match_takes_the_first_row_whose_flags_hold()
    {
        ResultRow[] rows = [Row("success", "rich", ifFlags: ["account"]), Row("success", "poor", notFlags: ["broke"]), Row("success", "end")];
        Assert.Equal(0, ChainModel.Match(rows, "success", new HashSet<string> { "account" }));
        Assert.Equal(1, ChainModel.Match(rows, "success", NoFlags));
        Assert.Equal(2, ChainModel.Match(rows, "success", new HashSet<string> { "broke" }));
    }

    [Fact]
    public void Covered_needs_a_row_without_flag_conditions()
    {
        Assert.False(ChainModel.Covered([Row("success", ifFlags: ["x"])], "success"));
        Assert.True(ChainModel.Covered([Row("success", ifFlags: ["x"]), Row("any")], "success"));
        Assert.True(ChainModel.Covered([Row("fail")], "lost"));
    }

    [Theory]
    [InlineData(true, false, false, false, "success")]
    [InlineData(false, true, true, false, "timeout")]
    [InlineData(false, true, true, true, "timeout")]
    [InlineData(false, true, false, true, "lost")]
    [InlineData(false, true, false, false, "fail")]
    [InlineData(false, false, false, false, "abandoned")]
    public void Classify_maps_how_a_mission_ended(bool completed, bool failed, bool timedOut, bool lost, string expected)
    {
        Assert.Equal(expected, ChainModel.Classify(new MissionEnd(completed, failed, timedOut, lost)));
    }

    [Fact]
    public void Pick_never_repeats_a_chain_while_unused_ones_remain()
    {
        ChainChoice[] choices = [new("a", 10, 1), new("b", 1, 0), new("c", 5, 2)];
        foreach (float roll in new[] { 0f, 0.5f, 0.999f }) Assert.Equal(1, ChainModel.Pick(choices, roll));
    }

    [Fact]
    public void Pick_weighs_among_the_least_used_chains()
    {
        ChainChoice[] choices = [new("a", 3, 1), new("b", 1, 1), new("c", 0, 0)];
        // c has no weight, so it never takes part. a has 3 of 4 weight units.
        Assert.Equal(0, ChainModel.Pick(choices, 0.74f));
        Assert.Equal(1, ChainModel.Pick(choices, 0.76f));
        Assert.Equal(-1, ChainModel.Pick([new("x", 0, 0)], 0.5f));
        Assert.Equal(-1, ChainModel.Pick([], 0.5f));
    }

    [Fact]
    public void Story_so_far_keeps_the_last_lines()
    {
        Assert.Equal("b. c.", ChainModel.StorySoFar(["a.", "b.", "c."], 2));
        Assert.Equal("a.", ChainModel.StorySoFar(["a."], 2));
        Assert.Equal("", ChainModel.StorySoFar([], 2));
    }

    [Fact]
    public void A_valid_chain_has_no_errors()
    {
        Assert.Empty(ChainModel.Validate(Valid()));
    }

    [Fact]
    public void Validate_reports_graph_errors()
    {
        static void Has(ChainNode chain, string part) =>
            Assert.Contains(ChainModel.Validate(chain), e => e.Contains(part, StringComparison.Ordinal));

        var v = Valid();
        // The first stage must be a mission offer at the station.
        Has(v with { Stages = [v.Stages[1], v.Stages[0], v.Stages[2]] }, "first stage");
        // A mission stage must handle every mission result.
        Has(v with { Stages = [Mission("job", StageWhere.Station, Row("success", "choice")), v.Stages[1], v.Stages[2]] }, "'timeout'");
        // A page result without a row, and a row for a result that the page never gives.
        Has(v with { Stages = [v.Stages[0], Page("choice", ["stay", "leave", "fight", "run"], Row("stay", "choice"), Row("leave"), Row("fight", "revenge")), v.Stages[2]] }, "'run'");
        Has(v with { Stages = [v.Stages[0], Page("choice", ["stay"], Row("stay"), Row("leave")), v.Stages[2]] }, "never comes from the page");
        // A Next that is not a stage, a stage that nothing reaches, a duplicate ID.
        Has(v with { Stages = [Mission("job", StageWhere.Station, Row("success", "nowhere"), Row("fail")), v.Stages[1], v.Stages[2]] }, "'nowhere'");
        Has(v with { Stages = [Mission("job", StageWhere.Station, Row("success"), Row("fail")), v.Stages[1], v.Stages[2]] }, "cannot be reached");
        Has(v with { Stages = [v.Stages[0], v.Stages[1], v.Stages[2], v.Stages[2]] }, "used twice");
    }

    [Fact]
    public void Validate_needs_an_ending()
    {
        var loop = new ChainNode("loop", [Mission("job", StageWhere.Station, Row("any", "job"))]);
        Assert.Contains(ChainModel.Validate(loop), e => e.Contains("no ending", StringComparison.Ordinal));
    }
}

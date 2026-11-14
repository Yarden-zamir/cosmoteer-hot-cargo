using HotCargo.RunRecord;
using Xunit;

namespace HotCargo.Tests;

public class RunRecordModelTests
{
    static readonly DateTimeOffset Day = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    static RunEntry Entry(string id, int days = 0, bool won = true, long score = 1000) =>
        new(id, Day.AddDays(days), won, score, 3, 3600, 8, 8, 90f, 1, "contract", "The Contract", 1);

    [Fact]
    public void A_new_run_comes_first_and_the_oldest_drop()
    {
        var runs = RunRecordModel.Upsert([Entry("a"), Entry("b")], Entry("c", 1), max: 2)!;
        Assert.Equal(["c", "a"], runs.Select(r => r.Id));
    }

    [Fact]
    public void A_known_run_keeps_its_place_and_end_date_and_an_equal_entry_writes_nothing()
    {
        IReadOnlyList<RunEntry> runs = [Entry("a"), Entry("b")];
        Assert.Null(RunRecordModel.Upsert(runs, Entry("b", days: 5), 10));
        var updated = RunRecordModel.Upsert(runs, Entry("b", days: 5, score: 2000), 10)!;
        Assert.Equal(["a", "b"], updated.Select(r => r.Id));
        Assert.Equal(2000, updated[1].Score);
        Assert.Equal(Day, updated[1].Ended);
    }

    [Fact]
    public void Best_is_the_highest_won_score()
    {
        Assert.Equal("b", RunRecordModel.Best([Entry("a", score: 10), Entry("b", score: 30), Entry("c", won: false, score: 99)])!.Id);
        Assert.Null(RunRecordModel.Best([Entry("c", won: false)]));
    }

    [Fact]
    public void Rows_are_newest_first()
    {
        Assert.Equal(["b", "c", "a"], RunRecordModel.Rows([Entry("a", 0), Entry("b", 2), Entry("c", 1)]).Select(r => r.Id));
    }

    [Fact]
    public void The_file_round_trips_and_refuses_what_it_cannot_read()
    {
        List<RunEntry> runs = [Entry("a"), Entry("b", won: false, score: 0)];
        Assert.Equal(runs, RunRecordModel.Read(RunRecordModel.Write(runs)));
        Assert.Throws<InvalidDataException>(() => RunRecordModel.Read("""{"Version":2,"Runs":[]}"""));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => RunRecordModel.Read("not json"));
    }
}

public class RunSortTests
{
    static readonly DateTimeOffset Day = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    static RunEntry Entry(string id, long score, int days = 0, string job = "The Contract", bool won = true) =>
        new(id, Day.AddDays(days), won, score, 3, 3600, 8, 8, 90f, 1, "contract", job, 1);

    static readonly RunEntry[] Runs = [Entry("a", 100, 0, "Rush Order"), Entry("b", 300, 1, "Loud Exit"), Entry("c", 200, 2, "deep space")];

    [Fact]
    public void The_default_is_score_highest_first()
    {
        Assert.Equal(RunColumn.Score, RunSort.DefaultColumn);
        Assert.Equal(["b", "c", "a"], RunSort.Sort(Runs, RunSort.DefaultColumn, RunSort.DefaultDescending(RunSort.DefaultColumn)).Select(r => r.Id));
    }

    [Fact]
    public void A_click_on_the_sorted_column_turns_it_around_and_another_column_takes_its_default()
    {
        Assert.Equal((RunColumn.Score, false), RunSort.Click(RunColumn.Score, true, RunColumn.Score));
        Assert.Equal((RunColumn.Job, false), RunSort.Click(RunColumn.Score, true, RunColumn.Job));
        Assert.Equal((RunColumn.Date, true), RunSort.Click(RunColumn.Job, false, RunColumn.Date));
    }

    [Fact]
    public void Text_sorts_without_case_and_ties_go_to_the_newer_run()
    {
        Assert.Equal(["c", "b", "a"], RunSort.Sort(Runs, RunColumn.Job, false).Select(r => r.Id));
        Assert.Equal(["y", "x"], RunSort.Sort([Entry("x", 5, 0), Entry("y", 5, 1)], RunColumn.Score, true).Select(r => r.Id));
    }

    [Fact]
    public void Every_column_sorts()
    {
        foreach (var column in Enum.GetValues<RunColumn>()) Assert.Equal(3, RunSort.Sort(Runs, column, true).Count);
    }
}

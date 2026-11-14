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

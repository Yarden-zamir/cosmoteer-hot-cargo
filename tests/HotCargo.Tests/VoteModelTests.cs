using HotCargo.SharedChoice;
using Xunit;

namespace HotCargo.Tests;

public class VoteModelTests
{
    static readonly HashSet<string> Three = ["a", "b", "c"];

    static VoteDecision? Resolve(IReadOnlySet<string> electorate, bool timedOut = false, bool forced = false, params (string, int)[] votes) =>
        VoteModel.Resolve(votes, electorate, timedOut, forced);

    [Fact]
    public void Open_until_everyone_voted()
    {
        Assert.Null(Resolve(Three, votes: [("a", 0), ("b", 0)]));
        Assert.Null(Resolve(Three));
    }

    [Fact]
    public void Unanimous_and_majority_need_every_vote()
    {
        Assert.Equal(new VoteDecision(1, VoteReason.Unanimous), Resolve(Three, votes: [("a", 1), ("b", 1), ("c", 1)]));
        Assert.Equal(new VoteDecision(2, VoteReason.Majority), Resolve(Three, votes: [("a", 2), ("b", 0), ("c", 2)]));
    }

    [Fact]
    public void A_split_without_majority_waits_for_the_timeout()
    {
        HashSet<string> two = ["a", "b"];
        Assert.Null(Resolve(two, votes: [("a", 0), ("b", 1)]));
        Assert.Equal(new VoteDecision(0, VoteReason.Timeout), Resolve(two, timedOut: true, votes: [("a", 1), ("b", 0)]));
    }

    [Fact]
    public void Timeout_and_force_take_the_plurality_with_the_lowest_option_on_a_tie()
    {
        HashSet<string> four = ["a", "b", "c", "d"];
        Assert.Equal(new VoteDecision(3, VoteReason.Timeout), Resolve(four, timedOut: true, votes: [("a", 3), ("b", 3), ("c", 1)]));
        Assert.Equal(new VoteDecision(1, VoteReason.Forced), Resolve(four, forced: true, votes: [("a", 3), ("c", 1)]));
        Assert.Null(Resolve(four, timedOut: true, forced: true));
    }

    [Fact]
    public void Votes_of_players_who_left_do_not_count()
    {
        HashSet<string> two = ["a", "b"];
        Assert.Equal(new VoteDecision(0, VoteReason.Unanimous), Resolve(two, votes: [("a", 0), ("b", 0), ("gone", 1)]));
    }

    [Fact]
    public void Timeout_counts_from_the_first_vote()
    {
        Assert.False(VoteModel.TimedOut(100, -1, 30));
        Assert.False(VoteModel.TimedOut(129, 100, 30));
        Assert.True(VoteModel.TimedOut(130, 100, 30));
        Assert.False(VoteModel.TimedOut(1000, 100, 0));
    }
}

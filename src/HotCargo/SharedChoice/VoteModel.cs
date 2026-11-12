namespace HotCargo.SharedChoice;

/// <summary>Why a vote ended.</summary>
public enum VoteReason { Unanimous, Majority, Timeout, Forced }

public readonly record struct VoteDecision(int Option, VoteReason Reason);

/// <summary>Vote rules of shared choices (spec 604, 605). Pure, so the unit tests check them.</summary>
public static class VoteModel
{
    /// <summary>
    /// Decides a vote, or returns null while it stays open. Votes of players outside the electorate do not count.
    /// Unanimous: every player voted for the same option. Majority: every player voted and one option has more than half.
    /// Timeout and forced: the option with the most votes; a tie goes to the lowest option index. No votes: no decision.
    /// </summary>
    public static VoteDecision? Resolve<TVoter>(IEnumerable<(TVoter Voter, int Option)> votes, IReadOnlySet<TVoter> electorate, bool timedOut, bool forced)
        where TVoter : notnull
    {
        var counted = votes.Where(v => electorate.Contains(v.Voter)).ToList();
        if (counted.Count == 0) return null;
        if (counted.Select(v => v.Voter).Distinct().Count() != counted.Count) throw new ArgumentException("a voter has more than one vote");
        var (top, topCount) = counted.GroupBy(v => v.Option)
            .Select(g => (Option: g.Key, Count: g.Count()))
            .OrderByDescending(t => t.Count).ThenBy(t => t.Option)
            .First();
        bool allVoted = counted.Count == electorate.Count;
        if (allVoted && topCount == counted.Count) return new(top, VoteReason.Unanimous);
        if (allVoted && topCount * 2 > electorate.Count) return new(top, VoteReason.Majority);
        if (forced) return new(top, VoteReason.Forced);
        if (timedOut) return new(top, VoteReason.Timeout);
        return null;
    }

    /// <summary>True when the vote timeout ran out. firstVote is -1 while nobody voted. A timeout of 0 or less never runs out.</summary>
    public static bool TimedOut(double now, double firstVote, float timeoutSeconds) =>
        timeoutSeconds > 0 && firstVote >= 0 && now - firstVote >= timeoutSeconds;
}

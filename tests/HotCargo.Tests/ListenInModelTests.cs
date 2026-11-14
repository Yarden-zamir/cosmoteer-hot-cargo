using HotCargo.SharedChoice;
using Xunit;

namespace HotCargo.Tests;

public class ListenInModelTests
{
    static OpenComm<string, int> C(string caller, int callerId, int calleeId, int player, bool dialogue = true) =>
        new(caller, "ship" + calleeId, callerId, calleeId, player, dialogue);

    [Fact]
    public void Lists_other_players_channels_with_a_dialogue_only()
    {
        var hails = ListenInModel.Hails([C("mine", 1, 10, 0), C("theirs", 2, 10, 1), C("trade", 3, 11, 1, dialogue: false)], localPlayer: 0);
        Assert.Equal(["theirs"], hails.Select(h => h.Caller));
    }

    [Fact]
    public void One_entry_per_speaker_and_ship_in_a_stable_order()
    {
        var hails = ListenInModel.Hails([C("b2", 7, 20, 2), C("a", 5, 30, 1), C("b1", 6, 20, 2), C("c", 4, 20, 3)], localPlayer: 0);
        Assert.Equal(["c", "b1", "a"], hails.Select(h => h.Caller));
    }

    [Fact]
    public void A_window_closes_only_after_the_linger_time()
    {
        Assert.False(ListenInModel.CloseNow(null, 100, 4));
        Assert.False(ListenInModel.CloseNow(98, 100, 4));
        Assert.True(ListenInModel.CloseNow(96, 100, 4));
    }
}

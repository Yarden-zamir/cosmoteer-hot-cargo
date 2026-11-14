using HotCargo.SharedChoice;
using Xunit;

namespace HotCargo.Tests;

public class ListenInModelTests
{
    static OpenComm<string, int> C(string caller, int callerId, int calleeId, int player, bool dialogue = true, bool openHere = false) =>
        new(caller, "ship" + calleeId, callerId, calleeId, player, dialogue, openHere);

    static List<(string Caller, HailRole Role)> Hails(int local, bool shared, params OpenComm<string, int>[] comms) =>
        [.. ListenInModel.Hails(comms, local, shared).Select(h => (h.Comm.Caller, h.Role))];

    [Fact]
    public void Lists_other_players_channels_with_a_dialogue_only()
    {
        Assert.Equal([("theirs", HailRole.Listen)], Hails(0, false, C("mine", 1, 10, 0), C("theirs", 2, 10, 1), C("trade", 3, 11, 1, dialogue: false)));
    }

    [Fact]
    public void One_entry_per_speaker_and_ship_in_a_stable_order()
    {
        var hails = Hails(0, false, C("b2", 7, 20, 2), C("a", 5, 30, 1), C("b1", 6, 20, 2), C("c", 4, 20, 3));
        Assert.Equal(["c", "b1", "a"], hails.Select(h => h.Caller));
    }

    [Fact]
    public void A_shared_player_index_joins_a_channel_that_this_machine_did_not_open()
    {
        Assert.Equal([("mate", HailRole.Join)], Hails(0, true, C("mate", 1, 10, 0)));
        Assert.Empty(Hails(0, true, C("mine", 1, 10, 0, openHere: true)));
        // Without a second human on the index, the local player opened the channel (its window may be closing).
        Assert.Empty(Hails(0, false, C("mine", 1, 10, 0)));
    }

    [Fact]
    public void A_window_closes_only_after_the_linger_time()
    {
        Assert.False(ListenInModel.CloseNow(null, 100, 4));
        Assert.False(ListenInModel.CloseNow(98, 100, 4));
        Assert.True(ListenInModel.CloseNow(96, 100, 4));
    }
}

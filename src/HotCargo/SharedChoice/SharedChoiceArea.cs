using System.Globalization;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Events;

namespace HotCargo.SharedChoice;

/// <summary>
/// Entry point of the SharedChoice area: co-op hover markers and votes on shared choice pages (spec 600 and later).
/// Other areas register their pages as an ISharedChoiceDomain and use SharedChoicePanel in their dialog.
/// </summary>
public static class SharedChoiceArea
{
    public static void Register()
    {
        Run.Hooks.Tick += SharedChoices.OnTick;
        AddAutotest();
    }

    static Run RequireRun() => Autotest.RequireRun();

    /// <summary>The serial of the pending event page. The test commands act on it.</summary>
    static int EventSerial() => RequireRun().State.Events.Pending?.Serial ?? throw new InvalidOperationException("no pending event");

    static void Send(SharedChoiceAction action, int option) =>
        RequireRun().Game.NetManager.EnqueueInput(new SharedChoiceInput(EventsChoiceDomain.Name, EventSerial(), action, option));

    static void AddAutotest()
    {
        // votemode on|off: sends the mode toggle like the dialog button.
        Autotest.Commands["votemode"] = arg => RequireRun().Game.NetManager.EnqueueInput(SharedChoiceInput.SetVoteMode(arg == "on"));
        // vote <choice number> | unvote | forcevote: vote actions on the pending event page, like the dialog sends them.
        Autotest.Commands["vote"] = arg => Send(SharedChoiceAction.Vote, int.Parse(arg, CultureInfo.InvariantCulture) - 1);
        Autotest.Commands["unvote"] = _ => Send(SharedChoiceAction.Unvote, 0);
        Autotest.Commands["forcevote"] = _ => Send(SharedChoiceAction.Force, 0);
        // hover <choice number> | hover off: replaces the mouse hover of this machine (headless games have no mouse).
        Autotest.Commands["hover"] = arg => Presence.ForcedOption = arg == "off" ? null : int.Parse(arg, CultureInfo.InvariantCulture) - 1;
        Autotest.Commands["sc_reset"] = _ => SharedChoices.LastDecision = null;
        // mp sc_set <SharedChoiceTunables field> <value>: changes a live tunable on every machine at the same tick. Test only.
        Autotest.Commands["sc_set"] = arg => Autotest.SetField(RequireRun().T.SharedChoice, arg);

        Autotest.Fields["sc_votemode"] = () => RequireRun().State.SharedChoice.VoteMode ? 1 : 0;
        Autotest.Fields["sc_votes"] = () => RequireRun().State.SharedChoice.Box(EventsChoiceDomain.Name, EventSerial())?.Votes.Count ?? 0;
        // Other players that hover an option of the pending event page on this machine's UI, and the first one's choice number.
        Autotest.Fields["sc_hovers"] = () => Presence.Others(RequireRun(), EventsChoiceDomain.Name, EventSerial()).Count;
        Autotest.Fields["sc_hover_choice"] = () => Presence.Others(RequireRun(), EventsChoiceDomain.Name, EventSerial()).FirstOrDefault()?.Option + 1 ?? 0;
        // The last vote decision on this machine: choice number, and reason (1 unanimous, 2 majority, 3 timeout, 4 forced). 0 = none.
        Autotest.Fields["sc_decided_choice"] = () => SharedChoices.LastDecision?.Option + 1 ?? 0;
        Autotest.Fields["sc_decided_reason"] = () => SharedChoices.LastDecision is { } d ? (int)d.Reason + 1 : 0;
    }
}

using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Events;

namespace HotCargo.Friendly;

/// <summary>
/// Entry point of the Friendly area (spec 1360 and later): friendly encounters. Their pages are ship-carried events
/// (mod/events/events.rules); this area adds the C# outcome effects (EventRunner.Effects). Mod.Boot calls Register before the events load.
/// </summary>
public static class FriendlyArea
{
    public static void Register()
    {
        EventRunner.Effects.Add("join", Join.Apply);
        AddAutotest();
    }

    internal static string Text(string key, params object?[] args) => Strings.FormatText("HotCargoFriendly/" + key, args);

    /// <summary>UI only: every machine shows the same line in the center and in the chat.</summary>
    internal static void Say(GameRoot game, string xml)
    {
        Ux.UxKit.CenterText(game, xml);
        Ux.UxKit.ChatLine(game, xml);
    }

    static void AddAutotest()
    {
        // fr_player_ships: ships of the local player. fr_joined: 1 when the last joined ship is the local player's, commandable and without AI.
        Autotest.Fields["fr_player_ships"] = () => Sim().Ships.HumanPlayerShips.Count(s => s.Metadata.PlayerIndex == Autotest.RequireRun().Game.LocalPlayerIndex);
        Autotest.Fields["fr_joined"] = () => Join.Last is { } id && Sim().Ships[id] is { } s && s.Commands.IsCommandableByLocalPlayer && s.AI is null ? 1 : 0;
        // fr_joined_crew: crew of the last joined ship, when all of them have its allegiance; else -1.
        Autotest.Fields["fr_joined_crew"] = () => Join.Last is { } id && Sim().Ships[id] is { } s
            ? s.Crew.Assigned.All(c => c.PlayerIndex == s.Metadata.PlayerIndex) ? s.Crew.Assigned.Count : -1 : -1;
    }

    static SimRoot Sim() => Autotest.RequireRun().Game.Sim ?? throw new InvalidOperationException("no sim");
}

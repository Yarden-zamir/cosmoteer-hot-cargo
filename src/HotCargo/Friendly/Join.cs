using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using HotCargo.Core;
using HotCargo.Events;

namespace HotCargo.Friendly;

/// <summary>Spec 1361: effect "join". The page's carrier ship becomes a ship of the player who chose.</summary>
static class Join
{
    /// <summary>Test only: the last ship that joined.</summary>
    internal static ObjectID? Last;

    internal static void Apply(Run run, SimRoot sim, int player, PendingEvent p, List<string> lines)
    {
        var id = p.Carrier ?? throw new InvalidDataException($"event {p.EventId}: Effect join needs a Carrier");
        if (sim.Ships[id] is not { IsJunk: false } ship)
        {
            lines.Add(FriendlyArea.Text("JoinGone"));
            return;
        }
        MakePlayerShip(ship, player);
        Last = id;
        string line = FriendlyArea.Text("JoinedFmt", ship.Metadata.XmlShipName);
        lines.Add(line);
        FriendlyArea.Say(run.Game, line);
        Log.Info($"friendly: {ship.Metadata.PlainShipName} joins player {player}");
    }

    /// <summary>
    /// An AI ship becomes a player's ship, as the vanilla allegiance change does (debug allegiance menu). It loses its AI, its event
    /// carrier tag and its stasis handler: player ships never go into stasis. Its crew take the ship's allegiance
    /// (vanilla CrewSoul.PlayerIndex: the allegiance of the crew's home ship).
    /// </summary>
    internal static void MakePlayerShip(Ship ship, int player)
    {
        ship.AI = null;
        ship.Tags?.Remove(EventHail.CarrierTag);
        if (ship.SpecialStasisHandler is StasisShipSpawner.StasisHandler h) ship.RemoveSpecialComponent(h);
        ship.Metadata.PlayerIndex = player;
        foreach (var crew in ship.Crew.Assigned) crew.PlayerIndex = player;
    }
}

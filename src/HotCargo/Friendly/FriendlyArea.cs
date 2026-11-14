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
        EventRunner.Effects.Add("apprentice", Apprentice.Apply);
        EventRunner.Effects.Add("leave", Leave);
        EventRunner.Effects.Add("halve_heat", HalveHeat);
        EventRunner.Effects.Add("debtor", Debtor.Apply);
        Run.Hooks.Tick += Debtor.OnTick;
        Run.Hooks.SystemEntered += Debtor.OnSystemEntered;
        Run.Hooks.Tick += Apprentice.OnTick;
        Run.Hooks.SystemEntered += Apprentice.OnSystemEntered;
        AddAutotest();
    }

    /// <summary>
    /// Spec 1364: effect "leave". The page's carrier jumps out of the system after FriendlyTunables.LeaveDelaySeconds, with the
    /// Diplomacy leaving jump (vanilla FTL jump-out effect), and a chat line names it.
    /// </summary>
    static void Leave(Run run, SimRoot sim, int player, PendingEvent p, List<string> lines)
    {
        var id = p.Carrier ?? throw new InvalidDataException($"event {p.EventId}: Effect leave needs a Carrier");
        if (sim.Ships[id] is not { IsJunk: false } ship) return;
        ship.Tags?.Remove(EventHail.CarrierTag);
        run.State.Diplomacy.Leaving.Add(new Diplomacy.Leaving { Ship = id, Due = run.State.GameSeconds + run.T.Friendly.LeaveDelaySeconds });
        Ux.UxKit.ChatLine(run.Game, Text("LeavesFmt", ship.Metadata.XmlShipName));
        Log.Info($"friendly: {ship.Metadata.PlainShipName} leaves the system");
    }

    /// <summary>
    /// Spec 1366: effect "halve_heat". Heat × FriendlyTunables.RiddleHeatKeep (0.5). The heat bar shows the drop; a center message
    /// and a chat line name the old and new heat.
    /// </summary>
    static void HalveHeat(Run run, SimRoot sim, int player, PendingEvent p, List<string> lines)
    {
        float before = run.State.Heat;
        run.AddHeat(before * run.T.Friendly.RiddleHeatKeep - before, sim);
        string line = Text("HeatHalvedFmt", before.ToString("0"), run.State.Heat.ToString("0"));
        lines.Add(line);
        Say(run.Game, line);
        Log.Info($"friendly: riddle heat {before:0.0} -> {run.State.Heat:0.0}");
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
        // fr_set <field> <value>: sets a Friendly tunable.
        Autotest.Commands["fr_set"] = arg => Autotest.SetField(Autotest.RequireRun().T.Friendly, arg);
        AddApprenticeAutotest();
        // fr_player_ships: ships of the local player. fr_joined: 1 when the last joined ship is the local player's, commandable and without AI.
        Autotest.Fields["fr_player_ships"] = () => Sim().Ships.HumanPlayerShips.Count(s => s.Metadata.PlayerIndex == Autotest.RequireRun().Game.LocalPlayerIndex);
        Autotest.Fields["fr_joined"] = () => Join.Last is { } id && Sim().Ships[id] is { } s && s.Commands.IsCommandableByLocalPlayer && s.AI is null ? 1 : 0;
        // fr_joined_crew: crew of the last joined ship, when all of them have its allegiance; else -1.
        Autotest.Fields["fr_joined_crew"] = () => Join.Last is { } id && Sim().Ships[id] is { } s
            ? s.Crew.Assigned.All(c => c.PlayerIndex == s.Metadata.PlayerIndex) ? s.Crew.Assigned.Count : -1 : -1;
    }

    static void AddApprenticeAutotest()
    {
        // fr_apprentices: running apprentices. fr_apprentice_fights: fights the first one saw.
        // fr_leaving: ships that are about to jump out or are jumping (Diplomacy leaving list).
        Autotest.Fields["fr_leaving"] = () => Autotest.RequireRun().State.Diplomacy.Leaving.Count;
        // Debtor (spec 1367, 1368): running debtors, the first one's hire, and test commands to end it now or to kill it.
        Autotest.Fields["fr_debtors"] = () => Autotest.RequireRun().State.Friendly.Debtors.Count;
        Autotest.Fields["fr_debtor_hired"] = () => Autotest.RequireRun().State.Friendly.Debtors.FirstOrDefault() is { } d
            && Autotest.RequireRun().State.Diplomacy.Hires.Any(h => h.Ship == d.Ship) ? 1 : 0;
        Autotest.Commands["fr_debtor_end"] = _ =>
        {
            var d = Autotest.RequireRun().State.Friendly.Debtors.FirstOrDefault() ?? throw new InvalidOperationException("no debtor");
            d.Until = Autotest.RequireRun().State.GameSeconds;
            Autotest.Wait(1);
        };
        Autotest.Commands["fr_debtor_kill"] = _ =>
        {
            var d = Autotest.RequireRun().State.Friendly.Debtors.FirstOrDefault() ?? throw new InvalidOperationException("no debtor");
            var ship = Sim().Ships[d.Ship] ?? throw new InvalidOperationException("the debtor is not live");
            foreach (var part in ship.Parts.ToList()) part.Health = 0;
            Autotest.Wait(1);
        };
        Autotest.Fields["fr_apprentices"] = () => Autotest.RequireRun().State.Friendly.Apprentices.Count;
        Autotest.Fields["fr_apprentice_fights"] = () => Autotest.RequireRun().State.Friendly.Apprentices.FirstOrDefault()?.Fights ?? -1;
        // fr_apprentice_hired: 1 while the first apprentice has its follow-and-fight hire.
        Autotest.Fields["fr_apprentice_hired"] = () => Autotest.RequireRun().State.Friendly.Apprentices.FirstOrDefault() is { } a
            && Autotest.RequireRun().State.Diplomacy.Hires.Any(h => h.Ship == a.Ship) ? 1 : 0;
        // fr_apprentice_end: the first apprentice decides now (its time is up).
        Autotest.Commands["fr_apprentice_end"] = _ =>
        {
            var a = Autotest.RequireRun().State.Friendly.Apprentices.FirstOrDefault() ?? throw new InvalidOperationException("no apprentice");
            a.Until = Autotest.RequireRun().State.GameSeconds;
            Autotest.Wait(1);
        };
        // fr_kill_enemy: destroys the enemy ship of the players nearest to the first player ship (every part to 0 health).
        Autotest.Commands["fr_kill_enemy"] = _ =>
        {
            var sim = Sim();
            var me = Core.SimQueries.FirstHumanShip(sim) ?? throw new InvalidOperationException("no player ship");
            var enemy = sim.Ships.Where(s => !s.IsJunk && s.IsSignificant && s.IsEnemiesWith(me)).MinBy(s => s.DetWorldCenter.DistanceSquaredTo(me.DetWorldCenter))
                ?? throw new InvalidOperationException("no enemy ship");
            Log.Info($"AUTOTEST killing {enemy.Metadata.PlainShipName}");
            foreach (var part in enemy.Parts.ToList()) part.Health = 0;
            Autotest.Wait(1);
        };
    }

    static SimRoot Sim() => Autotest.RequireRun().Game.Sim ?? throw new InvalidOperationException("no sim");
}

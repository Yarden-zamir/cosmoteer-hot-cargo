using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HotCargo.Core;

namespace HotCargo.Pursuit;

/// <summary>Text of the Pursuit area, from mod/pursuit/strings.en.rules (section HotCargoPursuit), and its UI-only outputs.</summary>
static class PursuitText
{
    public static string Text(string key) => Strings.GetText("HotCargoPursuit/" + key);

    public static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoPursuit/" + key, args);

    /// <summary>The outage color (spec 923): the heat bar and the outage texts.</summary>
    public static readonly Halfling.Graphics.Color OutageColor = new(0.3f, 0.6f, 1f);

    public static string Blue(string xml) => OutageColor.FormatWithXml(xml);

    /// <summary>Local chat line. UI only, safe in the sim tick on every machine.</summary>
    public static void Chat(Run run, string xml) => Ux.UxKit.ChatLine(run.Game, xml);

    /// <summary>
    /// A big text in the middle of the screen. UI only.
    /// Limit: uses the vanilla area name banner, because UxKit.CenterText is not on main yet; switch to it when it lands.
    /// </summary>
    public static void Center(Run run, string xml) => run.Game.Gui.ShowAreaName(xml);
}

/// <summary>Ships that jump out: the FTL effect now, removal at its end. Saved in the run state, so a save in between keeps it.</summary>
static class Removals
{
    /// <summary>Starts the jump-out effect and queues the removal. Sim logic.</summary>
    public static void JumpOut(Run run, Ship ship)
    {
        _ = ship.Renderer.AnimateFtlJumpOut(ship.WorldFlightDirection);
        run.State.Pursuit.Removals.Add(new Removal { Ship = ship.UniqueID, At = run.State.GameSeconds + ship.Rules.FtlEffects.TotalDuration.Seconds });
    }

    public static void Tick(Run run, SimRoot sim)
    {
        var list = run.State.Pursuit.Removals;
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            if (run.State.GameSeconds < r.At) continue;
            list.RemoveAt(i--);
            if (sim.Ships[r.Ship] is not { } ship || !ship.ExistsInSim(sim)) continue;
            sim.EnqueueDeterministic(ship.UniqueID, () => sim.Ships.Remove(ship, dispose: true, handleLostShip: false));
        }
    }
}

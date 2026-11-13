using System.Globalization;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Simulation;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Missions;
using HotCargo.Tech;

namespace HotCargo.Stash;

/// <summary>
/// Entry point of the Stash area (spec 970 to 989): a rumoured stash offered like a shady mission. Content is rules (mod/stash);
/// the C# part is the objective type (StashObjective), the offer chance and the autotest commands.
/// </summary>
public static class StashArea
{
    public const string Metatype = "HotCargoStash";
    public const string GroupTag = "hc_group_stash";

    /// <summary>Tests: the outcome of the next stash that resolves, instead of the roll.</summary>
    internal static StashState? ForcedOutcome;

    static readonly StashTunables Defaults = new();

    public static StashTunables TunablesFor(GameRoot game) => Run.Current is { } run && run.Game == game ? run.T.Stash : Defaults;

    internal static string Text(string key) => Strings.GetText("HotCargoStash/" + key);

    internal static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoStash/" + key, args);

    public static void Register()
    {
        var id = new ID<MissionMetatype>(Metatype);
        Autotest.Fields["st_offers"] = () => MissionArea.Offers(Sim()).Count(m => m.MetatypeID.Equals(id));
        Autotest.Fields["st_state"] = () => (int)Objective().State;
        Autotest.Fields["st_done"] = () => Objective().Completion == CompletionState.Completed ? 1 : 0;
        Autotest.Fields["st_chance"] = () => StashModel.Percent(Objective().FoundFirstNow(Autotest.RequireRun().Game));
        // 1 when the objective line, formatted the way the vanilla mission log and HUD format it, shows the found-first line.
        Autotest.Fields["st_shown"] = () =>
        {
            var game = Autotest.RequireRun().Game;
            var lines = new List<Cosmoteer.Modes.Career.Missions.Objectives.Objective.DisplayLine>();
            Objective().GetDisplayLinesImpl(lines, game, CompletionState.InProgress);
            return lines.Count > 0 && Strings.KeyStringFmt("Objectives/InProgressFmt", lines[0].Text).Text.Contains(Objective().FindLine(game), StringComparison.Ordinal) ? 1 : 0;
        };
        Autotest.Fields["st_ambushers"] = () => Objective().Ambushers.Count(a => a.IsAlive);
        Autotest.Fields["st_chance_param"] = () => TunablesFor(Autotest.RequireRun().Game).OfferChance;
        Autotest.Fields["st_group_chance"] = () => MissionArea.FindGroup(Sim(), GroupTag).SpawnChance;
        // st_set <StashTunables field> <value>: changes a live tunable for this run.
        Autotest.Commands["st_set"] = arg => Autotest.SetField(Autotest.RequireRun().T.Stash, arg);
        // st_generate: runs the stash spawner group now (SpawnChance 1), next to the player ship.
        Autotest.Commands["st_generate"] = _ => MissionArea.GenerateGroups(Sim(), [GroupTag]);
        Autotest.Commands["st_accept"] = _ => MissionArea.AcceptOffer(Metatype);
        // st_age <minutes>: the offer becomes older by this many game minutes (the found-first chance rises).
        Autotest.Commands["st_age"] = arg => Objective().Age(double.Parse(arg, CultureInfo.InvariantCulture) * 60.0);
        // st_force looted|empty|ambush: the outcome of the next stash that resolves.
        Autotest.Commands["st_force"] = arg => ForcedOutcome = Enum.Parse<StashState>(arg, ignoreCase: true);
        // st_goto: single player. Moves the player ship to the stash site.
        Autotest.Commands["st_goto"] = _ => Autotest.Teleport(TechAutotest.PlayerShip(), Autotest.ClearSpotNear(Sim(), Objective().Site, 200f, TechAutotest.PlayerShip().WorldBoundingRadius));
        Autotest.Commands["st_kill_ambush"] = _ =>
        {
            foreach (var a in Objective().Ambushers)
                if (a.TargetShip is { } ship) Sim().Ships.Remove(ship, dispose: true, handleLostShip: false);
        };
        // st_tostation: single player. Moves the player ship next to the station that offers a stash.
        Autotest.Commands["st_tostation"] = _ =>
        {
            var station = Cosmoteer.Ships.Special.SpaceStation.GetStations(Sim(), onlyOperating: false)
                .Where(s => s.GetProvidedMissions()?.Any(m => m.MetatypeID.Equals(id)) == true).Select(s => s.PhysicalShip).FirstOrDefault(s => s is not null)
                ?? throw new InvalidOperationException("no station offers a stash");
            var ship = TechAutotest.PlayerShip();
            Autotest.Teleport(ship, station.WorldCenter + new Halfling.Geometry.Vector2(station.WorldBoundingRadius + ship.WorldBoundingRadius + 2f, 0f));
        };
        // st_select: selects the stash offer in the open comms Missions tab (run "comms open" and "commsmission" first).
        Autotest.Commands["st_select"] = _ =>
        {
            var item = Autotest.RequireRun().Game.Gui.FloatingWindows.Children.OfType<Cosmoteer.Modes.Career.Comms.CommWindow>()
                .SelectMany(w => w.GetChainedWidgets(recursive: true)).OfType<Cosmoteer.Modes.Career.Missions.MissionsPanel>()
                .SelectMany(p => p).FirstOrDefault(i => i.Mission.MetatypeID.Equals(id))
                ?? throw new InvalidOperationException("no stash offer in an open missions panel");
            item.IsSelected = true;
        };
        // st_line: logs the objective line of the offer (the first stash offer) and of the accepted stash, if any.
        Autotest.Commands["st_line"] = _ =>
        {
            var game = Autotest.RequireRun().Game;
            var offer = MissionArea.Offers(Sim()).FirstOrDefault(m => m.MetatypeID.Equals(id));
            var o = offer is null ? null : Find(offer);
            Log.Info($"AUTOTEST stash line: offer='{o?.FindLine(game)}' accepted='{MissionArea.FindObjective<StashObjective>()?.FindLine(game)}'");
        };
    }

    static SimRoot Sim() => Autotest.RequireRun().Game.Sim;

    static StashObjective Objective() => MissionArea.FindObjective<StashObjective>() ?? throw new InvalidOperationException("no accepted stash");

    static StashObjective? Find(Mission m)
    {
        var list = new List<StashObjective>();
        foreach (var o in m.Objectives) o.GetObjectives(list);
        return list.FirstOrDefault();
    }

    /// <summary>Spec 970: sets the spawn chance of the stash group from the tunables before a system generates.</summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.InitSimRoot))]
    static class ChancePatch
    {
        static void Prefix(CareerSimModeManager __instance) => Errors.Guard("stash spawn chance", () =>
        {
            if (!Activation.IsModEnabled()) return;
            var tag = new ID<Cosmoteer.Generators.Simulation.SimObjectSpawner>(GroupTag);
            var group = MissionArea.AllSpawners(__instance.SectorType.SystemGenerator.Spawners).OfType<Cosmoteer.Generators.Simulation.SimObjectSpawner>()
                .FirstOrDefault(g => g.Tags?.Contains(tag) == true)
                ?? throw new InvalidOperationException($"spawner group {GroupTag} is missing in sector {__instance.SectorType.ID}");
            group.SpawnChance = (__instance.Sim.Game is { } game ? TunablesFor(game) : Defaults).OfferChance;
        });
    }
}

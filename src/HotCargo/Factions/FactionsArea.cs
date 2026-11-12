using Cosmoteer;
using Cosmoteer.Factions;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Special;
using Halfling.Random;
using HotCargo.Core;
using HotCargo.Journey;
using HotCargo.Ux;

namespace HotCargo.Factions;

/// <summary>
/// Entry point of the Factions area (spec 640 to 662): the corporation's ally and rival, hot zones, shelter, fences,
/// relay and blind jumps, station prices by heat, refusals and galaxy map markers.
/// Every rule reads sim state only (heat, reputation, map node factions, the galaxy seed), so all co-op machines agree.
/// </summary>
public static class FactionsArea
{
    public static void Register()
    {
        Run.Hooks.TimeHeatFactor = run =>
            FactionsModel.TimeHeatFactor(ZoneOf(run, run.Game.CurrentMapNode), run.T.Factions.AllyHeatFactor, run.T.Factions.ShelterHeatFactor);
        Run.Hooks.NotorietyFactor = run => ZoneOf(run, run.Game.CurrentMapNode) == Zone.Ally ? run.T.Factions.AllyNotorietyFactor : 1f;
        Run.Hooks.JumpHeatKept = run => HeatKept(run, Jumps.PendingKind(run));
        Run.Hooks.Tick += Jumps.OnTick;
        Run.Hooks.SystemEntered += OnSystemEntered;
        FactionsAutotest.Register();
    }

    /// <summary>The faction with the ID, or null (also before the game rules load).</summary>
    public static FactionRules? FactionById(string id) => GameApp.Rules?.Factions.FirstOrDefault(f => f.ID.ToString() == id);

    /// <summary>Spec 640: the corporation's ally and rival. A tunable names a faction; else both come from the galaxy seed.</summary>
    public static (FactionRules Ally, FactionRules Rival) Roles(Run run)
    {
        var all = GameApp.Rules.Factions;
        var (ally, rival) = FactionsModel.PickRoles(run.Mode.Init.RandSeed, all.Length);
        var t = run.T.Factions;
        var pickedAlly = FactionById(t.AllyFaction) ?? all[ally];
        var pickedRival = FactionById(t.RivalFaction) ?? all[rival];
        // One tunable names the seed's pick for the other role: the other role takes the seed's first pick, so the roles differ.
        if (pickedAlly == pickedRival)
        {
            if (t.RivalFaction.Length > 0) pickedAlly = all[rival];
            else pickedRival = all[ally];
        }
        return (pickedAlly, pickedRival);
    }

    public static FactionRules? Controller(MapNode node) => node.ControllingFaction is { } id ? GameApp.Rules.GetFactionRules(id) : null;

    public static bool IsAlly(Run run, FactionRules? f) => f is not null && f == Roles(run).Ally;

    public static bool IsRival(Run run, FactionRules? f) => f is not null && f == Roles(run).Rival;

    /// <summary>Spec 643: a faction that is not the ally shelters the players from ShelterReputation reputation.</summary>
    public static bool Shelters(Run run, FactionRules? f) =>
        f is not null && !IsAlly(run, f) && run.Mode.GetFactionReputation(f.ID) >= run.T.Factions.ShelterReputation;

    public static Zone ZoneOf(Run run, MapNode node)
    {
        if (!run.PressureActive) return Zone.Neutral;
        var f = Controller(node);
        return IsAlly(run, f) ? Zone.Ally : Shelters(run, f) ? Zone.Shelter : Zone.Neutral;
    }

    /// <summary>Spec 650: the share of heat that a switch of this kind keeps. A view switch (no jump) keeps Heat.HeatCarryOverFraction.</summary>
    public static float HeatKept(Run run, JumpKind kind)
    {
        var t = run.T.Factions;
        return kind switch
        {
            JumpKind.RelayWatched => FactionsModel.HeatKept(t.RelayWatchedHeatCut),
            JumpKind.RelayClear => FactionsModel.HeatKept(t.RelayClearHeatCut),
            JumpKind.Blind => FactionsModel.HeatKept(t.BlindHeatCut),
            _ => run.T.Heat.HeatCarryOverFraction,
        };
    }

    /// <summary>Spec 650: the heat cut of a jump kind, for the UI. 0 for a view switch.</summary>
    public static float HeatCut(Run run, JumpKind kind) => kind == JumpKind.View ? 0f : 1f - HeatKept(run, kind);

    /// <summary>Spec 655: the price factor of station goods and services now. 1 while heat is off.</summary>
    public static float Markup(Run run)
    {
        var t = run.T.Factions;
        return run.PressureActive ? FactionsModel.MarkupFactor(run.State.Heat, t.MarkupStartHeat, t.MarkupMaxFactor, t.PriceHeatStep) : 1f;
    }

    /// <summary>Spec 656: the crew hire price factor now. 1 while heat is off.</summary>
    public static float CrewMarkup(Run run)
    {
        var t = run.T.Factions;
        return run.PressureActive ? FactionsModel.CrewFactor(run.State.Heat, t.CrewPriceAtMaxHeat, t.PriceHeatStep) : 1f;
    }

    /// <summary>
    /// Spec 657: the station refuses to talk and trade. Never the Buyer, a fence (rival station), a faction that shelters the players,
    /// or a station that owes the caller a mission reward. The roll is seeded by the station and the sector index, so it holds for the visit.
    /// </summary>
    public static bool Refuses(Run run, Ship? caller, Ship station)
    {
        var t = run.T.Factions;
        if (!run.PressureActive || run.State.Heat < t.RefuseHeat) return false;
        if (station.IsHumanPlayerShip || Buyer.IsBuyer(station) || SpaceStation.GetStation(station, onlyOperating: false) is null) return false;
        var faction = FactionRules.GetShipFaction(station);
        if (faction is null || IsRival(run, faction) || Shelters(run, faction)) return false;
        if (caller is not null && OwesReward(station, caller)) return false;
        float roll = new FastRand(0x4843_5245_4655_5345UL ^ (uint)station.UniqueID.GetHashCode(), (ulong)run.State.SectorIndex).Single();
        return FactionsModel.Refuses(run.State.Heat, t.RefuseHeat, t.RefuseChance, roll);
    }

    /// <summary>GetPendingMissionRewards throws when no module provides missions, so it stays behind WillProvideMissions.</summary>
    static bool OwesReward(Ship station, Ship caller) =>
        station.AI is { } ai && ai.WillProvideMissions(caller) && ai.GetPendingMissionRewards(caller).Count > 0;

    internal static string Text(string key) => Strings.GetText("HotCargoFactions/" + key);

    internal static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoFactions/" + key, args);

    internal static string Name(FactionRules f) => f.TextIconXml + f.NameKey.Text;

    /// <summary>Spec 645, 650: on arrival, the blind switch turns off after a blind jump (sim), and a chat line names the zone (GUI).</summary>
    static void OnSystemEntered(Run run, bool firstVisit)
    {
        // Spec 650, 651: the blind switch holds for one jump, so a player never pays blind fuel by mistake on the next one.
        if (Jumps.PendingKind(run) == JumpKind.Blind)
        {
            run.State.Factions.BlindJump = false;
            Jumps.DamageOnArrival(run);
        }
        if (!run.PressureActive) return;
        var f = Controller(run.Game.CurrentMapNode);
        if (f is null) return;
        var t = run.T.Factions;
        string? line = ZoneOf(run, run.Game.CurrentMapNode) switch
        {
            Zone.Ally => Format("ArrivalAllyFmt", Name(f), t.AllyHeatFactor, t.AllyMoneyFactor),
            Zone.Shelter => Format("ArrivalShelterFmt", Name(f), t.ShelterHeatFactor),
            _ when IsRival(run, f) => Format("ArrivalRivalFmt", Name(f), t.RivalFencePremium),
            _ => null,
        };
        if (line is null) return;
        Run.OnNextFrame(() => UxKit.ChatLine(run.Game, line));
        Log.Info($"factions arrival line: {line}");
    }
}

/// <summary>Spec 658: a chat line each time heat rises into the markup or the refusal band, and a toast for the refusal band. UI only.</summary>
[HarmonyLib.HarmonyPatch(typeof(Cosmoteer.Modes.Career.CareerGameModeManager), nameof(Cosmoteer.Modes.Career.CareerGameModeManager.Update))]
static class HeatBandAlerts
{
    static (Run Run, int Band)? _last;

    static void Postfix(Cosmoteer.Modes.Career.CareerGameModeManager __instance)
    {
        if (Run.Current is not { } run || run.Mode != __instance) return;
        var t = run.T.Factions;
        int band = !run.PressureActive ? 0 : run.State.Heat >= t.RefuseHeat ? 2 : FactionsArea.Markup(run) > 1f ? 1 : 0;
        int last = _last is { } l && l.Run == run ? l.Band : 0;
        _last = (run, band);
        if (band <= last) return;
        UxKit.Guard("heat band alert", () =>
        {
            string line = band == 2 ? FactionsArea.Text("RefuseBand") : FactionsArea.Text("MarkupBand");
            // The markup band starts with the Hunt stage (both at 75 by default), whose alert takes the toast. So it gets a chat line only.
            if (band == 2) run.Game.Gui.ShowAreaName(line);
            UxKit.ChatLine(run.Game, line);
        });
    }
}

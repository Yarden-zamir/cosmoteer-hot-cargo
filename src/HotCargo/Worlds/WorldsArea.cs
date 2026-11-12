using Cosmoteer.GalaxyMap;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Map;
using Halfling;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Ux;

namespace HotCargo.Worlds;

/// <summary>
/// Entry point of the Worlds area (WORLDS.md, spec 220 to 229): system archetypes. The archetypes are rules (mod/worlds);
/// the C# part is the current, the heat zone depth, the archetype objectives, the hazard lines and the autotest commands.
/// </summary>
public static class WorldsArea
{
    public static void Register()
    {
        Run.Hooks.SystemEntered += OnSystemEntered;
        WorldsAutotest.Register();
    }

    /// <summary>Spec 225: the hazard line of a sector type, or null when the strings have none. Key: HotCargoWorlds/Hazard_&lt;sector type ID&gt;.</summary>
    internal static string? Hazard(SectorTypeRules sector) =>
        Strings.TryGetText($"HotCargoWorlds/Hazard_{sector.ID}", out string? text) ? text : null;

    internal static SectorTypeRules SectorOf(CareerGameModeManager mode, MapNode node) => mode.GetMapNodeData(node).SectorType;

    /// <summary>Spec 225: on the first arrival, a toast and a chat line name the system type and its hazard. GUI only.</summary>
    static void OnSystemEntered(Run run, bool firstVisit)
    {
        if (!firstVisit || !run.T.Worlds.ArrivalLine) return;
        var sector = SectorOf(run.Mode, run.Game.CurrentMapNode);
        if (Hazard(sector) is not { } hazard) return;
        ShowArrivalAsync(run, Strings.FormatText("HotCargoWorlds/ArrivalCenterFmt", sector.NameKey.Text, hazard),
            Strings.FormatText("HotCargoWorlds/ArrivalFmt", sector.NameKey.Text, hazard));
    }

    static async void ShowArrivalAsync(Run run, string center, string line)
    {
        await App.Clock.UntilElapsed(run.T.Worlds.ArrivalLineDelaySeconds);
        if (Run.Current != run) return;
        UxKit.Guard("arrival line", () =>
        {
            UxKit.CenterText(run.Game, center);
            UxKit.ChatLine(run.Game, line);
        });
        Log.Info($"arrival line: {line}");
    }

    /// <summary>Spec 225: the galaxy map tooltip of a system shows its hazard under the vanilla type line.</summary>
    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.GetMapNodeToolTip))]
    static class HazardTooltip
    {
        static void Postfix(CareerGameModeManager __instance, MapNode node, ref string? __result)
        {
            if (__result is null || Run.Current?.Mode != __instance) return;
            string? result = __result;
            UxKit.Guard("hazard tooltip", () =>
            {
                if (Hazard(SectorOf(__instance, node)) is { } hazard)
                    result += "\n" + Strings.FormatText("HotCargoWorlds/HazardTipFmt", hazard);
            });
            __result = result;
        }
    }

    /// <summary>App.Director does not exist yet when Register runs, so the frame hook starts at the first title screen.</summary>
    [HarmonyPatch(typeof(Cosmoteer.Gui.TitleScreen), MethodType.Constructor)]
    static class FrameHook
    {
        static bool _started;

        static void Postfix()
        {
            if (_started) return;
            _started = true;
            App.Director.FrameEnded += (_, _) =>
            {
                if (Run.Current is not { } run) return;
                UxKit.Guard("current arrows", () => CurrentArrows.Update(run));
                UxKit.Guard("heat zone line", () => HeatZone.OnFrame(run));
            };
        }
    }
}

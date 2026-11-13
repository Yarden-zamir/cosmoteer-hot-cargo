using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Ux;

/// <summary>
/// A mission title is XML: a chain stage title carries a color tag (Chains.ChainStageReward.Tag).
/// Put <see cref="Xml"/> into XML text as is, never escaped. Use <see cref="Plain"/> where nothing renders XML: logs, telemetry, plain labels.
/// </summary>
public static class MissionTitle
{
    public static string Xml(Mission m) => m.Name.Text;

    public static string Plain(Mission m) => Plain(m.Name.Text);

    /// <summary>XML text without tags and with entities decoded.</summary>
    public static string Plain(string xml) => System.Net.WebUtility.HtmlDecode(Events.EventView.Plain(xml));

    /// <summary>The vanilla offer details title (MissionsPanel.MissionInfoPanel._name) has no XML formatting, unlike the mission list items.</summary>
    [HarmonyPatch(typeof(MissionsPanel.MissionInfoPanel), MethodType.Constructor, [typeof(Mission)])]
    static class InfoPanelTitlePatch
    {
        static void Postfix(MissionsPanel.MissionInfoPanel __instance) =>
            UxKit.Guard("mission title xml", () => __instance._name.TextRenderer.XmlFormatting = true);
    }

    /// <summary>
    /// The vanilla new, completed and failed mission banners print m.Name.Text.ToUpper() without XML formatting.
    /// The banner gets the plain title in capitals instead.
    /// </summary>
    [HarmonyPatch]
    static class BannerTitlePatch
    {
        static readonly MethodInfo ToUpper = AccessTools.Method(typeof(string), nameof(string.ToUpper), []);

        static IEnumerable<MethodBase> TargetMethods() =>
            new[] { "DoNewMissionAnimation", "DoMissionCompletedAnimation", "DoMissionFailedAnimation" }
                .Select(name => AccessTools.AsyncMoveNext(AccessTools.Method(typeof(CareerGameModeManager), name)));

        static string BannerText(string title) => Plain(title).ToUpper();

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var banner = AccessTools.Method(typeof(BannerTitlePatch), nameof(BannerText));
            int replaced = 0;
            var result = instructions.Select(ci =>
            {
                if (!ci.Calls(ToUpper)) return ci;
                replaced++;
                return new CodeInstruction(OpCodes.Call, banner).MoveLabelsFrom(ci);
            }).ToList();
            // Each banner method upper-cases only the mission title (0.30.5a).
            if (replaced != 1)
                Errors.Report($"mission banner title {original.DeclaringType?.Name}", $"expected 1 ToUpper call, found {replaced}; the banner title may show raw tags");
            return result;
        }
    }
}

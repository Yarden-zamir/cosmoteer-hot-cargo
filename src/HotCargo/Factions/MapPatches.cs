using System.Text;
using Cosmoteer;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Modes.Career;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using HarmonyLib;
using HotCargo.Chains;
using HotCargo.Core;
using HotCargo.Ux;

namespace HotCargo.Factions;

/// <summary>Spec 645, 652, 660, 661: faction zones, jump heat and markers on the galaxy map. UI only: it reads sim state and changes nothing.</summary>
static class MapPatches
{
    static Run? For(CareerGameModeManager mode) => Run.Current is { PressureActive: true } run && run.Mode == mode ? run : null;

    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.GetMapNodeToolTip))]
    static class Tooltip
    {
        [HarmonyPriority(Priority.Low)]
        static void Postfix(CareerGameModeManager __instance, MapNode node, ref string? __result)
        {
            if (__result is null || For(__instance) is not { } run) return;
            string? result = __result;
            UxKit.Guard("faction tooltip", () => result += Lines(run, node));
            __result = result;
        }
    }

    /// <summary>The tooltip lines of a node, each with a leading line break. Tests log them.</summary>
    internal static string Lines(Run run, MapNode node)
    {
        var t = run.T.Factions;
        var sb = new StringBuilder();
        var f = FactionsArea.Controller(node);
        if (f is not null)
        {
            string name = FactionsArea.Name(f);
            if (FactionsArea.IsAlly(run, f))
                sb.Append('\n').Append(FactionsArea.Format("TipAllyFmt", name, t.AllyHeatFactor, t.AllyMoneyFactor, t.AllyNotorietyFactor));
            else if (FactionsArea.Shelters(run, f))
                sb.Append('\n').Append(FactionsArea.Format("TipShelterFmt", name, t.ShelterHeatFactor));
            else
                sb.Append('\n').Append(FactionsArea.Format("TipNoShelterFmt", name, t.ShelterReputation, run.Mode.GetFactionReputation(f.ID)));
            if (FactionsArea.IsRival(run, f))
                sb.Append('\n').Append(FactionsArea.Format("TipRivalFmt", t.RivalFencePremium));
        }
        if (node != run.Game.CurrentMapNode && run.Game.Map.Nodes.CanJumpTo(node))
        {
            float stored = run.State.NodeHeat.TryGetValue(node.UniqueID, out float h) ? h : 0f;
            float Arrive(JumpKind k) => HeatModel.OnEnterSystem(run.State.Heat, stored, FactionsArea.HeatKept(run, k));
            sb.Append('\n').Append(FactionsArea.Format("TipJumpFmt", Arrive(JumpKind.RelayClear), Arrive(JumpKind.RelayWatched), Arrive(JumpKind.Blind), t.BlindFuelFactor));
            if (Jumps.HuntersNearRelay(run, run.Game.Sim, node) > 0) sb.Append('\n').Append(FactionsArea.Text("TipRelayWatched"));
        }
        if (t.MapMarkers)
            foreach (var (n, label) in Markers.Targets(run))
                if (n == node) sb.Append('\n').Append(FactionsArea.Format("TipTargetFmt", label));
        return sb.ToString();
    }

    /// <summary>Spec 645: a short tag under the node name for a hot zone and for fence space.</summary>
    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.GetMapNodeText))]
    static class NodeText
    {
        static void Postfix(CareerGameModeManager __instance, MapNode node, StringBuilder sb)
        {
            if (For(__instance) is not { } run) return;
            // Spec 872: this runs for each map node in every frame while the map shows, so it has no closure.
            try
            {
                var f = FactionsArea.Controller(node);
                if (FactionsArea.IsAlly(run, f)) sb.Append("\n<s16><bad>").Append(FactionsArea.Text("TagAlly")).Append("</bad></s16>");
                else if (FactionsArea.IsRival(run, f)) sb.Append("\n<s16><good>").Append(FactionsArea.Text("TagRival")).Append("</good></s16>");
            }
            catch (Exception ex) { Errors.Report("ux faction map tag", ex.Message, ex); }
        }
    }

    /// <summary>Spec 660: the vanilla custom marker icon (custom_marker.png) on each node that holds a target, to the upper right of the node.</summary>
    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.DrawMapNodeExtras))]
    static class MarkerIcon
    {
        /// <summary>World size and offset of the icon on the map, the size of the vanilla mission icon (missions_gui.rules).</summary>
        static readonly Vector2 Size = new(32f / 64f, 32f / 64f);
        static readonly Vector2 Offset = new(40f / 64f, -40f / 64f);

        static void Postfix(CareerGameModeManager __instance, MapNode node, float scale, Direction rot)
        {
            if (For(__instance) is not { } run || !run.T.Factions.MapMarkers) return;
            UxKit.Guard("map markers", () =>
            {
                if (!Markers.Targets(run).Any(m => m.Node == node)) return;
                var sprite = GameApp.Rules.CareerMode.CustomMarkerIcon;
                var rect = Rect.CenteredOn(node.Location + Offset.RotateAroundOrigin(rot) * scale, Size * scale);
                App.Graphics.SetToMaterial(sprite);
                App.Graphics.Draw(new Quad(rect, rot, sprite));
            });
        }
    }
}

/// <summary>
/// Spec 660: the targets that the galaxy map marks. Contracts (accepted missions with terms) and Hot Cargo missions (events, chains) in the log,
/// at the node of their nearest objective, and chains that wait at their station's system (a station offer or a return stage).
/// </summary>
static class Markers
{
    static (Run Run, double Time, List<(MapNode Node, string Label)> List)? _cache;

    /// <summary>Cached for half a second of real time. UI only.</summary>
    internal static List<(MapNode Node, string Label)> Targets(Run run)
    {
        double now = App.Clock.Time.Seconds;
        if (_cache is { } c && c.Run == run && now - c.Time < 0.5) return c.List;
        var list = Compute(run);
        _cache = (run, now, list);
        return list;
    }

    internal static List<(MapNode Node, string Label)> Compute(Run run)
    {
        var list = new List<(MapNode, string)>();
        foreach (var m in run.Mode.Missions)
        {
            bool contract = run.State.Contracts.Terms.ContainsKey(m.MissionID);
            // Every Hot Cargo mission type (events, chains, the Buyer, the mod missions) has a metatype ID that starts with "HotCargo".
            bool hotCargo = m.MetatypeID.ToString().StartsWith("HotCargo", StringComparison.Ordinal);
            if ((contract || hotCargo) && m.GetClosestObjectiveMapNode(out _) is { } node) list.Add((node, m.Name.Text));
        }
        foreach (var chain in run.State.Chains.Instances)
        {
            if (chain.Ended || chain.MissionId is not null && run.Mode.Missions.Any(m => m.MissionID.Equals(chain.MissionId.Value))) continue;
            if (ChainLibrary.LoadError is not null || ChainLibrary.Get(chain.ChainId) is not { } rules) continue;
            if (rules.Stage(chain.Stage).Where == StageWhere.Arrival) continue;
            if (run.Game.Map.Nodes[chain.NodeId] is { } node) list.Add((node, rules.Title));
        }
        return list;
    }
}

using System.Text;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Pooling;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Ui;

namespace HotCargo.Ux;

/// <summary>UI-only patches on career screens (UX.md V3 to V6, H4). None of them writes simulation state.</summary>
static class CareerInfoPatches
{
    /// <summary>
    /// V3. The mission HUD shows the distance to a mission in another system as Dist2 / 1000, so 7.5 L.Y. reads "0.0 L.Y.".
    /// The mission log uses the raw value. This shows the raw value and the number of jumps.
    /// </summary>
    [HarmonyPatch(typeof(MissionsHud.MissionWidget), nameof(MissionsHud.MissionWidget.Distance), MethodType.Setter)]
    static class MissionHudDistance
    {
        static (MapNode From, MapNode To, int Jumps)? _cache;

        static void Postfix(MissionsHud.MissionWidget __instance, (float Dist1, float Dist2) value)
        {
            if (!float.IsInfinity(value.Dist1) || float.IsInfinity(value.Dist2) || !UxKit.On(s => s.MissionDistance)) return;
            UxKit.Guard("mission distance", () =>
            {
                var game = __instance._game;
                var node = __instance._mission.GetClosestObjectiveMapNode(out _);
                string ly = Strings.FormatText("MissionLog/DistanceLYFmt", value.Dist2);
                int jumps = node is null ? -1 : Jumps(game, node);
                __instance._distance.Text = "  (" + (jumps > 0 ? UxKit.Format("DistanceJumpsFmt", ly, jumps) : ly) + ")";
            });
        }

        static int Jumps(GameRoot game, MapNode to)
        {
            var from = game.CurrentMapNode;
            if (_cache is { } c && c.From == from && c.To == to) return c.Jumps;
            using var path = TempList<MapNode>.Alloc();
            float dist = game.Map.Nodes.FindPathTo(to, path, requireJumpable: true);
            int jumps = float.IsInfinity(dist) ? -1 : path.Count - 1;
            _cache = (from, to, jumps);
            return jumps;
        }
    }

    /// <summary>V4 and H4. More lines in the galaxy map node tooltip.</summary>
    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.GetMapNodeToolTip))]
    static class MapNodeTooltip
    {
        static void Postfix(CareerGameModeManager __instance, MapNode node, ref string? __result)
        {
            if (__result is null || !UxKit.On(s => s.MapNodeDetails)) return;
            string? result = __result;
            UxKit.Guard("map tooltip", () => result = result + Extra(__instance, node));
            __result = result;
        }

        static string Extra(CareerGameModeManager mode, MapNode node)
        {
            var game = mode.Game;
            var sb = new StringBuilder();
            bool current = node == game.CurrentMapNode;
            bool visited = current || game.SimArchive.ContainsSim(node);
            if (!current)
            {
                using var path = TempList<MapNode>.Alloc();
                float dist = game.Map.Nodes.FindPathTo(node, path, requireJumpable: true);
                sb.Append('\n').Append(float.IsInfinity(dist)
                    ? $"<gray>{UxKit.Text("MapNoRoute")}</gray>"
                    : UxKit.Format("MapJumpsFmt", path.Count - 1, Strings.FormatText("MissionLog/DistanceLYFmt", dist)));
            }
            if (!visited) sb.Append('\n').Append($"<gray>{UxKit.Text("MapUnvisited")}</gray>");
            int missions = mode.Missions.Count(m => m.IsMapNodeAssociated(node));
            if (missions > 0) sb.Append('\n').Append(UxKit.Format("MapMissionsFmt", missions));

            if (Run.Current is { } run && run.Mode == mode && run.State.Result == RunResult.Running && UxKit.On(s => s.BuyerMarker))
            {
                var s = run.State;
                if (!current && run.State.NodeHeat.TryGetValue(node.UniqueID, out float heat) && heat > 0)
                    sb.Append('\n').Append(UxKit.Format("MapHeatFmt", heat));
                if (!visited)
                {
                    // A first visit to any new system raises the sector index by one (Run.AfterSystemSwitch).
                    int sector = s.SectorIndex + 1;
                    int last = run.T.Journey.RunSectorCount;
                    sb.Append('\n').Append(sector == last
                        ? $"<good>{UxKit.Text("MapBuyerHere")}</good>"
                        : UxKit.Format("MapSectorFmt", sector, last));
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>V5. The fame tooltip says only "Fame". This shows the title, the next title and the crew limit.</summary>
    [HarmonyPatch(typeof(PlayerFameLabel), MethodType.Constructor, typeof(CareerGameModeManager))]
    static class FameTooltip
    {
        static void Postfix(PlayerFameLabel __instance, CareerGameModeManager gameMode) => UxKit.Guard("fame tooltip", () =>
        {
            var vanilla = __instance.ToolTips.TextProvider;
            __instance.ToolTips.TextProvider = new LiveText(() => UxKit.On(s => s.FameTooltip) ? Text(gameMode) : vanilla?.Text ?? "");
        });

        static string Text(CareerGameModeManager mode)
        {
            int fame = mode.PlayerFame;
            var titles = mode.Rules.FameTitles;
            var current = mode.GetCurrentFameTitle();
            var next = titles.FirstOrDefault(t => t.Fame > fame);
            int crew = mode.GetTotalPlayerCrew(mode.Game.LocalPlayerIndex);
            var sb = new StringBuilder(UxKit.Format("FameTitleFmt", fame.ToString("N0"), current.NameKey.Text));
            if (next is not null) sb.Append('\n').Append(UxKit.Format("FameNextFmt", next.NameKey.Text, (next.Fame - fame).ToString("N0")));
            sb.Append('\n').Append(UxKit.Format("FameCrewFmt", crew, mode.GetCurrentMaxCrew()));
            return sb.ToString();
        }
    }

    /// <summary>
    /// V6 and H3. The game-over screen: the Hot Cargo loss title and run numbers, career numbers, and a line for clients,
    /// which see no buttons in vanilla.
    /// </summary>
    [HarmonyPatch(typeof(GameOverGui), MethodType.Constructor, typeof(GameRoot))]
    static class GameOver
    {
        static void Postfix(GameOverGui __instance, GameRoot game) => UxKit.Guard("game over", () =>
        {
            if (game.Mode is not CareerGameModeManager mode) return;
            var box = (LayoutBox)__instance.Children[0];
            var title = (Label)box.Children[0];
            int insertAt = 2;
            if (Run.Current is { } run && run.Mode == mode && run.State.Result == RunResult.Lost && run.T.Ux.RunEndPanel)
            {
                title.Text = UxKit.Text("LossTitle");
                box.Children.Insert(insertAt++, Line($"<s22>{run.State.EndReason}</s22>\n{RunEndUi.StatsLine(run)}"));
            }
            if (!UxKit.On(s => s.GameOverStats)) return;
            int systems = game.Map.Nodes.Count(n => n == game.CurrentMapNode || game.SimArchive.ContainsSim(n));
            box.Children.Insert(insertAt++, Line(UxKit.Format("CareerStatsFmt", mode.PlayerFame.ToString("N0"),
                mode.Missions._completedMissions.Count, systems)));
            if (!game.NetManager.IsHost) box.Children.Insert(insertAt, Line($"<gray>{UxKit.Text("WaitingForHost")}</gray>"));
        });

        static Label Line(string xml)
        {
            var l = new Label { Text = xml };
            l.TextRenderer.Font = WidgetRules.Instance.ShadowGlowFont;
            l.TextRenderer.FontSize = 18;
            l.TextRenderer.XmlFormatting = true;
            l.TextRenderer.HAlignment = HAlignment.Center;
            l.TextRenderer.VAlignment = VAlignment.Center;
            l.AutoSize.AutoWidthMode = AutoSizeMode.Enable;
            l.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
            return l;
        }
    }
}

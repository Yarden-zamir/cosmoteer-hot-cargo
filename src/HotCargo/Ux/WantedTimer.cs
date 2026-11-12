using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Gui;
using Cosmoteer.Modes.Career;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using HarmonyLib;

namespace HotCargo.Ux;

/// <summary>
/// V14. The WANTED label shows a shrinking bar and nothing else. This adds one line under it: the factions that want you,
/// and either "seen: the timer waits" or the time until the search ends. Same data as WantedManager.GetLocalPlayerWantedTimeFractionRemaining.
/// </summary>
static class WantedTimer
{
    static readonly ConditionalWeakTable<WantedLabel, Label> Lines = new();

    [HarmonyPatch(typeof(WantedLabel), MethodType.Constructor, typeof(CareerGameModeManager))]
    static class Create
    {
        static void Postfix(WantedLabel __instance) => UxKit.Guard("wanted timer", () =>
        {
            var line = new Label
            {
                AnchorRect = AnchorPresets.TopCenter,
                Y = 42f,
                InputBlockMode = InputBlockMode.Never,
            };
            line.TextRenderer.Font = WidgetRules.Instance.ShadowFont;
            line.TextRenderer.FontSize = 16;
            line.TextRenderer.XmlFormatting = true;
            line.TextRenderer.HAlignment = HAlignment.Center;
            line.TextRenderer.VAlignment = VAlignment.Top;
            line.TextRenderer.OversizeMode = OversizeMode.None;
            line.TextRenderer.Clip = false;
            line.AutoSize.AutoWidthMode = AutoSizeMode.Enable;
            line.AutoSize.AutoHeightMode = AutoSizeMode.Enable;
            line.AutoSize.Anchor = new Vector2(0.5f, 0f);
            __instance.AddChild(line);
            Lines.AddOrUpdate(__instance, line);
        });
    }

    [HarmonyPatch(typeof(WantedLabel), nameof(WantedLabel.OnBeforeDraw))]
    static class Refresh
    {
        static void Postfix(WantedLabel __instance)
        {
            if (!Lines.TryGetValue(__instance, out var line)) return;
            UxKit.Guard("wanted timer", () =>
            {
                // A hail shows its "HAILED BY" widget right under the WANTED label, so the line steps aside while one is up.
                bool hail = __instance._mode.Game.Gui.RootWidget.Children.OfType<Cosmoteer.Modes.Career.Comms.CommsCallWidget>().Any(w => w.SelfActive);
                line.SelfActive = __instance.SelfActive && !hail && UxKit.On(s => s.WantedTimer);
                if (line.SelfActive) line.Text = Text(__instance._mode);
            });
        }
    }

    internal static string Text(CareerGameModeManager mode)
    {
        var wanted = mode.SimMode.Wanted;
        var now = mode.Game.Sim.LogicalTime;
        var names = new List<string>();
        bool seen = false, searching = false;
        double left = 0;
        foreach (var (faction, infos) in wanted._wantedShips)
        {
            names.Add(GameApp.Rules.GetFactionRules(faction).NameKey.Text);
            var duration = wanted.GetWantedDuration(faction);
            foreach (var info in infos)
            {
                if (info.LastSeenTime is not { } lastSeen) { seen = true; continue; }
                searching = true;
                left = Math.Max(left, (double)(lastSeen + duration - now));
            }
        }
        if (names.Count == 0) return "";
        string by = string.Join(", ", names);
        if (seen) return UxKit.Format("WantedSeenFmt", by);
        // A faction can want you before any of its ships has seen you; then there is no timer yet.
        if (!searching) return UxKit.Format("WantedByFmt", by);
        var t = TimeSpan.FromSeconds(Math.Max(0, left));
        return UxKit.Format("WantedLeftFmt", by, $"{(int)t.TotalMinutes}:{t.Seconds:00}");
    }
}

using System.Runtime.CompilerServices;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Halfling;
using Halfling.Events;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Gui;
using Halfling.Gui.Components.Text;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Ux;

/// <summary>
/// Mission select (spec 800 to 806): the comms missions list and the mission log. Each mission shows a kind icon, chain stages
/// stand out in the chain color, the right side shows the distance (jumps for other systems) and the risk, and a filter and a
/// risk sort join the vanilla sort. The sort, grouping and filter choices stay per player and per list (offers or log).
/// UI only: it reads missions and run state and never changes the sim.
/// </summary>
static class MissionSelect
{
    /// <summary>Text sprite names: hc_mission_&lt;metatype ID&gt; (mod/ux/actions.rules) and the heat icon.</summary>
    const string IconPrefix = "hc_mission_";
    const string HeatIcon = "hc_heat";

    /// <summary>The choices of one list kind. Key: true for the mission log. Local UI state, never saved and never sent.</summary>
    sealed class Choice { public int Sort; public int Group; public MissionFilter Filter; }
    // Limit: the choices last for the game session only. Revisit if players ask for them to survive a restart.
    static readonly Dictionary<bool, Choice> Choices = new() { [false] = new(), [true] = new() };
    static readonly ConditionalWeakTable<MissionsPanel, Choice> PanelChoice = new();

    sealed class Info { public double Time = double.MinValue; public Risk Risk; public string Right = ""; }
    static readonly ConditionalWeakTable<Mission, Info> Infos = new();
    static readonly ConditionalWeakTable<Mission, StrongBox<MissionKind>> Kinds = new();

    /// <summary>Risk of taking a mission: heat it adds on completion and the chance of a trap or an informant.</summary>
    internal readonly record struct Risk(float Heat, float Trap, float Informant, int Fee);

    static Run? RunOf(Mission m) => Run.Current is { } r && r.Game == m.Game ? r : null;
    static bool On => UxKit.On(s => s.MissionSelect);

    internal static MissionKind KindOf(Mission m) => Kinds.GetValue(m, mission => new StrongBox<MissionKind>(Classify(mission))).Value;

    /// <summary>
    /// Hot Cargo metatypes all start with "HotCargo" (mod rules). A chain stage carries the chain mark reward; a shady offer
    /// has the trap objective somewhere in its objective tree.
    /// </summary>
    static MissionKind Classify(Mission m)
    {
        if (m.Rewards.Any(r => r is Chains.ChainStageReward)) return MissionKind.Chain;
        var all = new List<Objective>();
        foreach (var o in m.Objectives) o.GetObjectives(all);
        if (all.Any(o => o is Missions.ShadyTrapObjective)) return MissionKind.Shady;
        return m.MetatypeID.ToString().StartsWith("HotCargo", StringComparison.Ordinal) ? MissionKind.HotCargo : MissionKind.Vanilla;
    }

    static bool IsContract(Mission m) => RunOf(m) is { } run && run.PressureActive && Contracts.ContractBook.Preview(run, m) is not null;

    internal static Risk RiskOf(Mission m)
    {
        if (RunOf(m) is not { } run || !run.PressureActive) return default;
        var t = run.T;
        bool accepted = run.Mode.Missions.ContainsMission(m);
        var terms = accepted ? Contracts.ContractBook.Find(run, m) : Contracts.ContractBook.Preview(run, m);
        int fame = Math.Max(0, m.GetRewardSortValue(Cosmoteer.Modes.Career.Missions.Rewards.Reward.SortValueType.Fame));
        float heat = HeatModel.FameHeat(t.Heat.HeatPerStandardMission, run.HeatMultiplier(run.State.FleetTech), fame, (int)Math.Round(run.StandardMissionFamePaid(run.CurrentTier)))
                     + m.Rewards.OfType<Missions.HeatReward>().Sum(r => r.Heat)
                     + (accepted ? 0f : terms?.FlatHeat ?? 0f);
        float trap = KindOf(m) == MissionKind.Shady ? Missions.MissionModel.TrapChance(t.Missions.ShadyTrapChanceBase, t.Missions.ShadyTrapChancePerHeat, run.State.Heat) : 0f;
        return new Risk(heat, trap, terms?.InformantChance ?? 0f, accepted ? 0 : terms?.Fee ?? 0);
    }

    static Info InfoOf(Mission m)
    {
        var info = Infos.GetOrCreateValue(m);
        double now = App.Clock.Time.Seconds;
        if (now - info.Time < RefreshSeconds) return info;
        info.Time = now;
        info.Risk = RiskOf(m);
        info.Right = RiskText(info.Risk);
        return info;
    }

    /// <summary>Real seconds between two risk updates of one mission: fame and heat change slowly, and each list redraws every frame.</summary>
    const double RefreshSeconds = 1.0;

    static string Image(string name) => $"<image name='{name}' colored='true'/>";

    static string RiskText(Risk r)
    {
        var parts = new List<string>();
        int heat = (int)MathF.Round(r.Heat);
        if (heat != 0) parts.Add(heat > 0 ? $"<bad>{Image(HeatIcon)}+{heat}</bad>" : $"<good>{Image(HeatIcon)}{heat}</good>");
        if (r.Trap > 0) parts.Add($"<bad>{UxKit.Format("RiskTrapFmt", r.Trap * 100f)}</bad>");
        if (r.Informant > 0) parts.Add($"<limited_color>{UxKit.Format("RiskInformantFmt", r.Informant * 100f)}</limited_color>");
        return string.Join(" ", parts);
    }

    static string Tooltip(Mission m)
    {
        var r = InfoOf(m).Risk;
        string kind = UxKit.Text("Kind" + KindOf(m));
        var lines = new List<string> { KindOf(m) == MissionKind.Chain ? ChainColor.FormatWithXml(kind) : kind };
        if (RunOf(m) is not { PressureActive: true }) return lines[0];
        lines.Add(UxKit.Format("RiskHeatFmt", r.Heat.ToString("+0;-0;0")));
        if (r.Trap > 0) lines.Add(UxKit.Format("RiskTrapTipFmt", r.Trap * 100f));
        if (r.Informant > 0) lines.Add(UxKit.Format("RiskInformantTipFmt", r.Informant * 100f));
        if (r.Fee > 0) lines.Add(UxKit.Format("RiskFeeFmt", r.Fee));
        return string.Join("\n", lines);
    }

    /// <summary>The kind icon left of the name: chain stages get the chain icon, other Hot Cargo missions the icon of their metatype.</summary>
    static string Badge(Mission m)
    {
        string name = IconPrefix + m.MetatypeID;
        return KindOf(m) != MissionKind.Vanilla && Halfling.Graphics.Text.TextAssetLibrary.TryGetImage(name, out _, out _) ? Image(name) + " " : "";
    }

    /// <summary>Distance text: the vanilla distance in this system, jumps for another system.</summary>
    static string? Distance(Mission m)
    {
        float here = m.GetDistanceToClosestObjective();
        if (!float.IsInfinity(here)) return Strings.FormatText("MissionLog/DistanceFmt", here / 1000f);
        if (m.GetClosestObjectiveMapNode(out _) is not { } node) return null;
        int jumps = UxKit.Jumps(m.Game, node);
        return jumps < 0 ? UxKit.Text("JumpsNoRoute") : UxKit.Format(jumps == 1 ? "JumpsOneFmt" : "JumpsFmt", jumps);
    }

    internal static Color ChainColor => Events.EventText.Heading;

    /// <summary>The mission name with a prefix. Forwards the name's changes, so the list item updates like the vanilla one.</summary>
    sealed class PrefixedText : ITextProvider
    {
        readonly ITextProvider _inner;
        readonly string _prefix;
        public PrefixedText(string prefix, ITextProvider inner)
        {
            _prefix = prefix;
            _inner = inner;
            WeakEventHandler.Subscribe<ITextProvider, ValueChangedEventArgs<string>>(inner, "TextChanged", OnChanged);
        }
        public string Text => _prefix + _inner.Text;
        public event EventHandler<ValueChangedEventArgs<string>>? TextChanged;
        void OnChanged(object? sender, ValueChangedEventArgs<string> e) =>
            TextChanged?.Invoke(this, new ValueChangedEventArgs<string>(_prefix + e.OldValue, _prefix + e.NewValue));
    }

    [HarmonyPatch(typeof(MissionsPanel.MissionItem), MethodType.Constructor, typeof(Mission), typeof(MissionsPanel.CategoryItem), typeof(bool), typeof(Color?))]
    static class ItemPatch
    {
        static void Postfix(MissionsPanel.MissionItem __instance, Mission mission)
        {
            if (!On) return;
            UxKit.Guard("mission select item", () =>
            {
                var item = __instance._item;
                string badge = Badge(mission);
                if (badge.Length > 0) item.TextProvider = new PrefixedText(badge, mission.NameAndInfo);
                item.ToolTips.TextProvider = new LiveText(() => Tooltip(mission));
                item.ToolTipDisplay = mission.Game.Gui.ToolTipLeft;
                __instance._timeDist.TextRenderer.XmlFormatting = true;
                if (KindOf(mission) == MissionKind.Chain && mission.Completion != CompletionState.Completed)
                    foreach (var r in new[] { item.StateNormalTextRenderer, item.StateHighlightedTextRenderer, item.StatePressedTextRenderer, item.SelectedTextRenderer })
                        r.Color = ChainColor;
            });
        }
    }

    /// <summary>Replaces the right-hand text: distance (jumps for another system), risk, and the vanilla time left.</summary>
    [HarmonyPatch(typeof(MissionsPanel.MissionItem), "RefreshDistance")]
    static class RightTextPatch
    {
        static bool Prefix(MissionsPanel.MissionItem __instance)
        {
            if (!On) return true;
            UxKit.Guard("mission select distance", () =>
            {
                var m = __instance.Mission;
                var parts = new List<string>();
                if (Distance(m) is { } d) parts.Add(d);
                string risk = InfoOf(m).Right;
                if (risk.Length > 0) parts.Add(risk);
                if (m.GetTimeRemaining() is { } left)
                {
                    // Rounded up to whole seconds, like the vanilla text.
                    var span = TimeSpan.FromSeconds(Math.Ceiling((double)left));
                    parts.Add(Strings.FormatText("MissionLog/TimeRemainingFmt", span.Minutes, span.Seconds));
                }
                var label = __instance._timeDist;
                label.SelfActive = parts.Count > 0;
                string text = string.Join(" | ", parts);
                if (label.Text != text) label.Text = text;
            });
            return false;
        }
    }

    /// <summary>The filter hides missions, the same way the vanilla "local only" check does.</summary>
    [HarmonyPatch(typeof(MissionsPanel), "CanAcceptMission")]
    static class FilterPatch
    {
        static void Postfix(MissionsPanel __instance, Mission m, ref bool __result)
        {
            if (!__result || !On || !PanelChoice.TryGetValue(__instance, out var choice) || choice.Filter == MissionFilter.All) return;
            bool pass = true;
            UxKit.Guard("mission select filter", () => pass = MissionSelectModel.Passes(choice.Filter, KindOf(m), IsContract(m)));
            __result = pass;
        }
    }

    /// <summary>Chain stages group first, in the chain color, when the list groups by mission type.</summary>
    [HarmonyPatch(typeof(MissionsPanel), "CategorizeByMissionType")]
    static class ChainsFirstPatch
    {
        static void Postfix(Mission m, ref float sort)
        {
            if (On && KindOf(m) == MissionKind.Chain) sort = float.MinValue / 2f;
        }
    }

    [HarmonyPatch(typeof(MissionsPanel), "GetCategoryItem", typeof(Mission))]
    static class ChainCategoryColorPatch
    {
        static void Postfix(Mission m, MissionsPanel.CategoryItem __result)
        {
            if (On && KindOf(m) == MissionKind.Chain && __result.TextColor is null) __result.TextColor = ChainColor;
        }
    }

    /// <summary>Adds the risk sort and the filter list, and restores this player's choices for the list kind.</summary>
    [HarmonyPatch(typeof(MissionsPanel), MethodType.Constructor, typeof(Cosmoteer.Game.GameRoot), typeof(MissionList), typeof(ITextProvider), typeof(bool), typeof(bool))]
    static class PanelPatch
    {
        static void Postfix(MissionsPanel __instance, bool localOnlyToggle)
        {
            if (!On) return;
            UxKit.Guard("mission select panel", () => Setup(__instance, Choices[localOnlyToggle]));
        }
    }

    static void Setup(MissionsPanel panel, Choice choice)
    {
        PanelChoice.AddOrUpdate(panel, choice);
        panel.AddCustomSort(new SimpleTextProvider(UxKit.Text("SortRisk"), null), CompareRisk);
        // The vanilla top bar: the docked box at the top that holds the grouping and sort lists.
        var bar = panel.DockedChildren.OfType<SimpleBox>().FirstOrDefault(b => b.DockMode == DockMode.Top)
            ?? throw new InvalidOperationException("mission list has no top bar");
        var filter = new DropList();
        filter.ToolTips.TextProvider = new SimpleTextProvider(UxKit.Text("FilterTip"), null);
        foreach (var f in Enum.GetValues<MissionFilter>())
        {
            var item = new ListItem { Text = UxKit.Text("Filter" + f) };
            item.Selected += (_, _) =>
            {
                if (choice.Filter == f) return;
                choice.Filter = f;
                UxKit.Guard("mission select filter", panel.RefreshOrganization);
            };
            filter.AddChild(item);
        }
        bar.AddChild(filter);
        // Four controls share the bar: grouping, sort, filter, and the log's "local only" box.
        var controls = new List<Widget> { panel._categoryMode, panel._sortMode, filter };
        if (panel._localOnly is { } local) controls.Add(local);
        float w = 100f / controls.Count;
        for (int i = 0; i < controls.Count; i++)
        {
            controls[i].PercentileRect = new Rect(i * w, 0f, w, 100f);
            controls[i].Right = i < controls.Count - 1 ? -5f : 0f;
        }
        Restore(panel._sortMode, choice.Sort, i => choice.Sort = i);
        Restore(panel._categoryMode, choice.Group, i => choice.Group = i);
        filter.ListBox.Children[(int)choice.Filter].IsSelected = true;
        // The constructor listed the missions before this filter existed.
        if (choice.Filter != MissionFilter.All) panel.RefreshOrganization();
    }

    /// <summary>Selects the remembered entry of a drop list, then remembers each new pick.</summary>
    static void Restore(DropList list, int index, Action<int> remember)
    {
        var items = list.ListBox.Children;
        if (index > 0 && index < items.Count) items[index].IsSelected = true;
        for (int i = 0; i < items.Count; i++)
        {
            int at = i;
            items[i].Selected += (_, _) => remember(at);
        }
    }

    /// <summary>Lowest risk first; ties keep the vanilla distance order.</summary>
    static int CompareRisk(Mission a, Mission b)
    {
        float heatPerChance = Run.Current?.T.Ux.RiskHeatPerChance ?? 0f;
        float Key(Mission m) { var r = InfoOf(m).Risk; return MissionSelectModel.RiskKey(r.Heat, r.Trap, r.Informant, heatPerChance); }
        int c = Key(a).CompareTo(Key(b));
        return c != 0 ? c : a.GetDistanceToClosestObjectiveMapNode().CompareTo(b.GetDistanceToClosestObjectiveMapNode());
    }
}

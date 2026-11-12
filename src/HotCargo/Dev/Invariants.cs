using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using Halfling.Graphics.Text;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Tech;

namespace HotCargo.Dev;

/// <summary>
/// Spec 990 to 1009: rules that must always hold, checked while the game runs. A broken rule writes
/// "ERROR invariant &lt;name&gt;: &lt;details&gt;" to the log once for each rule and subject. Autotests fail on ERROR lines,
/// and telemetry records every log line, so playtests show them too. Every check only reads the game, so co-op stays in sync.
/// </summary>
public static class Invariants
{
    static readonly HashSet<string> Reported = [];
    static double _sinceCheck;

    /// <summary>Broken rules reported in this game session. For autotests.</summary>
    public static int Broken { get; private set; }

    internal static void Register()
    {
        Run.Hooks.Tick += OnTick;
        Autotest.Fields["invariants_broken"] = () => Broken;
    }

    static bool On => Run.Current?.T.Invariants.Enabled == true;

    /// <summary>Logs a broken rule once for each name and subject.</summary>
    internal static void Fail(string name, string subject, string details)
    {
        lock (Reported)
        {
            if (!Reported.Add($"{name}|{subject}")) return;
            Broken++;
        }
        Log.Error($"invariant {name}: {details}");
    }

    static void OnTick(Run run, SimRoot sim, double dt)
    {
        if (!run.T.Invariants.Enabled) return;
        _sinceCheck += dt;
        if (_sinceCheck < run.T.Invariants.CheckSeconds) return;
        _sinceCheck = 0;
        Errors.Guard("invariants", () =>
        {
            HeatRange(run);
            TechInVaults(sim);
            HostilesWaitForTrigger(run, sim);
            MarkersEndWithMission(run, sim);
            RivalsSymmetric(run, sim);
        });
    }

    /// <summary>Spec 29, 993: heat stays in 0 to 100, and the started stages are the stages that the heat reached.</summary>
    static void HeatRange(Run run)
    {
        var s = run.State;
        if (s.Heat is < 0f or > HeatModel.MaxHeat || float.IsNaN(s.Heat))
            Fail("heat_range", "range", $"heat {s.Heat} is outside 0 to {HeatModel.MaxHeat}");
        int reached = HeatModel.StagesReached(s.Heat, run.T.Heat.Thresholds());
        if (s.StagesStarted != reached)
            Fail("heat_stages", $"{s.StagesStarted}/{reached}", $"{s.StagesStarted} stages started, but heat {s.Heat:0.0} reached {reached}");
    }

    /// <summary>
    /// Spec 14, 991: on human ships only Tech Vaults store tech, so the stored tech that the fleet count reads equals
    /// the units in the vault grids (the units that the vault art shows).
    /// </summary>
    static void TechInVaults(SimRoot sim)
    {
        foreach (Ship ship in Vaults.HumanShips(sim).OrderBy(s => s.UniqueID))
        {
            int stored = ship.Resources.GetResourceTotal(Vaults.TechId, includeStored: true, includeCarried: false);
            int inVaults = Vaults.OnShip(ship).Sum(g => g.Resources);
            if (stored != inVaults)
                Fail("tech_count", $"{ship.UniqueID}/{stored}/{inVaults}",
                    $"{ship.Metadata.PlainShipName}: the fleet count reads {stored} stored tech, but its vaults hold {inVaults}");
        }
    }

    /// <summary>
    /// Spec 994, 995: a Hot Cargo hostile ship attacks the players only after its trigger. A chain ship (tag hc_ch_*) waits until
    /// a chain stage mission of this system is accepted. A hunter waits until heat is above 0 and a stage started, or a hunter wave spawned.
    /// Limit: "attacks" means an attack command on a human ship. A weapon that fires at a ship in range without a command
    /// does not count. Revisit if a playtest shows hits from ships that have no attack command.
    /// </summary>
    static void HostilesWaitForTrigger(Run run, SimRoot sim)
    {
        bool chainAccepted = ChainStageAccepted(run, sim);
        // A wave in this session (a stage, a strike wave, an informant, an event or a test command) is the trigger too.
        bool hunterTrigger = (run.State.Heat > 0f && run.State.StagesStarted > 0) || Hunters.HunterSpawner.WavesSpawned > 0;
        foreach (Ship ship in sim.Ships)
        {
            if (ship.IsHumanPlayerShip || ship.IsJunk || ship.Tags is not { Count: > 0 } tags) continue;
            bool chain = !chainAccepted && tags.Any(t => t.ToString().StartsWith("hc_ch_", StringComparison.Ordinal));
            bool hunter = !hunterTrigger && ship.HasTag(Hunters.HunterSpawner.HunterTag);
            if (!chain && !hunter) continue;
            if (AttackTarget(ship) is not { } target) continue;
            string what = chain ? "chain ship attacks before its stage mission is accepted" : "hunter attacks before a stage at heat above 0 and before any hunter wave";
            Fail(chain ? "hostile_chain_trigger" : "hostile_hunter_trigger", ship.UniqueID.ToString(),
                $"{what}: {ship.Metadata.PlainShipName} [{string.Join(" ", tags)}] targets {target.Metadata.PlainShipName}; " +
                $"heat {run.State.Heat:0.0}, stages {run.State.StagesStarted}, run seconds {run.State.GameSeconds:0}");
        }
    }

    static Ship? AttackTarget(Ship ship) =>
        ship.Commands.ConfirmedCommands.OfType<IAttackCommand>().Select(c => c.Target).FirstOrDefault(t => t is { IsHumanPlayerShip: true });

    static bool ChainStageAccepted(Run run, SimRoot sim)
    {
        var node = sim.NodeId();
        foreach (var c in run.State.Chains.Instances)
            if (c.NodeId.Equals(node) && c.MissionId is { } id
                && run.Mode.Missions.Any(m => m.MissionID.Equals(id) && m.Completion == CompletionState.InProgress))
                return true;
        return false;
    }

    static readonly ID<DoodadRules> TargetArea = new("cosmoteer.poi_target_area");
    static readonly Dictionary<ObjectID, int> _strayMarkerChecks = [];

    /// <summary>
    /// Spec 996: a mission marker (a shown target area POI) belongs to a mission in progress or an offer. A marker without one for two checks
    /// in a row stayed after its mission ended. Limit: only the target area type counts, the marker type that Hot Cargo missions use.
    /// </summary>
    static void MarkersEndWithMission(Run run, SimRoot sim)
    {
        var seen = new HashSet<ObjectID>();
        var offers = Contracts.ContractBook.Offers(sim).Select(o => o.Mission).ToList();
        foreach (Doodad d in sim.Doodads)
        {
            if (d is not POIDoodad poi || poi.Rules.ID != TargetArea || (!poi.IsExplored && poi.IsUndiscovered)) continue;
            if (run.Mode.Missions.Any(m => m.Completion == CompletionState.InProgress && Owns(m, poi)) || offers.Any(m => Owns(m, poi))) continue;
            seen.Add(poi.UniqueID);
            int n = _strayMarkerChecks.GetValueOrDefault(poi.UniqueID) + 1;
            _strayMarkerChecks[poi.UniqueID] = n;
            if (n < 2) continue;
            var ended = run.Mode.Missions.Where(m => m.Completion != CompletionState.InProgress).Select(m => $"{m.MetatypeID} {m.Completion}");
            Fail("mission_marker", poi.UniqueID.ToString(),
                $"marker {poi.UniqueID} ({poi.XmlSafeCustomName ?? poi.Rules.ID.ToString()}) at {poi.WorldLocation} belongs to no mission in progress; ended missions: [{string.Join(", ", ended)}]");
        }
        foreach (var id in _strayMarkerChecks.Keys.Where(k => !seen.Contains(k)).ToList()) _strayMarkerChecks.Remove(id);
    }

    /// <summary>
    /// A mission owns a marker when an objective claims it (IsPOIAssociated) or has its area at the marker. Vanilla bounty
    /// objectives only point at their target area by location, so the claim alone misses them.
    /// </summary>
    static bool Owns(Mission m, POIDoodad poi) =>
        m.IsPOIAssociated(poi) || m.Objectives.Any(o => o.GetAreaLocation(m.Game) is { } area && area.Center.DistanceTo(poi.WorldLocation) < 1f);

    /// <summary>Spec 103, 997: rivals are symmetric. When accepting A dismisses B, accepting B dismisses A.</summary>
    static void RivalsSymmetric(Run run, SimRoot sim)
    {
        if (!run.T.Contracts.RivalsEnabled) return;
        foreach (var (m, _) in Contracts.ContractBook.Offers(sim))
        {
            if (Contracts.ContractBook.RivalOf(run, sim, m) is not { } rival) continue;
            if (Contracts.ContractBook.RivalOf(run, sim, rival) is { } back && back.MissionID.Equals(m.MissionID) && !rival.MissionID.Equals(m.MissionID)) continue;
            Fail("rival_symmetric", m.MissionID.ToString(),
                $"offer {m.MetatypeID} {m.MissionID} names rival {rival.MetatypeID} {rival.MissionID}, which does not name it back " +
                $"(group '{Contracts.ContractBook.Find(run, m)?.RivalGroup}'; offer lists that hold {rival.MissionID}: " +
                $"{Contracts.ContractBook.Offers(sim).Count(o => o.Mission.MissionID.Equals(rival.MissionID))})");
        }
    }

    /// <summary>
    /// Spec 992: a vault loses tech only when a hit takes its health past a step (VaultDamagePerUnitLost × max health), one unit per step.
    /// The prefix runs before every other patch and the postfix after them, so the check sees the result of all of them.
    /// </summary>
    [HarmonyPatch(typeof(Part), "OnHealthChanged")]
    static class VaultStepPatch
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(Part __instance, out int __state) => __state = Grid(__instance)?.Resources ?? -1;

        [HarmonyPriority(Priority.Last)]
        static void Postfix(Part __instance, int oldHealth, int newHealth, int __state) => Errors.Guard("invariant vault step", () =>
        {
            if (__state < 0 || newHealth == 0 || !On || Grid(__instance) is not { } grid) return;
            int lost = __state - grid.Resources;
            if (lost == 0 && newHealth >= oldHealth) return;
            int max = __instance.Rules.MaxHealth;
            float step = Run.Current!.T.Tech.VaultDamagePerUnitLost * max;
            int crossed = InvariantsModel.StepsCrossed(max, oldHealth, newHealth, step);
            int expected = Math.Min(crossed, __state);
            if (lost == expected) return;
            Fail("vault_step", $"{__instance.Ship?.UniqueID}/{__instance.Location}/{oldHealth}/{newHealth}",
                $"vault at {__instance.Location} on {__instance.Ship?.Metadata.PlainShipName}: health {oldHealth} -> {newHealth} of {max} " +
                $"crossed {crossed} steps of {step:0}, but the vault lost {lost} tech ({__state} -> {grid.Resources})");
        });

        static TypedResourceGrid? Grid(Part part) =>
            part.Rules.ID == Vaults.VaultId ? part.Components.OfType<TypedResourceGrid>().FirstOrDefault(g => g.ResourceType == Vaults.TechId) : null;
    }

    /// <summary>
    /// Spec 999: every heat change comes from a documented source. Telemetry names the source (TELEMETRY.md, Sources and causes);
    /// a change that matches no source rule gets the caller type as its source, and that breaks the rule.
    /// Limit: it runs only while the Telemetry Heat category is on. Revisit if play turns telemetry off.
    /// </summary>
    internal static void HeatSource(string source, string? caller, float delta)
    {
        if (!On || delta == 0f || Telemetry.TelemetryPatches.IsKnownSource(source)) return;
        Fail("heat_source", source, $"heat changed by {delta:0.###} from an undocumented source '{source}' (caller {caller})");
    }

    /// <summary>
    /// Spec 998: shown text has no raw markup and no unfilled placeholder. A text that a renderer gets is checked at the end of
    /// the frame, after the widget set its XML switch. GUI only: it reads widgets and never touches the sim.
    /// </summary>
    static class ShownText
    {
        static readonly HashSet<TextRenderer> Pending = [];

        static bool _subscribed;

        internal static void Add(TextRenderer r)
        {
            if (!On) return;
            // The frame director does not exist yet when the mod boots, so the check subscribes at the first shown text.
            if (!_subscribed)
            {
                Halfling.App.Director.FrameEnded += (_, _) => CheckPending();
                _subscribed = true;
            }
            Pending.Add(r);
        }

        internal static void CheckPending()
        {
            if (Pending.Count == 0) return;
            foreach (var r in Pending)
                if (InvariantsModel.TextProblem(r.Text, r.XmlFormatting) is { } problem)
                    Fail("shown_text", r.Text, $"{problem}: \"{(r.Text.Length > 160 ? r.Text[..160] + "..." : r.Text)}\"");
            Pending.Clear();
        }
    }

    /// <summary>A new text, or a switch of the XML formatting, queues the renderer for the check at the end of the frame.</summary>
    [HarmonyPatch]
    static class TextSetPatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
        [
            AccessTools.PropertySetter(typeof(TextRenderer), nameof(TextRenderer.Text)),
            AccessTools.PropertySetter(typeof(TextRenderer), nameof(TextRenderer.XmlFormatting)),
        ];

        static void Postfix(TextRenderer __instance)
        {
            if (__instance.Text is { Length: > 0 } text && text.AsSpan().IndexOfAny("<{&") >= 0) ShownText.Add(__instance);
        }
    }
}

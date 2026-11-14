using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
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
            HuntersOneSide(sim);
        });
    }

    /// <summary>
    /// Spec 1002: a hunter that the mod spawns has no human player index and is an enemy of every human ship. Players can take a
    /// hunter over later; that does not break the rule. Runs in the sim tick at each spawn.
    /// </summary>
    internal static void HunterSpawned(Ship hunter)
    {
        if (!On || hunter.Sim is not { } sim) return;
        var friends = sim.Ships.HumanPlayerSignificantShips.Where(h => !h.IsEnemiesWith(hunter)).Select(h => h.Metadata.PlainShipName).ToList();
        if (!hunter.IsHumanPlayerShip && friends.Count == 0) return;
        Fail("hunter_spawn_hostile", hunter.UniqueID.ToString(),
            $"new hunter {hunter.Metadata.PlainShipName} has player index {hunter.Metadata.PlayerIndex} (human: {hunter.IsHumanPlayerShip}) " +
            $"and is not an enemy of [{string.Join(", ", friends)}]");
    }

    /// <summary>
    /// Spec 33, 1003: the hunters of a system, live and in stasis, are one side with one player index. Wrecks and hunter ships that the
    /// players took over do not count.
    /// </summary>
    static void HuntersOneSide(SimRoot sim)
    {
        if (sim.Mode is not Cosmoteer.Modes.Career.CareerSimModeManager career) return;
        var sides = Hunters.HunterSpawner.SideIndexes(sim, career);
        if (sides.Count > 1) Fail("hunter_one_side", string.Join("/", sides), $"hunters in this system have {sides.Count} player indexes: [{string.Join(", ", sides)}]");
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
    /// Spec 994, 995: a Hot Cargo hostile ship attacks the players only after its trigger. A chain ship (tag hc_ch_*) with the hunter AI
    /// waits until a chain mission is in progress: a stage mission of this system that the players accepted, or a Return or Arrival
    /// mission, which goes straight to the log. A ship of a stage offer, with any AI, waits for the accept (spec 1127). After its stage
    /// ends, a guard with the vanilla Normal AI may defend its post like a vanilla bounty guard.
    /// A hunter waits until heat is above 0 and a stage started, or a hunter wave spawned.
    /// Limit: "attacks" means an attack command on a human ship. A weapon that fires at a ship in range without a command
    /// does not count. Revisit if a playtest shows hits from ships that have no attack command.
    /// </summary>
    static void HostilesWaitForTrigger(Run run, SimRoot sim)
    {
        bool chainAccepted = ChainMissionInProgress(run);
        var held = run.State.Chains.Instances.SelectMany(c => c.HeldShips.Keys).ToHashSet();
        // A wave in this session (a stage, a strike wave, an informant, an event or a test command) is the trigger too.
        bool hunterTrigger = (run.State.Heat > 0f && run.State.StagesStarted > 0) || Hunters.HunterSpawner.WavesSpawned > 0;
        foreach (Ship ship in sim.Ships)
        {
            if (ship.IsHumanPlayerShip || ship.IsJunk || ship.Tags is not { Count: > 0 } tags) continue;
            bool chain = held.Contains(ship.UniqueID)
                || !chainAccepted && Ai.AiGate.IsHunterAi(ship) && tags.Any(t => t.ToString().StartsWith("hc_ch_", StringComparison.Ordinal));
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

    /// <summary>Every chain mission has the HotCargoChain metatype: stage missions that the players accepted, and Return and Arrival missions.</summary>
    static bool ChainMissionInProgress(Run run) =>
        run.Mode.Missions.Any(m => m.MetatypeID == Chains.ChainLibrary.Metatype && m.Completion == CompletionState.InProgress);

    static readonly HashSet<ObjectID> _missionMarkers = [];
    static readonly Dictionary<ObjectID, int> _strayMarkerChecks = [];

    /// <summary>
    /// Spec 996: a mission marker (a target area marker that a mission owns, MissionMarkers.Of) goes with its mission. A marker that a
    /// mission owned at an earlier check and that no mission in progress or offer owns for two checks in a row stayed after its mission
    /// ended. Map rings that a mission only uses (belt passes) are never mission markers.
    /// Limit: a marker whose mission ends before its first check is not seen. Revisit if a mission can end within a second of its offer.
    /// </summary>
    static void MarkersEndWithMission(Run run, SimRoot sim)
    {
        var owned = run.Mode.Missions.Where(m => m.Completion == CompletionState.InProgress)
            .Concat(Contracts.ContractBook.Offers(sim).Select(o => o.Mission)).SelectMany(Missions.MissionMarkers.Of).ToHashSet();
        _missionMarkers.UnionWith(owned);
        var seen = new HashSet<ObjectID>();
        foreach (var id in _missionMarkers.ToList())
        {
            if (sim.Doodads[id] is not POIDoodad poi || poi.Rules.ID != Missions.MissionMarkers.TargetArea)
            {
                // Gone, or a marker of another system (object IDs belong to one system).
                if (sim.Doodads[id] is null) continue;
                _missionMarkers.Remove(id);
                continue;
            }
            if (owned.Contains(id)) continue;
            seen.Add(id);
            int n = _strayMarkerChecks.GetValueOrDefault(id) + 1;
            _strayMarkerChecks[id] = n;
            if (n < 2) continue;
            var ended = run.Mode.Missions.Where(m => m.Completion != CompletionState.InProgress).Select(m => $"{m.MetatypeID} {m.Completion}");
            Fail("mission_marker", id.ToString(),
                $"marker {id} ({poi.XmlSafeCustomName ?? poi.Rules.ID.ToString()}) at {poi.WorldLocation} belongs to no mission in progress; ended missions: [{string.Join(", ", ended)}]");
        }
        foreach (var id in _strayMarkerChecks.Keys.Where(k => !seen.Contains(k)).ToList()) _strayMarkerChecks.Remove(id);
    }

    /// <summary>
    /// Spec 103, 997: rivals are symmetric. When accepting A dismisses B, accepting B dismisses A. One offer can sit in several lists
    /// as copies with the same MissionID, so offers compare by MissionID, and an offer never names its own MissionID.
    /// </summary>
    static void RivalsSymmetric(Run run, SimRoot sim)
    {
        if (!run.T.Contracts.RivalsEnabled) return;
        var checkedIds = new HashSet<ObjectID>();
        foreach (var (m, _) in Contracts.ContractBook.Offers(sim))
        {
            if (!checkedIds.Add(m.MissionID)) continue;
            foreach (var (rival, _) in Contracts.ContractBook.RivalsOf(run, sim, m))
            {
                bool namesBack = Contracts.ContractBook.RivalsOf(run, sim, rival).Any(r => r.Mission.MissionID == m.MissionID);
                if (namesBack && rival.MissionID != m.MissionID) continue;
                Fail("rival_symmetric", $"{m.MissionID}/{rival.MissionID}",
                    $"offer {m.MetatypeID} {m.MissionID} names rival {rival.MetatypeID} {rival.MissionID}, which does not name it back " +
                    $"(group '{Contracts.ContractBook.Find(run, m)?.RivalGroup}')");
            }
        }
    }

    /// <summary>
    /// Spec 65, 992: a vault loses tech only when it is destroyed. A health change that leaves the vault standing costs no tech.
    /// The prefix runs before every other patch and the postfix after them, so the check sees the result of all of them.
    /// </summary>
    [HarmonyPatch(typeof(Part), "OnHealthChanged")]
    static class VaultHitPatch
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(Part __instance, out int __state) => __state = Grid(__instance)?.Resources ?? -1;

        [HarmonyPriority(Priority.Last)]
        static void Postfix(Part __instance, int oldHealth, int newHealth, int __state) => Errors.Guard("invariant vault hit", () =>
        {
            if (__state < 0 || newHealth == 0 || !On || Grid(__instance) is not { } grid) return;
            if (grid.Resources == __state) return;
            Fail("vault_hit", $"{__instance.Ship?.UniqueID}/{__instance.Location}/{oldHealth}/{newHealth}",
                $"vault at {__instance.Location} on {__instance.Ship?.Metadata.PlainShipName}: health {oldHealth} -> {newHealth} of {__instance.Rules.MaxHealth} " +
                $"left it standing, but its tech changed {__state} -> {grid.Resources}");
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

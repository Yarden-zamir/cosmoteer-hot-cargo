using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Doodads;
using HotCargo.Core;

namespace HotCargo.Missions;

/// <summary>A Hot Cargo objective that owns target area markers: they exist for this objective and go with its mission.</summary>
internal interface IMissionMarkers
{
    IEnumerable<ObjectID> MarkerIds { get; }
}

/// <summary>
/// Spec 996: a mission's target area markers go when the mission leaves the log (completed, failed or abandoned).
/// Vanilla removes a bounty marker only when its ships die (AutoRemoveWhenTagDestroyed), so an abandoned bounty kept its marker too.
/// Runs from the mission log's MissionRemoved event, which every client gets at the same sim step (like ChainRunner).
/// Markers in another system cannot go at once (object IDs belong to one system): the run remembers them, and the next arrival in
/// that system removes them (spec 1126).
/// </summary>
internal static class MissionMarkers
{
    internal static readonly ID<DoodadRules> TargetArea = new("cosmoteer.poi_target_area");
    static MissionList? _watched;

    internal static void Register()
    {
        Run.Hooks.Tick += (run, _, _) => Watch(run);
        Run.Hooks.SystemEntered += (run, _) => Errors.Guard("mission markers arrival", () => OnSystemEntered(run));
    }

    /// <summary>
    /// The markers that the objectives of a mission own: the POI of the vanilla objective types that name one (POIDoodadID; these four
    /// are every such type in the 0.30.5a objectives) and the markers of Hot Cargo objectives. A map ring that a mission only uses
    /// (a belt pass for Hold the Pass) is not in the list.
    /// </summary>
    internal static IEnumerable<ObjectID> Of(Mission m) => Objectives(m).SelectMany(OwnedBy);

    static List<Objective> Objectives(Mission m)
    {
        var objectives = new List<Objective>();
        m.GetObjectives(objectives);
        return objectives;
    }

    static IEnumerable<ObjectID> OwnedBy(Objective o)
    {
        ObjectID? vanilla = o switch
        {
            DefeatShipsObjective d => d.POIDoodadID,
            ReceiveRewardsObjective r => r.POIDoodadID,
            ProtectShipsObjective p => p.POIDoodadID,
            GotoShipObjective g => g.POIDoodadID,
            _ => null,
        };
        if (vanilla is { } id) yield return id;
        if (o is IMissionMarkers own)
            foreach (var marker in own.MarkerIds) yield return marker;
    }

    /// <summary>Subscribes to the mission log of the current run. A new or loaded game has a new log.</summary>
    static void Watch(Run run)
    {
        if (_watched == run.Mode.Missions) return;
        if (_watched is not null) _watched.MissionRemoved -= OnMissionRemoved;
        _watched = run.Mode.Missions;
        _watched.MissionRemoved += OnMissionRemoved;
    }

    static void OnMissionRemoved(object? sender, Mission m) => Errors.Guard("mission markers", () =>
    {
        if (Run.Current is not { } run || !ReferenceEquals(sender, run.Mode.Missions) || run.Game.Sim is not { } sim) return;
        RemoveOf(run, sim, m, "left the log");
        Remember(run, sim, m);
    });

    /// <summary>
    /// Spec 1126: the markers of a mission that ended while its objectives are in another system. An objective's markers lie in its
    /// first map node; the next arrival there removes them.
    /// </summary>
    static void Remember(Run run, SimRoot sim, Mission m)
    {
        ObjectID here = sim.NodeId();
        foreach (var o in Objectives(m))
        {
            var ids = OwnedBy(o).ToList();
            if (ids.Count == 0 || o.GetFirstMapNode(run.Game)?.UniqueID is not { } node || node.Equals(here)) continue;
            if (!run.State.EndedMarkers.TryGetValue(node, out var set)) run.State.EndedMarkers[node] = set = [];
            set.UnionWith(ids);
            Log.Info($"mission {m.MetatypeID} {m.MissionID} left the log in another system: {ids.Count} markers wait for the next arrival there");
        }
    }

    /// <summary>Spec 1126: removes the remembered markers of this system, unless a mission in progress or an offer owns them now.</summary>
    static void OnSystemEntered(Run run)
    {
        if (run.Game.Sim is not { } sim || !run.State.EndedMarkers.Remove(sim.NodeId(), out var ids)) return;
        var owned = run.Mode.Missions.Where(o => o.Completion == CompletionState.InProgress)
            .Concat(Contracts.ContractBook.Offers(sim).Select(o => o.Mission)).SelectMany(Of).ToHashSet();
        foreach (var id in ids.OrderBy(i => i))
        {
            if (owned.Contains(id) || sim.Doodads[id] is not POIDoodad poi || poi.Rules.ID != TargetArea) continue;
            sim.Doodads.Remove(poi, dispose: true);
            Log.Info($"arrival: marker {id} of an ended mission removed");
        }
    }

    /// <summary>
    /// Removes the markers of a mission that ended or of an offer that went away (a dismissed rival, spec 103), unless another mission
    /// in progress or offer owns them. Call it from sim code only.
    /// </summary>
    internal static void RemoveOf(Run run, SimRoot sim, Mission m, string why)
    {
        // Object IDs belong to one system: a marker of a mission in another system is not in this sim, so nothing goes.
        var others = run.Mode.Missions.Where(o => o.MissionID != m.MissionID && o.Completion == CompletionState.InProgress)
            .Concat(Contracts.ContractBook.Offers(sim).Select(o => o.Mission).Where(o => o.MissionID != m.MissionID))
            .SelectMany(Of).ToHashSet();
        foreach (var id in Of(m).Distinct())
        {
            if (others.Contains(id) || sim.Doodads[id] is not POIDoodad poi || poi.Rules.ID != TargetArea) continue;
            sim.Doodads.Remove(poi, dispose: true);
            Log.Info($"mission {m.MetatypeID} {m.MissionID} {why}: marker {id} removed");
        }
    }
}

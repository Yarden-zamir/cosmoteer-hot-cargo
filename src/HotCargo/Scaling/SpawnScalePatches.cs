using System.Reflection.Emit;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Simulation;
using Halfling.Random;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Scaling;

/// <summary>
/// Spec 332 to 335: the one place where rules spawns of hostile ships scale. Every rules ship spawn goes through ShipSpawner:
/// vanilla system generation, encounters, and the Hot Cargo rules spawns (events, chains, archetypes, shady trap, mission groups).
/// The rules decide what scales, so a new rules spawner follows them without code:
/// - Hostile: unique or barbarian allegiance (the effective one when a parent synchronizes allegiance). Faction, civilian,
///   neutral and junk ships never scale. Police scale through their own cap (PoliceCount).
/// - A group (Count above 1, no sub-spawners) scales its count. A spawner with sub-spawners spawns a mission or marker for each
///   ship, so it keeps its count, and only a fleet (2 or more ships for one spawn, the vanilla fleet bounty) gets more ships.
/// - A single ship (a bounty target, a mission carrier) and stations never scale. A single ship whose spawner has the tag
///   hc_scale_fleet (the vanilla ambush, mod/scaling/actions.rules) grows into a fleet, because its mission targets the fleet.
/// </summary>
[HarmonyPatch]
static class SpawnScalePatches
{
    static readonly ID<SimObjectSpawner> ScaleFleetTag = new("hc_scale_fleet");
    const SpawnedShipFeatures StationFeatures = SpawnedShipFeatures.Station | SpawnedShipFeatures.OperationalStation | SpawnedShipFeatures.Landmark;

    /// <summary>Hostile means barbarian, or an AI index that is no faction's (Unique allocates them above all faction indices).</summary>
    static bool IsHostile(ShipSpawner s) =>
        SimObjectSpawner.s_synchronizedAllegiance is { } index
            ? index == PlayerIndexes.Barbarian || (index >= PlayerIndexes.MinAITruceIndex && FactionRules.GetFaction(index) is null)
            : s.Allegiance is ShipSpawner.ShipAllegiance.Unique or ShipSpawner.ShipAllegiance.Barbarian;

    static bool Scales(ShipSpawner s) => (s.Features & StationFeatures) == 0 && IsHostile(s);

    static bool IsGroup(ShipSpawner s) => s.Count.Max > 1 && s.SubSpawners is not { Length: > 0 };

    [HarmonyPostfix, HarmonyPatch(typeof(SimObjectSpawner), nameof(SimObjectSpawner.GetCount))]
    static void GroupCount(SimObjectSpawner __instance, SimRoot sim, Rand rand, ref int __result)
    {
        if (__instance is not ShipSpawner s) return;
        int raw = __result;
        try
        {
            if (IsGroup(s) && Scales(s)) __result = EnemyScale.Count(raw, sim, rand);
        }
        catch (Exception ex)
        {
            __result = raw;
            Errors.Report("enemy scaling (group)", ex.Message, ex);
        }
    }

    /// <summary>
    /// Spec 332: while a scaled group spawns, its spacing from its own tags (pirate from pirate) shrinks by √factor, so the
    /// extra ships find a place. Spacing from other tags (sun, stations) stays. Rules objects are shared, so the finalizer
    /// puts the original list back.
    /// </summary>
    [HarmonyPatch(typeof(SimObjectSpawner), "DoSpawn",
        [typeof(SimRoot), typeof(Rand), typeof(List<SimSpawner.SpawnedObject>), typeof(uint), typeof(uint), typeof(bool),
         typeof(List<SimSpawner.SpawnedObject>), typeof(SpawnedObjectCollection), typeof(HashSet<ID<SimSpawner.SpawnFlag>>), typeof(float)],
        [ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Ref, ArgumentType.Normal,
         ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal])]
    static class GroupSpacing
    {
        static void Prefix(SimObjectSpawner __instance, SimRoot sim, out List<(ID<SimObjectSpawner> Tag, float Distance)>? __state)
        {
            __state = null;
            if (__instance is not ShipSpawner { MinDistanceFromTags: { } spacing, Tags: { } tags } s) return;
            try
            {
                if (!IsGroup(s) || !Scales(s)) return;
                double factor = EnemyScale.Factor(sim);
                if (factor <= 1 || !spacing.Any(t => tags.Contains(t.Tag))) return;
                s.MinDistanceFromTags = spacing.Select(t => tags.Contains(t.Tag) ? (t.Tag, ScalingModel.Spacing(t.Distance, factor)) : t).ToList();
                __state = spacing;
            }
            catch (Exception ex)
            {
                Errors.Report("enemy scaling (spacing)", ex.Message, ex);
            }
        }

        static void Finalizer(SimObjectSpawner __instance, List<(ID<SimObjectSpawner> Tag, float Distance)>? __state)
        {
            if (__state is not null) __instance.MinDistanceFromTags = __state;
        }
    }

    /// <summary>Extra fleet ships copy random ships of the fleet. They join its fleet and tags, so fleet objectives count them.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(ShipSpawner), nameof(ShipSpawner.PickSpawnShips))]
    static void FleetShips(ShipSpawner __instance, SimRoot sim, Rand rand,
        List<(ID<BuiltinShipRules> ShipID, IReadOnlySet<ID<BuiltinShipTag>>? Tags, ID<FactionRules>? Faction)> toSpawn)
    {
        int fleet = toSpawn.Count;
        try
        {
            bool fleetSpawn = fleet >= 2 || __instance.Tags?.Contains(ScaleFleetTag) == true;
            if (fleet == 0 || !fleetSpawn || IsGroup(__instance) || !Scales(__instance)) return;
            int wanted = EnemyScale.Count(fleet, sim, rand);
            for (int i = fleet; i < wanted; i++) toSpawn.Add(toSpawn[rand.Int32(0, fleet - 1)]);
        }
        catch (Exception ex)
        {
            toSpawn.RemoveRange(fleet, toSpawn.Count - fleet);
            Errors.Report("enemy scaling (fleet)", ex.Message, ex);
        }
    }

    /// <summary>Spec 335: the police that hunt a wanted player. WantedManager keeps spawning police until the cap holds.</summary>
    [HarmonyTranspiler, HarmonyPatch(typeof(WantedManager), "FixedUpdate")]
    static IEnumerable<CodeInstruction> PoliceCount(IEnumerable<CodeInstruction> instructions)
    {
        var field = AccessTools.Field(typeof(ReputationTitleRules), nameof(ReputationTitleRules.WantedPoliceCount));
        var cap = AccessTools.Method(typeof(SpawnScalePatches), nameof(PoliceCap));
        var result = new List<CodeInstruction>();
        int replaced = 0;
        foreach (var ci in instructions)
        {
            result.Add(ci);
            if (!ci.LoadsField(field)) continue;
            replaced++;
            result.Add(new CodeInstruction(OpCodes.Ldarg_0));
            result.Add(new CodeInstruction(OpCodes.Call, cap));
        }
        if (replaced == 0)
            Errors.Report("enemy scaling (police)", "no read of WantedPoliceCount in WantedManager.FixedUpdate; police do not scale");
        return result;
    }

    static int PoliceCap(int count, WantedManager wanted)
    {
        try { return EnemyScale.Cap(count, wanted.Sim); }
        catch (Exception ex)
        {
            Errors.Report("enemy scaling (police)", ex.Message, ex);
            return count;
        }
    }
}

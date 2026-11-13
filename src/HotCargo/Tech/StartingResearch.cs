using Cosmoteer.Data;
using Cosmoteer.Modes.Career.TechTree;
using HotCargo.Core;

namespace HotCargo.Tech;

/// <summary>Spec 1015: a new run starts with the StartingResearch techs unlocked, like the techs that the starter ship needs.</summary>
internal static class StartingResearch
{
    internal static void OnNewRun(Run run)
    {
        foreach (string id in run.T.Tech.StartingResearch)
        {
            var tech = new ID<TechRules>(id);
            if (!run.Mode.Techs.Rules.TechsByID.ContainsKey(tech)) throw new InvalidOperationException($"StartingResearch: no career tech {id}");
            run.Mode.Techs.UnlockTech(tech);
        }
        Log.Info($"starting research unlocked: {string.Join(", ", run.T.Tech.StartingResearch)}");
    }
}

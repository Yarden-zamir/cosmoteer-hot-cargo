using HarmonyLib;

namespace HotCargo;

public static class Mod
{
    public const string HarmonyId = "yarden_zamir.hot_cargo";

    /// <summary>Folder that holds mod.rules and tunables.rules. The DLL lives in its csharp subfolder.</summary>
    public static string ModRoot { get; private set; } = "";

    public static void Boot(string dllDir)
    {
        ModRoot = Path.GetFullPath(Path.Combine(dllDir, ".."));
        try
        {
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(typeof(Mod).Assembly);
            Core.TunablesDefaults.WriteFile();
            Tech.TechArea.Register();
            Journey.JourneyArea.Register();
            Hunters.HunterArea.Register();
            Missions.MissionArea.Register();
            Missions.Mechanics.MechanicsArea.Register();
            Tactics.TacticsArea.Register();
            Events.EventsArea.Register();
            Friendly.FriendlyArea.Register();
            Chains.ChainsArea.Register();
            SharedChoice.SharedChoiceArea.Register();
            Contracts.ContractsArea.Register();
            Ux.UxArea.Register();
            Worlds.WorldsArea.Register();
            Balance.BalanceArea.Register();
            ShipAi.ShipAiArea.Register();
            Autonomy.AutonomyArea.Register();
            Scaling.ScalingArea.Register();
            RunModes.RunModesArea.Register();
            Factions.FactionsArea.Register();
            TunablesMenu.TunablesMenuArea.Register();
            MultiChoice.MultiChoiceArea.Register();
            // Relays registers its comms provider before Diplomacy, so a relay page owns its shared choices (spec 958).
            Relays.RelaysArea.Register();
            Debris.DebrisArea.Register();
            Stash.StashArea.Register();
            Diplomacy.DiplomacyArea.Register();
            Debt.DebtArea.Register();
            // After Hunters: its stage hooks read the ships that the Hunters area just spawned.
            Pursuit.PursuitArea.Register();
            Telemetry.TelemetryArea.Register();
            Dev.Invariants.Register();
            Dev.Playthrough.Register();
            // Last: its run log hooks run after the hooks of the other areas.
            StartJobs.StartJobsArea.Register();
            RunRecord.RunRecordArea.Register();
            Onboarding.OnboardingArea.Register();
            Log.Info($"harmony patched {harmony.GetPatchedMethods().Count()} methods; mod root {ModRoot}");
        }
        catch (Exception ex)
        {
            Log.Error("boot failed", ex);
            throw;
        }
    }
}

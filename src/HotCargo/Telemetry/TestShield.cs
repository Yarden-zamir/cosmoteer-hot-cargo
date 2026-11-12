using Cosmoteer.Crew;
using Cosmoteer.Ships.Parts;
using HarmonyLib;

namespace HotCargo.Telemetry;

/// <summary>
/// Test only: the autotest command "shieldplayers on" keeps human ship parts from losing health and human crew from dying,
/// so a scripted run (telemetry-run) does not depend on the starter ship surviving. Repairs still work. Single player only:
/// it changes the sim from a GUI command. Turn it off around test commands that damage on purpose (vault damage).
/// </summary>
static class TestShield
{
    internal static bool On;

    static bool Shields(Part part) => On && part.Ship is { IsHumanPlayerShip: true };

    /// <summary>The Health setter; a lower value keeps the current health.</summary>
    [HarmonyPatch(typeof(Part), nameof(Part.Health), MethodType.Setter)]
    static class HealthSetter
    {
        static void Prefix(Part __instance, ref int value)
        {
            if (Shields(__instance) && value < __instance.Health) value = __instance.Health;
        }
    }

    /// <summary>Damage with salvage (AddDamageAndSalvage goes through here).</summary>
    [HarmonyPatch(typeof(Part), nameof(Part.SetHealthAndSalvageDamage))]
    static class HealthAndSalvage
    {
        static void Prefix(Part __instance, ref int health)
        {
            if (Shields(__instance) && health < __instance.Health) health = __instance.Health;
        }
    }

    /// <summary>Crew that hit effects or fires would kill stay alive.</summary>
    [HarmonyPatch(typeof(CrewSoul), nameof(CrewSoul.Kill))]
    static class CrewKill
    {
        static bool Prefix(CrewSoul __instance) =>
            !(On && __instance.Sim?.Game is { } game && game.HumanPlayersIndexes.Contains(__instance.PlayerIndex));
    }
}

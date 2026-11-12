using System.Reflection;
using Cosmoteer.Localization;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Halfling.Random;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Tech;

/// <summary>
/// Spec 65, 445: a destroyed vault drops its tech as crates (the resource drop rate StorageDestroyed = 100 %).
/// With chance VaultTechLossChance the tech burns instead: this empties the tile before the game drops its nuggets, and a toast and
/// a chat line tell the players. The roll is a FastRand seeded from the ship, the tile and the sim tick, so every client rolls the same.
/// </summary>
[HarmonyPatch]
static class VaultDropPatch
{
    const string Method = "Cosmoteer.Ships.Parts.Resources.TypedResourceGrid.IResourceTileMembers.OnDestroying";

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(TypedResourceGrid.ResourceTile), Method) ?? throw new MissingMethodException("TypedResourceGrid.ResourceTile", Method);

    static void Prefix(TypedResourceGrid.ResourceTile __instance) => Errors.Guard("vault drop", () =>
    {
        var grid = __instance.Grid;
        if (grid.ResourceType != Vaults.TechId || !__instance.HasResources || (grid.Part.Flags & PartFlags.Salvaged) != 0) return;
        if (grid.Ship is not { IsHumanPlayerShip: true } ship || ship.Sim is not { } sim || Run.Current is not { } run || run.Game.Sim != sim) return;
        var cell = __instance.ShipCell;
        ulong seed = ((ulong)(uint)ship.UniqueID << 32) | (uint)((cell.X & 0xFFFF) << 16 | (cell.Y & 0xFFFF));
        float roll = new FastRand(seed, (ulong)sim.Tick).Single();
        int units = __instance.Resources;
        if (roll >= run.T.Tech.VaultTechLossChance)
        {
            Log.Info($"destroyed vault at {cell} drops {units} tech as crates (roll {roll:0.000})");
            return;
        }
        __instance.Resources = 0;
        LostMessages++;
        Log.Info($"destroyed vault at {cell}: {units} tech burned (roll {roll:0.000} < {run.T.Tech.VaultTechLossChance})");
        string text = string.Format(Strings.GetText("HotCargoTech/VaultBurnedFmt"), units);
        // Destruction can run on a worker thread, so the toast and the chat line wait for the next frame.
        Run.OnNextFrame(() =>
        {
            Ux.UxKit.CenterText(run.Game, text);
            Ux.UxKit.ChatLine(run.Game, text);
        });
    });

    /// <summary>Destroyed vaults whose tech burned since start. For autotests.</summary>
    internal static int LostMessages;
}

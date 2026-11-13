using Cosmoteer.Gui;
using Cosmoteer.Modes.Career.Comms;
using Halfling.Gui;
using HarmonyLib;

namespace HotCargo.Ux;

/// <summary>
/// Spec 871. Some vanilla UI updates set a label's text in every frame, and Hot Cargo postfixes append to that text. The text then
/// changes twice in each frame, and each change lays out the label and its parent again. These patches give the update a scratch
/// label in place of the real one: vanilla and the postfixes write to the scratch label, and the real label gets the final text once.
/// So the real label changes only when the final text changes.
/// Limit: the update must change only the text of that label. Check this after a game update.
/// </summary>
static class StableText
{
    static void Swap<T>(AccessTools.FieldRef<T, Label> field, T owner, Label scratch, out Label real)
    {
        real = field(owner);
        scratch.Text = real.Text;
        field(owner) = scratch;
    }

    /// <summary>Puts the real label back with the final text. Also runs when the update throws.</summary>
    static void Restore<T>(AccessTools.FieldRef<T, Label> field, T owner, Label? real)
    {
        if (real is null) return;
        real.Text = field(owner).Text;
        field(owner) = real;
    }

    /// <summary>The comms list row: badges (StationBadges, Factions.CommsPatches) after the vanilla IFF text.</summary>
    [HarmonyPatch(typeof(CommsToolbox.CallShipItem), nameof(CommsToolbox.CallShipItem.Update))]
    static class CommsRow
    {
        static readonly AccessTools.FieldRef<CommsToolbox.CallShipItem, Label> Iff = AccessTools.FieldRefAccess<CommsToolbox.CallShipItem, Label>("_iff");
        static readonly Label Scratch = new();

        [HarmonyPriority(Priority.First)]
        static void Prefix(CommsToolbox.CallShipItem __instance, out Label __state) => Swap(Iff, __instance, Scratch, out __state);

        static void Finalizer(CommsToolbox.CallShipItem __instance, Label? __state) => Restore(Iff, __instance, __state);
    }

    /// <summary>The hire tab: the heat line (Factions.CommsPatches) after the vanilla crew count.</summary>
    [HarmonyPatch(typeof(CommHireTab), "OnUpdatingUIState")]
    static class HireTab
    {
        static readonly AccessTools.FieldRef<CommHireTab, Label> Count = AccessTools.FieldRefAccess<CommHireTab, Label>("_fameCountLbl");
        static readonly Label Scratch = new();

        [HarmonyPriority(Priority.First)]
        static void Prefix(CommHireTab __instance, out Label __state) => Swap(Count, __instance, Scratch, out __state);

        static void Finalizer(CommHireTab __instance, Label? __state) => Restore(Count, __instance, __state);
    }
}

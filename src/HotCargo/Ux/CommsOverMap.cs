using Cosmoteer.Modes.Career.Comms;
using HarmonyLib;

namespace HotCargo.Ux;

/// <summary>
/// V12. Vanilla closes every comms window when the galaxy map opens (CommWindow.OnParented subscribes MapShown to close).
/// So a click on a mission location inside comms closes the window, and the player must hail again.
/// With CommsStayOnMap, the window stays open over the map. CommsPause still sees the window as open, so the game stays
/// paused on the map, and a later close (the X button, or a hail that ends) unpauses as before.
/// Limit: the switch is read when a window opens. A window that is already open keeps its behavior.
/// </summary>
[HarmonyPatch(typeof(CommWindow), nameof(CommWindow.OnParented))]
static class CommsOverMap
{
    static void Postfix(CommWindow __instance)
    {
        if (!UxKit.On(s => s.CommsStayOnMap)) return;
        // The same delegate as the vanilla subscription (instance + WindowBox.OnCloseClicked), so the removal matches it.
        // OnUnparenting removes it again; removing a missing handler is a no-op.
        UxKit.Guard("comms over map", () => __instance.Game.MapShown -= __instance.OnCloseClicked);
    }
}

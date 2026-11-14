using Cosmoteer.Localization;
using Cosmoteer.Ships;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Diplomacy;

/// <summary>Text of the diplomacy area, from mod/diplomacy/strings.en.rules (section HotCargoDiplomacy).</summary>
static class DipText
{
    public static string Text(string key) => Strings.GetText("HotCargoDiplomacy/" + key);

    public static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoDiplomacy/" + key, args);
}

/// <summary>
/// Spec 578: ship marker labels. A ship that holds tech shows "HOLDS n TECH" (where the held tech is), a hired ship its time
/// left or "QUITTING", a mercenary "MERCENARY". Reads run state only.
/// </summary>
[HarmonyPatch(typeof(ShipIndicatorSource), nameof(ShipIndicatorSource.GetNonMouseoverText))]
static class DiplomacyMarkers
{
    static void Postfix(ShipIndicatorSource __instance, ref string? __result)
    {
        if (Run.Current is not { } run || !run.T.Diplomacy.Enabled) return;
        try
        {
            if (Label(run, __instance.Ship) is { } label) __result = __result is null ? label : label + "\n" + __result;
        }
        catch (Exception ex) { Errors.Report("diplomacy label", ex.Message, ex); }
    }

    internal static string? Label(Run run, Cosmoteer.Ships.Ship ship)
    {
        var s = run.State.Diplomacy;
        var id = ship.UniqueID;
        // Spec 872: every ship blip asks in every frame, so a ship without a label allocates nothing.
        int held = 0;
        foreach (var h in s.Held) if (h.Ship == id) held += h.Units;
        Hire? hire = null;
        foreach (var h in s.Hires) if (h.Ship == id && !h.HideLabel) { hire = h; break; }
        if (held == 0 && hire is null && !s.Mercs.Contains(id)) return null;
        var parts = new List<string>();
        if (held > 0) parts.Add(DipText.Format("LabelHoldsFmt", held));
        if (hire is not null)
            parts.Add(hire.Fleeing ? DipText.Text("LabelQuitting")
                : DipText.Format("LabelHiredFmt", TimeSpan.FromSeconds(Math.Max(0, hire.Until - run.State.GameSeconds)).ToString(@"m\:ss")));
        else if (s.Mercs.Contains(ship.UniqueID)) parts.Add(DipText.Text("LabelMerc"));
        return parts.Count == 0 ? null : string.Join("\n", parts);
    }
}

using HotCargo.Core;
using HotCargo.Ui;

namespace HotCargo.Diplomacy;

/// <summary>Entry point of the Diplomacy area (spec 560 to 589). Mod.Boot calls Register after the Tech area.</summary>
public static class DiplomacyArea
{
    public static void Register()
    {
        CommsHail.Providers.Add(DiplomacyHail.Instance);
        SharedChoice.SharedChoices.Register(DiplomacyChoiceDomain.Instance);
        Run.Hooks.Tick += DiplomacyTick.OnTick;
        Run.Hooks.SystemEntered += DiplomacyTick.OnSystemEntered;
        // Spec 575: tech that a friendly ship holds counts in the fleet tech count, so it is not lost (loss rule) and the corporation
        // still tracks it (heat multiplier, score).
        var vaults = Run.Hooks.CountFleetTech;
        Run.Hooks.CountFleetTech = sim => vaults(sim) + HeldUnits();
        DiplomacyAutotest.Register();
    }

    static int HeldUnits() => Run.Current?.State.Diplomacy.Held.Sum(h => h.Units) ?? 0;
}

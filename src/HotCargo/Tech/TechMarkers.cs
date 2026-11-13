using Cosmoteer;
using Cosmoteer.Localization;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Graphics;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Tech;

/// <summary>
/// Spec 444: map markers for tech in the current system, with the vanilla object indicators (the blips of the zoomed-out map
/// and the off-screen arrows). Ship blips get a "TECH n" line: human ships with tech in vaults, and ships that other areas register.
/// Each tech crate (nugget) in space gets its own indicator with the Stolen Tech icon. GUI only: nothing here changes the sim.
/// </summary>
public static class TechMarkers
{
    static readonly List<Func<SimRoot, IEnumerable<(Ship Ship, int Units)>>> HolderSources = [];

    /// <summary>
    /// Registers other ships that hold tech for the players, for example a friendly ship that keeps tech to return it later.
    /// The source runs each frame while a blip draws, so keep it cheap.
    /// </summary>
    public static void RegisterHolders(Func<SimRoot, IEnumerable<(Ship Ship, int Units)>> source) => HolderSources.Add(source);

    static bool On => Run.Current is { } run && run.T.Tech.TechMarkers;

    /// <summary>Tech units that the ship's blip shows: tech in the vaults of a human or derelict ship, plus the units of registered holders.</summary>
    public static int ShipUnits(Ship ship)
    {
        if (ship.Sim is not { } sim) return 0;
        int units = Vaults.HoldsForPlayers(sim, ship) ? Vaults.ShipTech(ship) : 0;
        foreach (var source in HolderSources)
            foreach (var (holder, n) in source(sim))
                if (holder == ship) units += n;
        return units;
    }

    [HarmonyPatch(typeof(ShipIndicatorSource), nameof(ShipIndicatorSource.GetNonMouseoverText))]
    static class ShipTag
    {
        static void Postfix(ShipIndicatorSource __instance, ref string? __result)
        {
            if (!On) return;
            string? result = __result;
            Errors.Guard("tech ship marker", () =>
            {
                int units = ShipUnits(__instance.Ship);
                if (units <= 0) return;
                string key = __instance.Ship.IsHumanPlayerShip || __instance.Ship.IsJunk ? "HotCargoTech/MarkerTag" : "HotCargoTech/MarkerHeldTag";
                string tag = string.Format(Strings.GetText(key), units);
                result = result is null ? tag : tag + "\n" + result;
            });
            __result = result;
        }
    }

    /// <summary>Adds a crate indicator to each tech nugget that enters a sim, also nuggets that a save loads.</summary>
    [HarmonyPatch(typeof(SimNuggetManager), nameof(SimNuggetManager.AddNugget))]
    static class CrateAdded
    {
        static void Postfix(Nugget nugget) => Errors.Guard("tech crate marker", () =>
        {
            if (nugget.ResourceType != Vaults.TechId || nugget.Sim is not { } sim) return;
            // The indicator is a component of the nugget node, so it leaves the scene with the nugget.
            // Vanilla adds indicator components through the non-deterministic queue too (CrewSoul distance warnings).
            sim.EnqueueNonDeterministic(() => nugget.Components.Add(new CrateIndicator()));
        });
    }

    /// <summary>
    /// One marker for each group of crates: the crate with the lowest ID within ClusterRadius shows "TECH n", the others hide.
    /// Limit: the group is the crates near the leader, so two groups can share a crate at their edges. Revisit if counts look off.
    /// </summary>
    internal sealed class CrateIndicator : ObjectIndicatorSource
    {
        const float ClusterRadius = 100f;
        static readonly Color Gold = new(1f, 0.78f, 0.2f);
        readonly List<Nugget> _near = [];
        int _units;

        public override Color Color => Gold;
        public override int Sort => 1;
        public override bool AllowInteractions => false;

        public override bool? ShouldShowA(out bool allowOffScreen, out bool isPingable)
        {
            allowOffScreen = true;
            isPingable = false;
            if (!On || Node is not Nugget self || self.Sim is not { } sim) return false;
            _near.Clear();
            sim.Nuggets.FindIntersectingWith(new Halfling.Geometry.Circle(self.WorldLocation, ClusterRadius), _near, n => n.ResourceType == Vaults.TechId);
            if (_near.Any(n => n.UniqueID.CompareTo(self.UniqueID) < 0)) return false;
            _units = Math.Max(1, _near.Sum(n => n.Quantity));
            return true;
        }

        /// <summary>Tech units in the group this marker leads, as of the last frame. For autotests.</summary>
        internal int Units => _units;

        public override void GetIcons(out ISprite normal, out ISprite highlighted, out ISprite pressed)
        {
            ISprite icon = GameApp.Rules.GetResourceRules(Vaults.TechId).Icon ?? throw new InvalidOperationException($"{Vaults.TechId} has no icon");
            normal = highlighted = pressed = icon;
        }

        public override string? GetNonMouseoverText() =>
            _units > 1 ? string.Format(Strings.GetText("HotCargoTech/MarkerTag"), _units) : Strings.GetText("HotCargoTech/MarkerCrate");
    }
}

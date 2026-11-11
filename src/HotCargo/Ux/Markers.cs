using System.Runtime.CompilerServices;
using Cosmoteer.Data;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Ships.Special;
using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Overlays;
using Cosmoteer.Simulation.Stasis;
using Halfling;
using Halfling.Graphics;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Ux;

/// <summary>
/// H5 and H6. A "BUYER" tag on the Buyer station blip, near or far, and a pulsing outline on the Tech Vault parts of your ships.
/// The IDs are the shared IDs from the brief. Before the Tech and Journey areas merge, nothing matches and nothing shows.
/// </summary>
static class Markers
{
    static readonly ID<SimObjectSpawner> BuyerTag = new("hc_buyer");
    static readonly ID<PartRules> VaultPart = new("yarden_zamir.tech_vault");

    static bool ShowBuyer => Run.Current is { } r && r.T.Ux.BuyerMarker;

    static string Tag(string? vanilla)
    {
        string tag = $"<good><b>{UxKit.Text("BuyerTag")}</b></good>";
        return vanilla is null ? tag : tag + "\n" + vanilla;
    }

    [HarmonyPatch(typeof(ShipIndicatorSource), nameof(ShipIndicatorSource.GetNonMouseoverText))]
    static class BuyerShipBlip
    {
        [HarmonyPriority(Priority.Low)]
        static void Postfix(ShipIndicatorSource __instance, ref string? __result)
        {
            if (__instance.Ship.Tags?.Contains(BuyerTag) == true && ShowBuyer) __result = Tag(__result);
        }
    }

    /// <summary>A station out of physics range is a stasis landmark. Its spawner keeps the tags.</summary>
    [HarmonyPatch(typeof(StasisLandmark), nameof(StasisLandmark.GetNonMouseoverText))]
    static class BuyerLandmarkBlip
    {
        static void Postfix(StasisLandmark __instance, ref string? __result)
        {
            if (__instance.StasisSpawner is StasisShipSpawner s && s.Tags?.Contains(BuyerTag) == true && ShowBuyer) __result = Tag(__result);
        }
    }

    // ---- Vault outline

    static SimRoot? _sim;
    static BatchedPartNineSliceRenderer? _renderer;
    static readonly ConditionalWeakTable<Ship, VaultCache> Vaults = new();
    sealed class VaultCache { public double Time = double.MinValue; public List<Part> Parts = []; }

    /// <summary>Hooks the overlay of the current sim. Called each frame; it re-hooks after a system jump or a load.</summary>
    internal static void Update(Run run)
    {
        var sim = run.Game.Sim;
        if (sim == _sim) return;
        if (_sim is not null) _sim.Overlays.DrawingCustomOverlays -= OnDrawOverlays;
        _sim = sim;
        _renderer ??= new BatchedPartNineSliceRenderer(sim.Rules.SimGui.SelectedPartNineSlice);
        sim.Overlays.DrawingCustomOverlays += OnDrawOverlays;
    }

    static void OnDrawOverlays(object? sender, EventArgs e) => UxKit.Guard("vault outline", () =>
    {
        if (Run.Current is not { } run || run.Game.Sim != _sim || !run.T.Ux.VaultMarker || _renderer is null) return;
        double now = App.Clock.Time.Seconds;
        int count = 0;
        foreach (var ship in _sim.Ships.LocalPlayerShips)
            foreach (var part in VaultsOf(ship, now)) { _renderer.QueueNineSlice(part); count++; }
        if (count == 0) return;
        float pulse = 0.65f + 0.35f * MathF.Sin((float)now * 3f);
        _renderer.Draw(_sim, new Color(1f, 0.78f, 0.2f, pulse));
    });

    /// <summary>Vault parts of a ship, found again once a second. Limit: a vault built in the last second shows a moment late.</summary>
    static List<Part> VaultsOf(Ship ship, double now)
    {
        var c = Vaults.GetOrCreateValue(ship);
        if (now - c.Time < 1.0) return c.Parts;
        c.Time = now;
        c.Parts.Clear();
        foreach (var part in ship.Parts) if (part.Rules.ID == VaultPart) c.Parts.Add(part);
        return c.Parts;
    }
}

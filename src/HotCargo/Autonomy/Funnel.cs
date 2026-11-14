using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Ships.Parts.UI;
using Cosmoteer.Ships.Resources;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HotCargo.Core;

namespace HotCargo.Autonomy;

/// <summary>
/// Spec 1054, 1055. Each autonomy tick a Funnel takes in the loot in its area that its filter accepts and moves it into the parts that
/// touch its sides. Ships, Funnels, resource types and nuggets go in a stable order, so every co-op machine moves the same units.
/// </summary>
internal static class Funnel
{
    /// <summary>Units taken in since start. For autotests; not sim state.</summary>
    internal static int Taken;

    internal static FunnelChoice ChoiceOf(Part funnel) =>
        funnel.Components.OfType<PartUIToggle>().FirstOrDefault(t => t.Rules.ToggleID.ToString() == "hc_funnel_filter") is { } toggle
            ? (FunnelChoice)Math.Clamp(toggle.Mode, 0, (int)FunnelChoice.Off)
            : throw new InvalidOperationException($"the Funnel at {funnel.Location} has no filter toggle");

    /// <summary>The unit value of a resource for loot filters: the vanilla sell price with no faction.</summary>
    internal static int UnitValue(SimRoot sim, ID<ResourceRules> type) => sim.Mode.GetResourceSellRefund(type, null);

    internal static void Tick(SimRoot sim, AutonomyTunables tun)
    {
        var found = new List<Nugget>();
        for (int i = 0; i < sim.Ships.Count; i++)
        {
            var ship = sim.Ships[i];
            if (!ship.IsHumanPlayerShip) continue;
            foreach (var part in ship.Parts.Where(p => p.Rules.ID == Autonomous.FunnelId).OrderBy(p => p.Location.X).ThenBy(p => p.Location.Y))
                Errors.Guard("funnel", () => Pull(sim, ship, part, tun, found));
        }
    }

    static void Pull(SimRoot sim, Ship ship, Part funnel, AutonomyTunables tun, List<Nugget> found)
    {
        var choice = ChoiceOf(funnel);
        if (choice == FunnelChoice.Off) return;
        var cargo = ShipAi.ShipTactics.Of(ship)?.Cargo ?? CargoSettings.Default;
        var box = Autonomous.BoxOf(funnel);
        float reach = tun.FunnelReach;
        float radius = (MathF.Max(box.W, box.H) / 2f + reach) * 1.5f * ship.Scale.X;
        var area = new Circle(ship.DetTransformPointToWorld(new Vector2(box.X + box.W / 2f, box.Y + box.H / 2f)), radius);
        foreach (var rules in GameApp.Rules.Resources.OrderBy(r => r.ID.ToString(), StringComparer.Ordinal))
        {
            var type = rules.ID;
            if (!AutonomyModel.FunnelAccepts(choice, cargo, type.ToString(), UnitValue(sim, type))) continue;
            found.Clear();
            sim.Nuggets.DetFindIntersectingWith(type, area, found);
            foreach (var n in found.OrderBy(n => n.UniqueID))
            {
                var local = ship.DetTransformPointFromWorld(n.DetWorldLocation);
                if (!AutonomyModel.InFunnelArea(box, local.X, local.Y, reach)) continue;
                int moved = Deliver(ship, funnel, box, type, n.Quantity);
                if (moved <= 0) break;
                n.SubtractResources(moved);
                Taken += moved;
            }
        }
    }

    /// <summary>Puts up to amount units into the parts that touch the Funnel, in edge tile order. Returns the units placed.</summary>
    static int Deliver(Ship ship, Part funnel, Box box, ID<ResourceRules> type, int amount)
    {
        int left = amount;
        var seen = new HashSet<Part>();
        foreach (var cell in AutonomyModel.EdgeCells(box))
        {
            if (left <= 0) break;
            var part = ship.Parts[new IntVector2(cell.X, cell.Y), PartRectType.Normal];
            if (part is null || part == funnel || !seen.Add(part)) continue;
            foreach (var sink in SinksOf(part, type))
            {
                int room = sink.GetRemainingCapacity(type, allowPreferredTypeMismatch: false) - ship.Resources.GetAnticipatedDelivery(sink, MPValueType.Confirmed);
                if (room <= 0) continue;
                left -= sink.ReceiveResources(type, Math.Min(room, left), isCrewDelivery: false, sticky: false, playSound: false);
                if (left <= 0) break;
            }
        }
        return amount - left;
    }

    /// <summary>Storage bay tiles of a part (grid order), then its typed storages of that resource.</summary>
    internal static IEnumerable<IResourceSink> SinksOf(Part part, ID<ResourceRules> type)
    {
        foreach (var grid in FlexResourceGrid.GetAllOnPart(part))
            foreach (var tile in grid)
                yield return tile;
        foreach (var storage in BaseResourceStorage.GetAllOfTypeOnPart(type, part))
            if (storage is IResourceSink sink) yield return sink;
    }
}

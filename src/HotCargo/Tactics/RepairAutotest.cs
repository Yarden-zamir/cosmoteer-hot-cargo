using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using HotCargo.Ai;
using HotCargo.Dev;
using HotCargo.Hunters;

namespace HotCargo.Tactics;

/// <summary>Autotest commands and fields for the repair plans of fall back (spec 1305-1313). Test only: single player.</summary>
static class RepairAutotest
{
    public static void Register()
    {
        foreach (var plan in Enum.GetValues<RepairPlan>().Where(p => p != RepairPlan.None))
            Autotest.Fields[$"ai_fb_{plan.ToString().ToLowerInvariant()}"] = () => Modules().Count(m => m.Plan == plan);
        foreach (string name in new[] { FallBackStrategy.TakeEvent, Resupply.SupplyEvent })
            Autotest.Fields[$"tactic_{name}"] = () => TacticsLog.Count(name);
        Autotest.Fields["ai_fb_active"] = () => Modules().Count(m => m.Plan != RepairPlan.None);
        Autotest.Fields["ai_fb_in_cover"] = () => Hostile().Count(s => s.AI?.GetStrategyModule<FallBackStrategy>()?.InCover(s) == true);
        Autotest.Fields["ai_flee_to_station"] = () => Hostile().Count(s => s.AI?.GetStrategyModule<Ai.FleeStrategy>()?.StationCenter(s) is not null);
        Autotest.Fields["ai_flee_supplied"] = () => Hostile().Count(s => s.AI?.GetStrategyModule<Ai.FleeStrategy>() is { Supplied: true });
        Autotest.Fields["ai_fb_supplied"] = () => Modules().Count(m => m.Supplied);
        // The lowest health fraction (spec 47) and the lowest crew count of the ships of the last aispawn.
        Autotest.Fields["ai_new_min_health"] = () => New().Select(s => (double)HunterOrders.HealthFraction(s)).DefaultIfEmpty(-1).Min();
        Autotest.Fields["ai_new_first_health"] = () => New().Select(s => (double)HunterOrders.HealthFraction(s)).DefaultIfEmpty(-1).First();
        Autotest.Fields["ai_new_min_crew"] = () => New().Select(s => (double)s.Crew.Assigned.Count).DefaultIfEmpty(-1).Min();

        // aidamage <index>|new <fraction>: sets every part of ship <index> of the last aispawn to this fraction of its health.
        Autotest.Commands["aidamage"] = arg =>
        {
            var (ships, value) = Target(arg);
            float fraction = float.Parse(value, CultureInfo.InvariantCulture);
            foreach (var ship in ships)
                foreach (var part in ship.Parts.ToList()) part.Health = Math.Max(1, (int)(part.Rules.MaxHealth * fraction));
        };
        // aistrip <index>|new resources|crew <keep>: removes every stored resource, or the crew down to <keep>, of ship <index> of the last aispawn.
        // A ship without crew runs no AI at all (ShipAI.FixedUpdate needs Commands.CanExecuteCommands), so a test keeps a few.
        Autotest.Commands["aistrip"] = arg =>
        {
            var (ships, what) = Target(arg);
            foreach (var ship in ships)
            {
                if (what.StartsWith("crew ", StringComparison.Ordinal))
                    ship.Crew.DeleteRandomAssigned(Math.Max(0, ship.Crew.Assigned.Count - int.Parse(what[5..], CultureInfo.InvariantCulture)));
                else if (what == "resources")
                    foreach (var type in GameApp.Rules.Resources.Select(r => r.ID))
                        ship.Resources.RemoveResources(type, ship.Resources.GetResourceTotal(type, includeStored: true, includeCarried: false));
                else throw new InvalidOperationException("aistrip needs: index|new resources|crew <keep>");
                Log.Info($"AUTOTEST stripped {what} of {ship.Metadata.PlainShipName}: crew {ship.Crew.Assigned.Count}");
            }
        };
        // aigivelacking <from> <to>: puts into ship <from> twice what ship <to> (indexes of the last aispawn) lacks for its repairs.
        Autotest.Commands["aigivelacking"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new InvalidOperationException("aigivelacking needs: from to");
            var (from, to) = (At(parts[0]), At(parts[1]));
            foreach (var (id, n) in Resupply.Lacking(to))
            {
                int added = from.Resources.AddResources(new ID<ResourceRules>(id), 2 * n);
                Log.Info($"AUTOTEST {from.Metadata.PlainShipName} gets {added} of {2 * n} {id}");
            }
        };
        // ainear <i> <j> <distance>: moves ship <i> of the aispawn list to this distance from the hull of ship <j>.
        Autotest.Commands["ainear"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) throw new InvalidOperationException("ainear needs: i j distance");
            var (ship, to) = (At(parts[0]), At(parts[1]));
            float d = float.Parse(parts[2], CultureInfo.InvariantCulture);
            Autotest.Teleport(ship, Autotest.ClearSpotNear(ship.Sim!, to.DetWorldCenter, to.WorldBoundingRadius + ship.WorldBoundingRadius + d, ship.WorldBoundingRadius));
        };
        // ainew_camera <i> <size>: the camera stops following ships and shows ship <i> of the aispawn list (screenshots).
        Autotest.Commands["ainew_camera"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new InvalidOperationException("ainew_camera needs: i size");
            var at = At(parts[0]).WorldCenter;
            Autotest.Commands["camera"](string.Create(CultureInfo.InvariantCulture, $"{at.X} {at.Y} {parts[1]}"));
        };
        // aiplan_near <distance>: moves each hostile ship that falls back to a station or donor, or flees to a station, to this distance from its target's hull.
        Autotest.Commands["aiplan_near"] = arg =>
        {
            float d = float.Parse(arg, CultureInfo.InvariantCulture);
            foreach (var ship in Hostile())
            {
                var at = ship.AI?.GetStrategyModule<FallBackStrategy>()?.TargetCenter(ship) ?? ship.AI?.GetStrategyModule<Ai.FleeStrategy>()?.StationCenter(ship);
                if (at is not { } target) continue;
                Autotest.Teleport(ship, Autotest.ClearSpotNear(ship.Sim!, target.Center, target.Radius + d, ship.WorldBoundingRadius));
                Log.Info($"AUTOTEST {ship.Metadata.PlainShipName} moved {d:0} from its repair target");
            }
        };
    }

    static List<Ship> Hostile() => PersonalityAutotest.Hostile().OrderBy(s => s.UniqueID).ToList();

    /// <summary>The live ships of the last aispawn, in spawn order. Index i of the commands counts in this list.</summary>
    static List<Ship> New()
    {
        var sim = Autotest.RequireRun().Game.Sim;
        return PersonalityAutotest.LastSpawned.Select(id => sim.Ships[id]).OfType<Ship>().Where(s => !s.IsJunk).ToList();
    }

    static IEnumerable<FallBackStrategy> Modules() => Hostile().Select(s => s.AI?.GetStrategyModule<FallBackStrategy>()).OfType<FallBackStrategy>();

    static Ship At(string index)
    {
        var all = New();
        int i = int.Parse(index, CultureInfo.InvariantCulture);
        return i >= 0 && i < all.Count ? all[i] : throw new InvalidOperationException($"no ship {i} of the last aispawn; {all.Count} live");
    }

    static (List<Ship> Ships, string Value) Target(string arg)
    {
        var parts = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new InvalidOperationException("needs: index|new value");
        return (parts[0] == "new" ? New() : [At(parts[0])], parts[1].Trim());
    }
}

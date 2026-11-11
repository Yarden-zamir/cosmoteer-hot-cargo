using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Logic;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Gui;
using HarmonyLib;

namespace HotCargo.Ux;

/// <summary>
/// V7. Vanilla colors a resource only below zero, and shows no resources in flight unless they are pinned.
/// Watched supplies are the resources that parts burn in operation: every resource that a ResourceConsumer component consumes
/// on a part without a "provides_*" type category (factories have one), toggled component sets included.
/// In 0.30.5a this gives battery, bullet, hyperium (FTL drives), mine_part and the four missile parts.
/// Carbon is out: only the diamond factory consumes it.
/// Fractions are of typed capacity only, because cargo holds also count as capacity for any resource.
/// A watched supply shows orange below LowSupplyFraction and red below CriticalSupplyFraction in the resources box.
/// When one of your ships goes below the critical fraction, a yellow banner and a chat line name it, once until it recovers.
/// </summary>
static class LowSupplies
{
    static ShipRules? _rulesFor;
    static HashSet<ID<ResourceRules>> _watched = [];

    /// <summary>Supply state per ship and resource: true after an alert, until the supply is above the low fraction again.</summary>
    static readonly Dictionary<(Ship Ship, ID<ResourceRules> Resource), bool> Alerted = [];
    static SimRoot? _sim;
    static double _nextPoll;

    /// <summary>Alerts since start. For autotests.</summary>
    internal static int AlertsShown;

    public static IReadOnlySet<ID<ResourceRules>> Watched(ShipRules rules)
    {
        if (rules == _rulesFor) return _watched;
        var set = new HashSet<ID<ResourceRules>>();
        foreach (var part in rules.Parts)
        {
            if (part.TypeCategories?.Any(c => c.ToString().StartsWith("provides_", StringComparison.Ordinal)) == true) continue;
            AddConsumed(part.Components, set);
        }
        // The Tech Vault consumes stolen tech, but tech is cargo with its own UI (heat panel, loss rules), not a supply.
        set.Remove(Tech.Vaults.TechId);
        _rulesFor = rules;
        _watched = set;
        Log.Info($"ux: watched supplies: {string.Join(", ", set.Select(id => id.ToString()).Order())}");
        return set;
    }

    /// <summary>Consumers inside toggled component sets count too (the missile launcher keeps one set per missile type).</summary>
    static void AddConsumed(IEnumerable<PartComponentRules> components, HashSet<ID<ResourceRules>> set)
    {
        foreach (var c in components)
        {
            if (c is ResourceConsumerRules consumer) set.Add(consumer.ResourceType);
            else if (c is PartToggledComponentsRules toggled) AddConsumed(toggled.Components, set);
        }
    }

    static float? Fraction(Ship ship, ID<ResourceRules> id)
    {
        int cap = ship.Resources.GetCapacityTotal(id, includeImplicit: true, includeUntyped: false);
        if (cap <= 0) return null;
        return (float)ship.Resources.GetResourceTotal(id) / cap;
    }

    internal static void OnFrame(GameRoot game)
    {
        if (game.Sim is not { } sim || UxKit.Switches is not { LowSupplyWarnings: true } s) return;
        double now = App.Clock.Time.Seconds;
        if (now < _nextPoll) return;
        _nextPoll = now + 1.0;
        Poll(game, sim, s);
    }

    /// <summary>One check of every local ship. Called once a second, and directly by the autotest.</summary>
    internal static void Poll(GameRoot game, SimRoot sim, UxTunables s)
    {
        // A new sim (jump, load) records the current state without alerts, so a supply that is already low does not alert on arrival.
        bool fresh = sim != _sim;
        if (fresh) { Alerted.Clear(); _sim = sim; }
        foreach (var ship in sim.Ships.LocalPlayerShips)
        {
            foreach (var id in Watched(ship.Rules))
            {
                if (Fraction(ship, id) is not { } f) continue;
                var key = (ship, id);
                bool alerted = Alerted.TryGetValue(key, out bool a) && a;
                if (f > s.LowSupplyFraction) { Alerted[key] = false; continue; }
                if (alerted || f >= s.CriticalSupplyFraction) { Alerted.TryAdd(key, false); continue; }
                Alerted[key] = true;
                if (fresh) continue;
                Alert(game, ship, id, f);
            }
        }
        foreach (var gone in Alerted.Keys.Where(k => k.Ship.IsDisposed).ToList()) Alerted.Remove(gone);
    }

    static void Alert(GameRoot game, Ship ship, ID<ResourceRules> id, float f)
    {
        AlertsShown++;
        var rr = GameApp.Rules.GetResourceRules(id);
        string text = UxKit.Format("SupplyLowFmt", ship.Metadata.PlainShipName, (rr.PluralNameKey ?? rr.NameKey).Text, f * 100f);
        UxKit.ChatLine(game, $"<limited_color>{Halfling.StringTools.EscapeForXml(text)}</limited_color>");
        ShipAlerts.Banner(game, text, WidgetRules.Instance.LimitedTextColor);
        game.Gui.PlayerResourcesDisplay.FlashResource(id, 6, 0.25f);
        Log.Info($"ux: low supply {ship.Metadata.PlainShipName} {id} {f:P0}");
    }

    /// <summary>Colors a watched supply in the resources box by the typed capacity of the selected ships. The color is set again each frame.</summary>
    [HarmonyPatch(typeof(PlayerResourcesDisplay), nameof(PlayerResourcesDisplay.UpdateResource))]
    static class DisplayColor
    {
        static void Postfix(PlayerResourcesDisplay __instance, int quantity, Label quantityLbl, ResourceRules rr)
        {
            if (quantity < 0 || UxKit.Switches is not { LowSupplyWarnings: true } s) return;
            long cap = 0;
            bool watched = false;
            foreach (var ship in __instance.Sim.PlayerInput.SelectedCommandableShips)
            {
                if (!Watched(ship.Rules).Contains(rr.ID)) continue;
                watched = true;
                cap += ship.Resources.GetCapacityTotal(rr.ID, includeImplicit: true, includeUntyped: false);
            }
            if (!watched || cap <= 0) return;
            float f = (float)quantity / cap;
            if (f < s.CriticalSupplyFraction) quantityLbl.TextRenderer.Color = WidgetRules.Instance.BadTextColor;
            else if (f < s.LowSupplyFraction) quantityLbl.TextRenderer.Color = WidgetRules.Instance.LimitedTextColor;
        }
    }
}

using System.Globalization;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Factions;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling;
using Halfling.Random;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Hunters;
using HotCargo.Ui;

namespace HotCargo.Diplomacy;

/// <summary>
/// Autotest commands and fields of the Diplomacy area. Targets: hunter (first hunter by ID), hunterN, friend or enemy (the last ship
/// that dip_spawn made), fresh (the first hunter that got no offer yet). Commands that change the sim run with "mp" in co-op tests; "dip" sends the real MP input like a click.
/// </summary>
static class DiplomacyAutotest
{
    static ObjectID? _friend, _enemy;
    static string _pageText = "";
    static int _pageButtons, _pageActive, _pageSlider;

    static Run R() => Autotest.RequireRun();
    static SimRoot Sim() => R().Game.Sim;
    static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
    static float Float(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    internal static Ship Target(string name)
    {
        var sim = Sim();
        if (name == "fresh")
        {
            var used = R().State.Diplomacy.Used;
            return HunterSpawner.Hunters(sim).OrderBy(h => h.UniqueID)
                .FirstOrDefault(h => !Enum.GetValues<Offer>().Any(o => used.Contains(DiplomacyState.Key(h.UniqueID, o))))
                ?? throw new InvalidOperationException("no hunter without offers");
        }
        if (name.StartsWith("hunter", StringComparison.Ordinal))
        {
            int i = name.Length > 6 ? Int(name[6..]) : 0;
            return HunterSpawner.Hunters(sim).OrderBy(h => h.UniqueID).ElementAtOrDefault(i) ?? throw new InvalidOperationException($"no hunter {i}");
        }
        ObjectID? id = name switch { "friend" => _friend, "enemy" => _enemy, _ => throw new InvalidOperationException($"unknown target '{name}'") };
        return (id is { } x ? sim.Ships[x] : null) ?? throw new InvalidOperationException($"no live {name} ship");
    }

    static Ship Me() => Autotest.ActingShip(Sim()) ?? throw new InvalidOperationException("no player ship");

    internal static void Register()
    {
        // dip <offer> <target> [amount]: sends the choice as the local player, through the shared choice input like a click.
        Autotest.Commands["dip"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length is < 2 or > 3) throw new InvalidOperationException("dip needs: offer target [amount]");
            var offer = Enum.Parse<Offer>(p[0], ignoreCase: true);
            var target = Target(p[1]);
            R().Game.NetManager.EnqueueInput(DiplomacyChoiceDomain.Pick(target, offer, p.Length == 3 ? Int(p[2]) : 0));
        };
        Autotest.Commands["dip_set"] = arg => Autotest.SetField(R().T.Diplomacy, arg);
        // dip_spawn friend|enemy|trader [distance]: a tier-1 combat ship (or a trade ship) near the acting ship, with chances off for hails.
        Autotest.Commands["dip_spawn"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            float distance = p.Length > 1 ? Float(p[1]) : 500f;
            var ship = Spawn(p[0], distance);
            if (p[0] == "enemy") _enemy = ship.UniqueID; else _friend = ship.UniqueID;
        };
        // dip_expect <offer> <target> <amount> <op> <value>: checks the chance that the dialogue shows.
        Autotest.Commands["dip_expect"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 5) throw new InvalidOperationException("dip_expect needs: offer target amount op value");
            float chance = Chance(Enum.Parse<Offer>(p[0], ignoreCase: true), Target(p[1]), Int(p[2]));
            double expected = double.Parse(p[4], CultureInfo.InvariantCulture);
            bool ok = p[3] switch
            {
                "==" => Math.Abs(chance - expected) < 0.005, ">=" => chance >= expected, "<=" => chance <= expected,
                ">" => chance > expected, "<" => chance < expected, _ => throw new InvalidOperationException($"unknown op {p[3]}"),
            };
            Autotest.Check(ok, $"chance {p[0]} {p[1]} {p[2]} {p[3]} {p[4]} (actual {chance:0.000})");
        };
        // dip_page <target> [topic]: builds the dialogue page as the local player sees it and logs it.
        Autotest.Commands["dip_page"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var target = Target(p[0]);
            if (p.Length > 1) DiplomacyHail.SetTopic(target, p[1]);
            var page = target.AI?.GetDialogueExchange(Me()) as HailPage
                ?? throw new InvalidOperationException($"{target.Metadata.PlainShipName} has no hail page");
            _pageText = page.PrimaryDialogue;
            _pageButtons = page.Buttons.Count;
            _pageActive = page.Buttons.Count(b => b.SelfInputActive);
            _pageSlider = page.Slider is null ? 0 : 1;
            Log.Info($"AUTOTEST PAGE {target.Metadata.PlainShipName}: {_pageText.Replace('\n', ' ')} | slider={page.Slider?.Key} | " +
                     string.Join(" | ", page.Buttons.Select(b => (b.SelfInputActive ? "" : "(off) ") + b.Text)));
        };
        // dip_slider <target> <key> <value>: sets the slider value of a callee as if the player moved it.
        Autotest.Commands["dip_slider"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            CommsHail.SetSliderValue(Target(p[0]), p[1], Int(p[2]));
        };
        // dip_open <target> [topic]: opens the comms window to the target (for screenshots). dip_close closes every comms window.
        Autotest.Commands["dip_open"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var target = Target(p[0]);
            if (p.Length > 1) DiplomacyHail.SetTopic(target, p[1]);
            R().Game.Gui.FloatingWindows.AddChild(new CommWindow(R().Game, Me(), target));
        };
        Autotest.Commands["dip_close"] = _ =>
        {
            foreach (var w in R().Game.Gui.FloatingWindows.Children.OfType<CommWindow>().ToList()) w.SelfActive = false;
        };
        // dip_damage <target> <fraction>: every part of the target to this fraction of its health.
        Autotest.Commands["dip_damage"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var ship = Target(p[0]);
            foreach (var part in ship.Parts.ToList()) part.Health = Math.Max(1, (int)(part.Rules.MaxHealth * Float(p[1])));
        };
        // dip_fillfuel: fills every human storage that takes FTL fuel, so surrender fuel must drop into space.
        Autotest.Commands["dip_fillfuel"] = _ =>
        {
            int added = 0;
            foreach (var s in Tech.Vaults.HumanShips(Sim()).ToList()) added += s.Resources.AddResources(GameApp.Rules.FtlFuelResource, 100000, excludeConstructionSinks: true);
            Log.Info($"AUTOTEST filled {added} fuel");
        };
        // dip_dropfuel <n>: removes FTL fuel from the human ships, so loot has room.
        Autotest.Commands["dip_dropfuel"] = arg =>
        {
            int left = Int(arg);
            foreach (var s in Tech.Vaults.HumanShips(Sim()).OrderBy(s => s.UniqueID).ToList()) left -= s.Resources.RemoveResources(GameApp.Rules.FtlFuelResource, left);
            Log.Info($"AUTOTEST removed {Int(arg) - left} fuel");
        };
        // dip_near <target> [distance]: moves the target next to the acting ship, inside comms and sight range. "hunters" moves every hunter.
        Autotest.Commands["dip_near"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            float distance = p.Length > 1 ? Float(p[1]) : 250f;
            var ships = p[0] == "hunters" ? HunterSpawner.Hunters(Sim()).OrderBy(h => h.UniqueID).ToList() : [Target(p[0])];
            foreach (var ship in ships)
                Autotest.Teleport(ship, Autotest.ClearSpotNear(Sim(), Me().DetWorldCenter, distance, ship.WorldBoundingRadius));
        };
        // dip_mark: remembers the fuel of the human fleet and the acting player's money, for dip_fuelgain and dip_moneyspent.
        Autotest.Commands["dip_mark"] = _ => { _fuelMark = HumanFuel(); _moneyMark = Money(); };
        Autotest.Commands["dip_log"] = arg =>
        {
            var s = R().State.Diplomacy;
            Log.Info($"AUTOTEST DIP {arg}: used={s.Used.Count} held=[{string.Join(",", s.Held.Select(h => $"{h.ShipName}:{h.Units}:p{h.Player}"))}] " +
                     $"hires=[{string.Join(",", s.Hires.Select(h => $"{h.Ship}:{h.Until:0}:{(h.Merc ? "merc" : "hire")}{(h.Fleeing ? ":flee" : "")}"))}] " +
                     $"mercs={s.Mercs.Count} leaving={s.Leaving.Count} asks={s.FeeAsks.Count} tech={Situation.VaultTech(Sim())} fleet={R().State.FleetTech}");
        };

        Autotest.Fields["dip_used"] = () => R().State.Diplomacy.Used.Count;
        Autotest.Fields["dip_held"] = () => R().State.Diplomacy.Held.Sum(h => h.Units);
        Autotest.Fields["dip_asks"] = () => R().State.Diplomacy.FeeAsks.Count;
        Autotest.Fields["dip_hires"] = () => R().State.Diplomacy.Hires.Count;
        Autotest.Fields["dip_friendhired"] = () => _friend is { } f && R().State.Diplomacy.Hires.Any(h => h.Ship == f) ? 1 : 0;
        Autotest.Fields["dip_hirefleeing"] = () => R().State.Diplomacy.Hires.Count(h => h.Fleeing);
        Autotest.Fields["dip_mercs"] = () => R().State.Diplomacy.Mercs.Count;
        Autotest.Fields["dip_friendmerc"] = () => _friend is { } f && R().State.Diplomacy.Mercs.Contains(f) ? 1 : 0;
        Autotest.Fields["dip_yieldoffers"] = () => R().State.Diplomacy.Rolled.Count(k => k.EndsWith(":yieldok", StringComparison.Ordinal));
        Autotest.Fields["dip_leaving"] = () => R().State.Diplomacy.Leaving.Count;
        Autotest.Fields["dip_vaulttech"] = () => Situation.VaultTech(Sim());
        Autotest.Fields["dip_results"] = () => R().State.Diplomacy.Serial;
        Autotest.Fields["dip_hailing"] = () => Situation.Career(Sim()).ShipsHailingPlayer.Values.Sum(l => l.Count);
        Autotest.Fields["dip_surrendered"] = () => Situation.Career(Sim()).SurrenderedAIs.Count;
        Autotest.Fields["dip_fuelgain"] = () => HumanFuel() - _fuelMark;
        Autotest.Fields["dip_moneyspent"] = () => _moneyMark - Money();
        Autotest.Fields["dip_enemyhostile"] = () => _enemy is { } e && Sim().Ships[e] is { } s && Me().IsEnemiesWith(s) ? 1 : 0;
        Autotest.Fields["dip_technuggets"] = () => Sim().Nuggets.Where(n => n.ResourceType == Tech.Vaults.TechId).Sum(n => n.Quantity);
        Autotest.Fields["dip_friendlive"] = () => _friend is { } f && Sim().Ships[f] is { } s && s.ExistsInSim(Sim()) ? 1 : 0;
        Autotest.Fields["dip_friendattacking"] = () =>
            _friend is { } f && Sim().Ships[f]?.Commands.CurConfirmedCommand is Cosmoteer.Ships.Commands.AttackCommand ? 1 : 0;
        Autotest.Fields["dip_pagebuttons"] = () => _pageButtons;
        Autotest.Fields["dip_pageactive"] = () => _pageActive;
        Autotest.Fields["dip_pageslider"] = () => _pageSlider;
        Autotest.Fields["dip_answers"] = () => _friend is { } f && Sim().Ships[f] is { } s && Situation.Career(Sim()).WillShipAnswerCall(Me(), s) ? 1 : 0;
    }

    static int _fuelMark, _moneyMark;

    static int Money() => R().Game.GetPlayerState(Autotest.ActingPlayerIndex, deterministic: true).Money;

    static int HumanFuel() => Tech.Vaults.HumanShips(Sim()).Sum(s => s.Resources.GetResourceTotal(GameApp.Rules.FtlFuelResource, includeStored: true, includeCarried: false));

    static float Chance(Offer offer, Ship target, int amount)
    {
        var run = R();
        var sim = Sim();
        int player = Autotest.ActingPlayerIndex;
        return offer switch
        {
            Offer.HandOver => Quotes.HandOver(run, sim, player, target, amount),
            Offer.Bluff => Quotes.Bluff(run, sim, player, amount),
            Offer.Deal => Quotes.Deal(run, sim, target, amount),
            Offer.DemandSurrender => Quotes.DemandSurrender(run, sim, player, target),
            Offer.OfferSurrender => Quotes.OfferSurrender(run, sim, player, target),
            Offer.Hold => Quotes.Hold(run, sim, target, amount),
            Offer.Hire => Quotes.Hire(run, sim, player, target, amount * Quotes.HireStep(run)),
            _ => throw new InvalidOperationException($"no chance for {offer}"),
        };
    }

    /// <summary>
    /// Spawns a tier-1 ship near the acting ship: "friend" a combat ship of the system's faction military, "trader" a faction trade
    /// ship (civilian), "enemy" a combat ship on a new hostile side. Deterministic: the seed comes from the sim tick.
    /// </summary>
    static Ship Spawn(string kind, float distance)
    {
        var sim = Sim();
        var me = Me();
        var faction = sim.MapNode?.ControllingFaction is { } fid ? GameApp.Rules.Factions.First(f => f.ID == fid) : GameApp.Rules.Factions.First();
        var rand = new FastRand((ulong)(uint)sim.Tick, 0x4449_5053UL);
        var tags = new[] { new ID<BuiltinShipTag>(kind == "trader" ? "civilian" : "combat") };
        var built = GameApp.Rules.BuiltinShips.GetRandomMatchingShip(rand, [faction.ID], new Range<int>(1, 3), null, tags)
            ?? GameApp.Rules.BuiltinShips.GetRandomMatchingShip(rand, null, new Range<int>(1, 3), null, tags)
            ?? throw new InvalidOperationException($"no built-in {tags[0]} ship of tier 1 to 3");
        var loc = Autotest.ClearSpotNear(sim, me.DetWorldCenter, distance, 120f);
        int allegiance = kind switch
        {
            "enemy" => Situation.Career(sim).AllocUniquePlayerIndex(),
            "trader" => faction.CivilianPlayerIndex,
            _ => faction.MilitaryPlayerIndex,
        };
        var spawner = new StasisShipSpawner(sim, built.ID, loc, SpawnedShipFeatures.None, rand.UInt64(), [])
        {
            AI = new ID<ShipAIRules>(kind == "trader" ? "TradeShip" : "Normal"),
            Direction = loc.DirectionTo(me.DetWorldCenter),
            ObjectAllegiance = allegiance,
        };
        spawner.Spawn(sim);
        var ship = sim.Ships[spawner.ReservedObjectID] ?? throw new InvalidOperationException($"{built.ID} did not spawn");
        Log.Info($"AUTOTEST spawned {kind} {ship.Metadata.PlainShipName} id={ship.UniqueID} player={allegiance} at {loc.DistanceTo(me.DetWorldCenter):0}");
        return ship;
    }
}

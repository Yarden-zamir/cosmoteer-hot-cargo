using System.Globalization;
using Cosmoteer;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Ships.Resources;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Tech;

/// <summary>Autotest commands and fields of the Tech area. The game runs them from the frame loop, in single player only.</summary>
static class TechAutotest
{
    internal static void Register()
    {
        Autotest.Commands["tech"] = Tech;
        Autotest.Commands["vault"] = Vault;
        Autotest.Commands["tostation"] = _ => ToStation();
        // tech_set Field value: changes a Tech tunable of the run. In co-op run it through "mp".
        Autotest.Commands["tech_set"] = arg => Autotest.SetField(Active().T.Tech, arg);
        Autotest.Fields["losstimer"] = () => LossTimer.IsRunning(Active()) ? 1 : 0;
        Autotest.Fields["losstimerleft"] = () => LossTimer.SecondsLeft(Active()) ?? -1;
        // Spec 444: tech crate indicators in the scene, and the TECH count on the player ship's blip.
        Autotest.Fields["techcrates"] = () => ObjectIndicatorSource.GetAllInScene(Active().Game.Sim).OfType<TechMarkers.CrateIndicator>().Count();
        // Shown crate markers (one per group of crates) and the units they name.
        Autotest.Fields["cratemarkers"] = () => CrateLeaders().Count;
        Autotest.Fields["crateunits"] = () => CrateLeaders().Sum(c => c.Units);
        Autotest.Fields["shiptag"] = () => PlayerShip().IndicatorSource.GetNonMouseoverText()?.Contains($"TECH {TechMarkers.ShipUnits(PlayerShip())}") == true ? TechMarkers.ShipUnits(PlayerShip()) : 0;
        Autotest.Fields["techvalue"] = () => { var r = Active(); return r.TechValue(); };
        Autotest.Fields["sellprice"] = () => Active().Game.Sim.Mode.GetResourceSellRefund(Vaults.TechId, null);
        Autotest.Fields["buyprice"] = () => Active().Game.Sim.Mode.GetResourceBuyCost(Vaults.TechId, null);
        Autotest.Fields["money"] = Money;
        Autotest.Fields["moneygain"] = () => Money() - _moneyAtSell;
        // Must stay 0: the price a station pays equals the tech value (spec 19, 20).
        Autotest.Fields["pricegap"] = () => Active().Game.Sim.Mode.GetResourceSellRefund(Vaults.TechId, null) - Autotest.Fields["techvalue"]();
        Autotest.Fields["vaults"] = () => Vaults.All(Active().Game.Sim).Count;
        Autotest.Fields["vaulttech"] = () => Vaults.All(Active().Game.Sim).Sum(g => g.Resources);
        // Vaults on human ships that crew can walk to from crew quarters. Must equal vaults (spec 441).
        Autotest.Fields["vaultpaths"] = () => Vaults.HumanShips(Active().Game.Sim).Sum(s =>
        {
            var finder = new Cosmoteer.Ships.Blueprints.BlueprintPathfinder(s);
            return Vaults.VaultParts(s).Count(p => StartingVault.HasCrewPath(finder, new PartInfo(p.Rules, p.Location, p.Rotation, p.FlipX)));
        });
        // Parts on human ships that need door access (the build tool's check) and that crew cannot walk to. The starting vaults must not add any.
        // Crew quarters are skipped: the path search starts at the part and looks for another crew source, so a ship's only quarters always fails.
        Autotest.Fields["noaccess"] = () => Vaults.HumanShips(Active().Game.Sim).Sum(s =>
        {
            var finder = new Cosmoteer.Ships.Blueprints.BlueprintPathfinder(s);
            var cut = s.BlueprintParts.Where(p => p.Rules.HasDoorLocations && !p.Rules.NoAutoDoors && p.Rules.DoorAccessWarningLevel != AccessWarningLevel.None && p.Rules.CrewCapacity == 0
                && !StartingVault.HasCrewPath(finder, new PartInfo(p.Rules, p.Location, p.Rotation, p.FlipX))).ToList();
            foreach (var p in cut) Log.Info($"AUTOTEST no crew path to {p.Rules.ID} at {p.Location} on {s.Name}");
            return cut.Count;
        });
        Autotest.Fields["outsidetech"] = () => OutsideVault()?.Resources ?? throw new InvalidOperationException("no vault from vault outside");
        // Vault parts on human ships that are done building.
        Autotest.Fields["vaultsbuilt"] = () => Vaults.HumanShips(Active().Game.Sim).Sum(s => Vaults.VaultParts(s).Count(p => !p.IsUnderConstruction));
        // Health lost by vault parts, and by all other parts, of the player ship (spec 448).
        Autotest.Fields["vaultdamage"] = () => PlayerShip().Parts.Where(p => p.Rules.ID == Vaults.VaultId).Sum(p => p.Rules.MaxHealth - p.Health);
        Autotest.Fields["partdamage"] = () => PlayerShip().Parts.Where(p => p.Rules.ID != Vaults.VaultId).Sum(p => p.Rules.MaxHealth - p.Health);
        Autotest.Fields["vaultburned"] = () => VaultDropPatch.LostMessages;
        Autotest.Fields["wreckspills"] = () => HunterGrab.WreckSpills;
        Autotest.Fields["hunterheld"] = () => Active().State.Tech.HunterHeld.Sum(h => h.Units);
        // The units on the blip of the first hunter that holds tech, or 0 when the blip does not show them.
        Autotest.Fields["holdertag"] = () =>
        {
            var held = Active().State.Tech.HunterHeld.FirstOrDefault();
            if (held is null || Active().Game.Sim.Ships[held.Ship] is not { } ship) return 0;
            return ship.IndicatorSource.GetNonMouseoverText()?.Contains($"HAS {held.Units} TECH") == true ? held.Units : 0;
        };
        Autotest.Fields["shipvalid"] = () => Vaults.HumanShips(Active().Game.Sim).All(s => s.BlueprintParts.IsValid && s.PhysicalMatchesBlueprints) ? 1 : 0;
        Autotest.Fields["livetech"] = () => Vaults.CountFleetTech(Active().Game.Sim);
        Autotest.Fields["heatmult"] = () => Active().HeatMultiplier(Active().State.FleetTech);
        // Normal storage tiles on human ships that would accept tech. Must stay 0 (spec 14).
        Autotest.Fields["flexsinks"] = () => Vaults.HumanShips(Active().Game.Sim).Sum(s => FlexTilesTakingTech(s));
        Autotest.Fields["stationsinks"] = () => FlexTilesTakingTech(NearestStation());
        Autotest.Fields["stationtech"] = () => NearestStation().Resources.GetResourceTotal(Vaults.TechId);
    }

    static Run Active() => Autotest.RequireRun();

    static List<TechMarkers.CrateIndicator> CrateLeaders() =>
        [.. ObjectIndicatorSource.GetAllInScene(Active().Game.Sim).OfType<TechMarkers.CrateIndicator>().Where(c => c.ShouldShowA(out _, out _) == true)];

    static Halfling.Geometry.IntVector2? _outside;

    static TypedResourceGrid? OutsideVault() => _outside is { } at ? Vaults.OnShip(PlayerShip()).FirstOrDefault(g => g.Part!.Location == at) : null;

    static double _moneyAtSell;
    static bool _tradeLogged;

    static double Money() => Active().Game.GetPlayerState(Active().Game.LocalPlayerIndex, deterministic: true).Money;

    static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>tech add N | tech remove N | tech sell N | tech dump | tech drop N (takes N from the vaults and drops them as crates next to the player ship) | tech collect (crew fetch every tech crate) | tech drophunter N (crates near the first hunter) | tech wreckholder (the first hunter with tech becomes a wreck)</summary>
    static void Tech(string arg)
    {
        var run = Active();
        var sim = run.Game.Sim;
        var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts)
        {
            case ["add", var n]: Log.Info($"AUTOTEST tech added {Vaults.Add(sim, Int(n))}"); break;
            case ["remove", var n]: Log.Info($"AUTOTEST tech removed {Vaults.Remove(sim, Int(n))}"); break;
            case ["sell", var n]: Sell(run, Int(n)); break;
            case ["dump"]: Dump(run); break;
            case ["collect"]:
                // The input of the vanilla collect tool (SalvageCollectState): crew fetch each crate into the best vault with room.
                var collector = PlayerShip();
                int queued = 0;
                for (int i = 0; i < sim.Nuggets.Count; i++)
                {
                    var nugget = sim.Nuggets[i];
                    // The vanilla tool skips crates beyond the crew's safe distance from the ship.
                    if (nugget.ResourceType != Vaults.TechId || collector.WorldBoundingCircle.DistanceTo(nugget.WorldLocation) >= GameApp.Rules.Crew.LoseHomeShipWarningDistance) continue;
                    // The vaults are full (pending pickups count): the rest of the crates stay, as with the vanilla tool.
                    if (collector.Resources.GetBestReceivingSink(nugget, nugget.Quantity, MPValueType.Displayed, out int room) is not { } sink) break;
                    queued++;
                    run.Game.NetManager.EnqueueInput(new MPMoveResourcesInput(nugget, sink, Math.Min(room, nugget.Quantity), collector.Jobs.AllocLocalJobID()));
                }
                if (queued == 0) throw new InvalidOperationException("no tech crate within crew range, or no vault room");
                Log.Info($"AUTOTEST tech collect queued {queued} crates");
                break;
            case ["drophunter", var n]:
                // Crates 600 from the first hunter, between it and the player ship.
                var hunter = Hunters.HunterSpawner.Hunters(sim).FirstOrDefault() ?? throw new InvalidOperationException("no hunter");
                var toward = (PlayerShip().DetWorldCenter - hunter.DetWorldCenter).Normalize();
                int taken = Vaults.Remove(sim, Int(n));
                HunterGrab.Drop(sim, hunter.DetWorldCenter + toward * 600f, taken);
                Log.Info($"AUTOTEST tech dropped {taken} crates near {hunter.Metadata.PlainShipName}");
                break;
            case ["wreckholder"]:
                var held = run.State.Tech.HunterHeld.FirstOrDefault() ?? throw new InvalidOperationException("no hunter holds tech");
                var holder = sim.Ships[held.Ship] ?? throw new InvalidOperationException("the holder is not live");
                sim.Mode.SetReducedShipAllegiance(holder, Cosmoteer.PlayerIndexes.Junk, -1);
                Log.Info($"AUTOTEST wrecked {holder.Metadata.PlainShipName}");
                break;
            case ["drop", var n]:
                int removed = Vaults.Remove(sim, Int(n));
                var ship = PlayerShip();
                var at = ship.WorldCenter + new Halfling.Geometry.Vector2(ship.WorldBoundingRadius + 150f, 0f);
                sim.Nuggets.DropNuggets(Vaults.TechId, removed, new Halfling.Geometry.RotRect(at, new Halfling.Geometry.Vector2(20f, 20f), default));
                Log.Info($"AUTOTEST tech dropped {removed} crates at {at}");
                break;
            default: throw new InvalidOperationException($"tech: unknown arguments '{arg}'");
        }
    }

    /// <summary>
    /// vault damage F (fraction of max health) | vault destroy | vault repair: acts on the first vault that holds tech (else the first vault),
    /// or on every vault with "all" at the end.
    /// vault outside: builds one more vault outside the hull of the player ship, where crew can walk to it.
    /// vault move: moves the tech of the first full vault into the vault that "vault outside" built, with the input of the vanilla move resources tool.
    /// vault steel N: adds steel to the player ship, for a vault build.
    /// vault focus [wide]: points the camera at the player ship and zooms in until the ship fills the view (wide: with the dropped crates).
    /// </summary>
    static void Vault(string arg)
    {
        var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts is ["outside"])
        {
            var ship = PlayerShip();
            var at = StartingVault.PlaceOutside(ship, ship.Rules.PartsByID[Vaults.VaultId], requireCrewPath: true) ?? throw new InvalidOperationException("no outside spot with a crew path");
            _outside = at;
            Log.Info($"AUTOTEST vault outside at {at}");
            return;
        }
        if (parts is ["steel", var n])
        {
            // Steel into the player ship's storage, like steel that a player bought at a station.
            var ship = PlayerShip();
            int added = ship.Resources.AddResources(new Cosmoteer.Data.ID<Cosmoteer.Resources.ResourceRules>("steel"), Int(n), excludeConstructionSinks: true);
            Log.Info($"AUTOTEST vault steel: added {added} steel");
            return;
        }
        if (parts is ["build"])
        {
            // A player's blueprint: the game's construction mode decides how it gets built (crew and steel in a normal career).
            var ship = PlayerShip();
            var mode = ship.Sim.Mode.GetConstructionMode(ship);
            var at = StartingVault.PlaceOutside(ship, ship.Rules.PartsByID[Vaults.VaultId], requireCrewPath: true, mode) ?? throw new InvalidOperationException("no outside spot with a crew path");
            _outside = at;
            Log.Info($"AUTOTEST vault build at {at}, construction mode {mode}");
            return;
        }
        if (parts is ["focus"] or ["focus", "wide"])
        {
            // "wide" also fits the crates that "tech drop" puts 150 beyond the hull.
            var ship = PlayerShip();
            ship.FocusCamera(playSound: false);
            float size = parts.Length == 2 ? (ship.WorldBoundingRadius + 200f) * 2.2f : ship.WorldBoundingRadius * 2.4f;
            ship.Sim.Camera.ViewController.ZoomToFit(new Halfling.Geometry.Vector2(size));
            return;
        }
        if (parts is ["move"])
        {
            var game = Active().Game;
            var ship = PlayerShip();
            var to = OutsideVault() ?? throw new InvalidOperationException("vault move needs vault outside first");
            var from = Vaults.OnShip(ship).FirstOrDefault(g => g != to && g.Resources == g.MaxResources) ?? throw new InvalidOperationException("no full vault");
            TypedResourceGrid.ResourceTile source = from.First(), sink = to.First();
            var id = MPMoveResourcesInput.GetJobOwner(source, sink, game.LocalPlayerIndex).Jobs.AllocLocalJobID();
            game.NetManager.EnqueueInput(new MPMoveResourcesInput(source, sink, from.Resources, id, sticky: true));
            Log.Info($"AUTOTEST vault move {from.Resources} from {from.Part!.Location} to {to.Part!.Location}");
            return;
        }
        bool all = parts is [.., "all"];
        var grids = Vaults.All(Active().Game.Sim);
        if (grids.Count == 0) throw new InvalidOperationException("no vault");
        var first = grids.FirstOrDefault(g => g.Resources > 0) ?? grids[0];
        foreach (var grid in all ? grids : [first])
        {
            Part part = grid.Part ?? throw new InvalidOperationException("vault grid has no part");
            switch (all ? parts[..^1] : parts)
            {
                case ["damage", var f]:
                    part.Health -= (int)(float.Parse(f, CultureInfo.InvariantCulture) * part.Rules.MaxHealth);
                    break;
                case ["destroy"]: part.Health = 0; break;
                case ["repair"]: part.Health = part.Rules.MaxHealth; break;
                default: throw new InvalidOperationException($"vault: unknown arguments '{arg}'");
            }
            Log.Info($"AUTOTEST vault {arg} at {part.Location}: health {part.Health}/{part.Rules.MaxHealth}, tech in it {grid.Resources}");
        }
    }

    /// <summary>The first ship of the player that runs the command (see Autotest.ActingShip). In co-op each player commands only own ships.</summary>
    internal static Ship PlayerShip() => Autotest.ActingShip(Active().Game.Sim) ?? throw new InvalidOperationException("no player ship");

    /// <summary>The station that tostation picked.</summary>
    static Ship? _station;

    /// <summary>
    /// The station that tostation picked, else the trading ship nearest to the first human ship. Throws when the system has none.
    /// The pick stays fixed, because a trade ship that docks next to the player can come closer than the station center.
    /// </summary>
    internal static Ship NearestStation()
    {
        var ship = PlayerShip();
        if (_station is { IsDisposed: false } picked && picked.Sim == ship.Sim) return picked;
        return Active().Game.Sim.Ships.AllShips
            .Where(s => s != ship && s.AI?.WillTradeWith(ship) == true)
            .OrderBy(s => s.WorldCenter.DistanceSquaredTo(ship.WorldCenter))
            .FirstOrDefault() ?? throw new InvalidOperationException("no trading station in this system");
    }

    /// <summary>Moves the player ship next to the nearest trading station, so that crew can trade. Test only.</summary>
    static void ToStation()
    {
        var ship = PlayerShip();
        _station = null;
        var station = _station = NearestStation();
        // In co-op each player's ship gets its own spot on the same side, so two ships never land on each other.
        float side = Autotest.ActingPlayerIndex * (2f * ship.WorldBoundingRadius + 50f);
        Autotest.Teleport(ship, station.WorldCenter + new Halfling.Geometry.Vector2(station.WorldBoundingRadius + ship.WorldBoundingRadius + 2f, side));
        Log.Info($"AUTOTEST moved {ship.Name} next to {station.Name}; trade allowed={Active().Game.Sim.Mode.AllowResourceTrade(ship, station)}");
    }

    static int FlexTilesTakingTech(Ship ship) =>
        FlexResourceGrid.GetAllOnShip(ship).SelectMany(g => g).Count(t => ((IResourceSink)t).GetRemainingCapacity(Vaults.TechId, allowPreferredTypeMismatch: false) > 0);

    /// <summary>Sells tech to the nearest station with the same inputs as the trade window (CrewAndResourceTransferWindow.DoTransfer).</summary>
    static void Sell(Run run, int units)
    {
        var game = run.Game;
        var ship = PlayerShip();
        var station = NearestStation();
        _moneyAtSell = Money();
        if (!_tradeLogged)
        {
            _tradeLogged = true;
            game.EventHub.ResourceTradePickup += (_, e) => Log.Info($"AUTOTEST trade pickup {e.TradeType} {e.ResourceType} x{e.Amount} by crew of player {e.Crew.PlayerIndex}, fleet count {Vaults.CountFleetTech(game.Sim)}");
            game.EventHub.ResourceTradeDelivery += (_, e) => Log.Info($"AUTOTEST trade delivery {e.TradeType} {e.ResourceType} x{e.Amount}, money {Money()}");
        }
        var sources = new List<(IResourceSource Source, int AnticipatedResources, float Distance)>();
        ship.Resources.SearchAllAvailableResourceSources(station, Vaults.TechId, MPValueType.Displayed, sources);
        var sinks = new List<(IResourceSink Sink, int AnticipatedCapacity, float Distance)>();
        station.Resources.SearchAllAvailableResourceSinks(ship, Vaults.TechId, MPValueType.Displayed, sinks);
        Log.Info($"AUTOTEST sell: {sources.Count} sources, {sinks.Count} station sinks with room {sinks.Sum(s => s.AnticipatedCapacity)}");
        int left = units;
        foreach (var (source, available, _) in sources)
        {
            int fromSource = available;
            for (int i = 0; i < sinks.Count && left > 0 && fromSource > 0; i++)
            {
                var (sink, room, dist) = sinks[i];
                int n = Math.Min(left, Math.Min(fromSource, room));
                if (n <= 0) continue;
                int price = game.Sim.Mode.GetResourceSellRefund(Vaults.TechId, sink.Ship?.Metadata.PlayerIndex);
                var id = MPMoveResourcesInput.GetJobOwner(source, sink, game.LocalPlayerIndex).Jobs.AllocLocalJobID();
                game.NetManager.EnqueueInput(new MPMoveResourcesInput(source, sink, n, id, sticky: false, TradeType.Sell, price));
                sinks[i] = (sink, room - n, dist);
                left -= n;
                fromSource -= n;
            }
        }
        Log.Info($"AUTOTEST sell: queued {units - left} of {units}");
    }

    /// <summary>Logs the blueprint as a text map: one letter per part type (V = Tech Vault, upper case = walled), '.' = empty.</summary>
    static void LogMap(Ship ship)
    {
        var parts = ship.BlueprintParts;
        var cells = parts.SelectMany(p => p.Rect).ToHashSet();
        int minX = cells.Min(c => c.X), maxX = cells.Max(c => c.X), minY = cells.Min(c => c.Y), maxY = cells.Max(c => c.Y);
        var letters = new Dictionary<string, char>();
        char Letter(PartRules r)
        {
            string id = r.ID.ToString();
            if (!letters.TryGetValue(id, out char c))
                letters[id] = c = r.ID == Vaults.VaultId ? 'V' : (char)('a' + letters.Count(kv => kv.Value != 'V') % 26);
            return r.IsWalled ? char.ToUpperInvariant(c) : c;
        }
        for (int y = minY; y <= maxY; y++)
        {
            var row = new System.Text.StringBuilder();
            for (int x = minX; x <= maxX; x++)
                row.Append(parts[new Halfling.Geometry.IntVector2(x, y), Cosmoteer.Ships.Parts.PartRectType.Normal] is { } p ? Letter(p.Rules) : '.');
            Log.Info($"  map {y,3} {row}");
        }
        Log.Info($"  map x from {minX}; legend: {string.Join(", ", letters.Select(kv => $"{kv.Value}={kv.Key}"))}");
        Log.Info($"  doors: {string.Join(" ", ship.BlueprintDoors.Select(d => d.Location.ToString()))}");
    }

    static void Dump(Run run)
    {
        var sim = run.Game.Sim;
        foreach (var ship in Vaults.HumanShips(sim))
        {
            Log.Info($"AUTOTEST ship {ship.Name}: blueprint valid={ship.BlueprintParts.IsValid} matches={ship.PhysicalMatchesBlueprints}");
            foreach (var p in ship.BlueprintParts.OrderBy(p => p.Location.Y).ThenBy(p => p.Location.X))
                Log.Info($"  part {p.Rules.ID} at {p.Location} rect {p.Rect}");
            foreach (var d in ship.BlueprintDoors) Log.Info($"  door {d.Location}");
            var cm = ship.OptionalConstructionManager;
            Log.Info($"  steel {ship.Resources.GetResourceTotal(new Cosmoteer.Data.ID<Cosmoteer.Resources.ResourceRules>("steel"))}, queued builds {cm?.QueuedPartConstructions.Count}, blocked {cm?.BlockedPartConstructions.Count}, needs resources {cm?.HasSinksNeedingResources}");
            foreach (var v in Vaults.VaultParts(ship))
                Log.Info($"  vault part at {v.Location}: health {v.Health}, flags {v.Flags}, destroyed={v.IsDestroyed}, tech {Vaults.OnShip(ship).FirstOrDefault(g => g.Part == v)?.Resources}");
        }
        foreach (var starter in GameApp.Rules.CareerMode.StarterShips)
        {
            var s = Ship.Load(Ship.LoadBytes(starter.Ship.FilePath), starter.Ship.ShipName, blueprintsOnly: true);
            var storage = s.BlueprintParts.Where(p => p.Rules.ID.ToString().Contains("storage")).Select(p => $"{p.Rules.ID}@{p.Location}");
            Log.Info($"AUTOTEST starter {starter.Ship.ShipName}: {s.BlueprintParts.Count} parts; storage: {string.Join(", ", storage)}");
            LogMap(s);
        }
        foreach (var ship in Vaults.HumanShips(sim)) LogMap(ship);
        foreach (var s in sim.Ships.AllShips.Where(s => !sim.Mode.IsHumanPlayerShip(s)))
            Log.Info($"AUTOTEST other ship {s.Name} player={s.Metadata.PlayerIndex} trades={s.AI?.WillTradeWith(PlayerShip())} flexTilesForTech={FlexTilesTakingTech(s)}");
    }
}

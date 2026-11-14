using System.Globalization;
using Cosmoteer;
using Cosmoteer.Bullets;
using Cosmoteer.Bullets.Hits;
using Cosmoteer.Data;
using Cosmoteer.Nebulas;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.HitEffects;
using Halfling;
using Halfling.Geometry;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Hunters;
using HotCargo.Tactics;

namespace HotCargo.Ai;

/// <summary>Enemy AI behaviors (spec 193-202) and personalities (spec 360-369). TacticsArea.Register calls Register once at startup.</summary>
public static class AiArea
{
    // TacticsLog event names. Each one is an autotest field "ai_<name>" that counts the events.
    public const string Flee = "flee", FleeStay = "flee_stay", FleeEnd = "flee_end", FleeJump = "flee_jump";
    public const string CallStart = "call_start", CallComplete = "call_complete", CallInterrupt = "call_interrupt";
    public const string CallBlocked = "call_blocked", CallAssist = "call_assist", CallIntercept = "call_intercept";
    public const string Regroup = "regroup", RegroupEnd = "regroup_end";
    public const string PersonalityEvent = "personality";

    static readonly string[] EventNames =
        [Flee, FleeStay, FleeEnd, FleeJump, CallStart, CallComplete, CallInterrupt, CallBlocked, CallAssist, CallIntercept, Regroup, RegroupEnd, PersonalityEvent];

    public static void Register()
    {
        foreach (string name in EventNames) Autotest.Fields[$"ai_{name}"] = () => TacticsLog.Count(name);
        Autotest.Fields["ai_calling"] = () => Sim().Ships.Count(s => s.AI?.GetStrategyModule<CallStrategy>() is { Calling: true });
        // Spec 1300: the longest running call, in seconds (0 with no call).
        Autotest.Fields["ai_call_seconds"] = () => Sim().Ships.Select(s => s.AI?.GetStrategyModule<CallStrategy>()).OfType<CallStrategy>().Where(m => m.Calling).Select(m => (double)m.CallSeconds).DefaultIfEmpty(0).Max();
        PersonalityAutotest.Register();
        Autotest.Fields["ai_fleeing"] = () => Sim().Ships.Count(s => s.AI?.GetStrategyModule<FleeStrategy>() is { Fleeing: true });

        // aiset <TacticsTunables field> <value>: sets one tactics tunable for this run. Test only.
        Autotest.Commands["aiset"] = arg => Autotest.SetField(Autotest.RequireRun().T.Tactics, arg);
        // stageset <stage> <StageResponse field> <value>: sets one field of a hunter stage response for this run. Test only.
        Autotest.Commands["stageset"] = arg =>
        {
            int space = arg.IndexOf(' ');
            if (space < 0) throw new InvalidOperationException("stageset needs: stage field value");
            string stage = arg[..space];
            var response = Autotest.RequireRun().T.Hunters.Stages.FirstOrDefault(s => s.Name == stage)
                ?? throw new InvalidOperationException($"no hunter stage '{stage}'");
            Autotest.SetField(response, arg[(space + 1)..]);
        };
        // sectorindex <n>: sets the run's sector index, for the strike scaling checks. Test only.
        Autotest.Commands["sectorindex"] = arg => Autotest.RequireRun().State.SectorIndex = int.Parse(arg, CultureInfo.InvariantCulture);
        // spawnstage <stage>: starts the hunter response of one stage without heat. Test only.
        Autotest.Commands["spawnstage"] = arg =>
        {
            var run = Autotest.RequireRun();
            HunterSpawner.StartStage(run, run.Game.Sim, arg);
        };
        // disarmhunter <index>: destroys every weapon part of hunter <index> (by ID). Test only.
        Autotest.Commands["disarmhunter"] = arg =>
        {
            var ship = HunterAt(arg);
            foreach (var part in ship.Weapons.Select(w => w.Part).OfType<Cosmoteer.Ships.Parts.Part>().Distinct().ToList()) part.Health = 0;
            Log.Info($"AUTOTEST disarmed {ship.Metadata.PlainShipName}: offensive weapons left={AiGate.HasOffensiveWeapons(ship)}");
        };
        // Hyperium in loose nuggets in the system, and hunters with held fuel (spec 192).
        Autotest.Fields["fuelnuggets"] = () => Sim().Nuggets.Where(n => n.ResourceType == GameApp.Rules.FtlFuelResource).Sum(n => n.Quantity);
        Autotest.Fields["heldfuel"] = () => Autotest.RequireRun().State.Hunters.HeldFuel.Sum(h => h.Amount);
        // killhunter <index>|held: destroys every part of a hunter. "held" picks the first hunter with held fuel, else hunter 0. Test only.
        Autotest.Commands["killhunter"] = arg =>
        {
            var held = Autotest.RequireRun().State.Hunters.HeldFuel;
            var ship = arg == "held"
                ? HunterSpawner.Hunters(Sim()).FirstOrDefault(h => held.Any(f => f.Ship == h.UniqueID)) ?? HunterAt("0")
                : HunterAt(arg);
            Log.Info($"AUTOTEST killing {ship.Metadata.PlainShipName}");
            foreach (var part in ship.Parts.ToList()) part.Health = 0;
        };
        // damagehunter <index> <fraction>: sets every part of one hunter to this fraction of its health. Test only.
        Autotest.Commands["damagehunter"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new InvalidOperationException("damagehunter needs: index fraction");
            var ship = HunterAt(parts[0]);
            float fraction = float.Parse(parts[1], CultureInfo.InvariantCulture);
            foreach (var part in ship.Parts.ToList()) part.Health = Math.Max(1, (int)(part.Rules.MaxHealth * fraction));
        };
        // disrupt disruptor|emp: hits the first calling ship with a real disruptor bolt or EMP missile effect from the first human ship. Test only.
        Autotest.Commands["disrupt"] = arg =>
        {
            var sim = Sim();
            var target = sim.Ships.FirstOrDefault(s => s.AI?.GetStrategyModule<CallStrategy>() is { Calling: true })
                ?? throw new InvalidOperationException("no ship is calling");
            Hit(sim, target, arg switch
            {
                "disruptor" => ("cosmoteer.disruptor_bolt", typeof(BulletPenetratingHitRules)),
                "emp" => ("cosmoteer.missile_emp", typeof(BulletSimpleHitRules)),
                _ => throw new InvalidOperationException("disrupt needs: disruptor|emp"),
            });
        };
        // hunternear <index>|all <distance>: moves hunter <index>, or every hunter, to this distance from the first human ship. Test only.
        Autotest.Commands["hunternear"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new InvalidOperationException("hunternear needs: index|all distance");
            var player = Tech.TechAutotest.PlayerShip();
            float d = float.Parse(parts[1], CultureInfo.InvariantCulture);
            foreach (var ship in parts[0] == "all" ? HunterSpawner.Hunters(Sim()).OrderBy(h => h.UniqueID).ToList() : [HunterAt(parts[0])])
                Autotest.Teleport(ship, Autotest.ClearSpotNear(Sim(), player.WorldCenter, d, ship.WorldBoundingRadius));
        };
        // storm <index>|all: puts an ion storm around hunter <index>, or around every hunter. Test only.
        Autotest.Commands["storm"] = arg =>
        {
            var sim = Sim();
            var storm = new ID<NebulaTypeRules>("ion_storm");
            foreach (var ship in arg.Trim() == "all" ? HunterSpawner.Hunters(sim).ToList() : [HunterAt(arg)])
            {
                for (float x = -1500f; x <= 1500f; x += 250f)
                    for (float y = -1500f; y <= 1500f; y += 250f)
                        sim.Nebulas[sim.Nebulas.GetCoordUnderWorldPoint(ship.DetWorldCenter + new Vector2(x, y))] = storm;
                Log.Info($"AUTOTEST storm around {ship.Metadata.PlainShipName}: in storm={AiGate.InStorm(ship, Autotest.RequireRun().T.Tactics)}");
            }
        };
    }

    static SimRoot Sim() => Autotest.RequireRun().Game.Sim;

    /// <summary>
    /// Live hunter by index in ID order. A negative index counts from the end: -1 is the last hunter in ID order.
    /// Object IDs are random (ObjectID.NewRandom), so the order is stable but it is not the spawn order.
    /// </summary>
    static Ship HunterAt(string arg)
    {
        int index = int.Parse(arg, CultureInfo.InvariantCulture);
        var hunters = HunterSpawner.Hunters(Sim()).OrderBy(h => h.UniqueID).ToList();
        int i = index < 0 ? hunters.Count + index : index;
        if (i < 0 || i >= hunters.Count) throw new InvalidOperationException($"no hunter {index}; {hunters.Count} live");
        return hunters[i];
    }

    /// <summary>Runs the operational hit effects of a vanilla shot on a part of the target, as a real hit would.</summary>
    static void Hit(SimRoot sim, Ship target, (string Bullet, Type HitType) shot)
    {
        var source = SimQueries.FirstHumanShip(sim) ?? throw new InvalidOperationException("no human ship");
        var bullet = GameApp.Rules.GetBulletRules(new ID<BulletRules>(shot.Bullet));
        var hitRules = bullet.Components.FirstOrDefault(c => c.GetType() == shot.HitType)
            ?? throw new InvalidOperationException($"{shot.Bullet} has no {shot.HitType.Name}");
        var effects = (hitRules switch
        {
            BulletPenetratingHitRules penetrating => penetrating.HitOperational,
            BulletSimpleHitRules simple => simple.HitOperational,
            _ => null,
        })?.HitEffects ?? throw new InvalidOperationException($"{shot.Bullet} has no operational hit effects");
        // A part with battery storage, so the EMP area drain finds something to drain next to the hit point.
        var battery = new ID<Cosmoteer.Resources.ResourceRules>("battery");
        var part = target.Parts.FirstOrDefault(pt => Cosmoteer.Ships.Parts.Resources.BaseResourceStorage.GetAllSimpleStoragesOfTypeOnPart(battery, pt).Any())
                   ?? target.Parts.First();
        using var p = HitEffectParams.Alloc(sim, part.DetWorldCenter);
        p.SourceShip = source;
        p.SourcePlayerIndex = source.Metadata.PlayerIndex;
        p.TargetShip = target;
        p.HitShip = target;
        p.HitPart = part;
        p.EffectScale = 1f;
        effects.DoEffect(p);
        Log.Info($"AUTOTEST {shot.Bullet} hit {target.Metadata.PlainShipName} at {part.Rules.ID}");
    }

    /// <summary>App.Director does not exist yet when Register runs, so the overlay hook starts at the first title screen.</summary>
    [HarmonyPatch(typeof(Cosmoteer.Gui.TitleScreen), MethodType.Constructor)]
    static class FrameHook
    {
        static bool _started;

        static void Postfix()
        {
            if (_started) return;
            _started = true;
            App.Director.FrameEnded += (_, _) =>
            {
                if (Run.Current is { } run) Errors.Guard("ai overlay hook", () => AiOverlay.Update(run));
            };
        }
    }
}

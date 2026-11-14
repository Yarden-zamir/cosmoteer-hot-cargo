using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.AI.StrategyModules;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;
using Halfling.Timing;
using HotCargo.Core;
using HotCargo.Hunters;

namespace HotCargo.Tactics;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcFallBack")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class FallBackStrategyRules : AIStrategyModuleRules
{
    [Serialize(Optional = true), PortableHash] public int RepairChecksPerTick = 2;
    [Serialize(Optional = true), PortableHash] public HashSet<ID<SimObjectSpawner>>? AvoidableDoodadTags;
    [Serialize(Optional = true), PortableHash] public float AvoidableDoodadBuffer;

    public override AIStrategyModule CreateModule(AIParameters? parameters) => new FallBackStrategy(this);
}

/// <summary>
/// Spec 47, 1305-1311. A badly damaged ship breaks off and repairs, then rejoins the fight. It picks where: behind a rock when it
/// carries the resources, next to a friendly ship that gives it resources, at a friendly station that supplies resources and crew,
/// or just away from the players. Tell: the ship turns away, flies to the rock, ally or station, sits still while its crew work.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class FallBackStrategy : AIStrategyModule
{
    readonly FallBackStrategyRules _rules;
    readonly AISelfRepairModule _repair;
    [Serialize(Optional = true)] bool _active;
    [Serialize(Optional = true)] Time _since;
    [Serialize(Optional = true)] Time _cooldownUntil;
    [Serialize(Optional = true)] int _plan;
    /// <summary>The donor ship or the station of the plan.</summary>
    [Serialize(Optional = true)] ObjectID _target;
    [Serialize(Alias = "Rock", Optional = true)] Ship? _rock;
    /// <summary>The plan's one-time step is done: the station supplied the ship, the donor gave, or the ship reached its cover.</summary>
    [Serialize(Optional = true)] bool _supplied;

    public FallBackStrategy(FallBackStrategyRules rules)
    {
        _rules = rules;
        // Vanilla repair logic, used only while the ship repairs. Partial work lets crews repair with the resources on board.
        _repair = new AISelfRepairModule(new AISelfRepairModuleRules
        {
            RepairChecksPerTick = rules.RepairChecksPerTick,
            AllowPartialRepair = true,
            AllowPartialRebuild = true,
            AvoidableDoodadTags = rules.AvoidableDoodadTags,
            AvoidableDoodadBuffer = rules.AvoidableDoodadBuffer,
        });
    }

    /// <summary>Tactics log event of a donor transfer (autotest field tactic_fallback_take).</summary>
    public const string TakeEvent = "fallback_take";

    public RepairPlan Plan => _active ? (RepairPlan)_plan : RepairPlan.None;
    public bool Supplied => _supplied;

    /// <summary>True when the ship falls back in cover and its rock blocks the line to the nearest enemy. For autotests.</summary>
    public bool InCover(Ship ship) =>
        Plan == RepairPlan.Cover && _rock is { } rock && Ai.AiGate.NearestHumanEnemy(ship) is { } threat && CoverStrategy.Covered(ship, threat, rock);

    /// <summary>True when the ship falls back to a station and is next to it.</summary>
    public bool AtStation(Ship ship) => Plan == RepairPlan.Station && Resupply.Station(ship, _target) is { } station && Resupply.AtStation(ship, station);

    /// <summary>Place and hull radius of the donor or station of the plan, or null. For autotests.</summary>
    public (Vector2 Center, float Radius)? TargetCenter(Ship ship) => Plan switch
    {
        RepairPlan.Donor when HunterOrders.Sim(ship).Ships[_target] is { } donor => (donor.DetWorldCenter, donor.WorldBoundingRadius),
        RepairPlan.Station when Resupply.Station(ship, _target) is { } station => (station.Center, station.Radius),
        _ => null,
    };

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        _active.GetIntegrityHash(iHash);
        _since.GetIntegrityHash(iHash);
        _plan.GetIntegrityHash(iHash);
        _target.GetIntegrityHash(iHash);
        (_rock?.UniqueID ?? default).GetIntegrityHash(iHash);
        _supplied.GetIntegrityHash(iHash);
    }

    public override void OnEnteringStasis(Ship ship) => _rock = null;

    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        if (TacticGate.Active(Tactic.FallBack, ship) is not { } t)
        {
            _active = false;
            return false;
        }
        Time now = HunterOrders.Sim(ship).LogicalTime;
        float health = HunterOrders.HealthFraction(ship);
        bool wasActive = _active;
        float limit = RepairModel.TimeLimit((RepairPlan)_plan, t.FallBackMaxSeconds, t.FallBackTripSeconds);
        _active = TacticsModel.FallBackActive(_active, health, t.FallBackHealthFraction, t.FallBackRecoverFraction,
            timedOut: now - _since >= limit, coolingDown: now < _cooldownUntil);
        if (_active && !wasActive)
        {
            _since = now;
            SetPlan(RepairPlan.None);
            TacticsLog.Event(ship, Tactic.FallBack, $"start at health {health:0.00}");
        }
        if (!_active)
        {
            if (wasActive)
            {
                _cooldownUntil = now + limit;
                TacticsLog.Event(ship, Tactic.FallBack, $"return at health {health:0.00} ({(RepairPlan)_plan})");
                SetPlan(RepairPlan.None);
            }
            return false;
        }
        var threat = Ai.AiGate.NearestHumanEnemy(ship);
        if ((RepairPlan)_plan == RepairPlan.None) PickPlan(ship, threat, t);
        return (RepairPlan)_plan switch
        {
            RepairPlan.Cover => Cover(ship, ai, threat, t),
            RepairPlan.Donor => Donor(ship, ai, threat, t),
            RepairPlan.Station => Station(ship, ai, threat, t),
            RepairPlan.Away => Away(ship, ai, threat, t),
            var plan => throw new InvalidOperationException($"fall back: no plan after a pick ({plan})"),
        };
    }

    public override AIStrategyModule Clone(AIParameters? parameters) => new FallBackStrategy(_rules)
    {
        _active = _active, _since = _since, _cooldownUntil = _cooldownUntil, _plan = _plan, _target = _target, _rock = _rock, _supplied = _supplied,
    };

    void SetPlan(RepairPlan plan, ObjectID target = default, Ship? rock = null) => (_plan, _target, _rock, _supplied) = ((int)plan, target, rock, false);

    /// <summary>Spec 1305. Measures what the ship lacks and picks the first plan that applies.</summary>
    void PickPlan(Ship ship, Ship? threat, TacticsTunables t)
    {
        if (!t.FallBackResupplyEnabled)
        {
            SetPlan(RepairPlan.Away);
            return;
        }
        var lacking = Resupply.Lacking(ship);
        bool shortOfCrew = Resupply.ShortOfCrew(ship, t);
        var rock = threat is null ? null : CoverStrategy.FindRock(ship, threat, t);
        var donor = Resupply.FindDonor(ship, lacking, t);
        var station = Resupply.FindStation(ship);
        var plan = RepairModel.PickPlan(lacking.Count > 0, shortOfCrew, new RepairOptions(rock is not null, donor is not null, station is not null));
        SetPlan(plan, plan switch
        {
            RepairPlan.Donor => donor!.UniqueID,
            RepairPlan.Station => station!.Value.Id,
            _ => default,
        }, plan == RepairPlan.Cover ? rock : null);
        string lacks = lacking.Count == 0 ? "nothing" : string.Join(", ", lacking.Select(kv => $"{kv.Value} {kv.Key}"));
        TacticsLog.Event(ship, Tactic.FallBack, $"plan {plan}: lacks {lacks}, crew {ship.Crew.Assigned.Count}/{ship.Crew.TotalCrewCapacity}");
    }

    /// <summary>Holds still while the vanilla module queues repairs. True: this module drives the ship.</summary>
    bool RepairHere(Ship ship, ShipAI ai, TacticsTunables t)
    {
        if (!Resupply.RepairBlocked(ship, t)) _repair.OnAITick(ship, ai);
        return true;
    }

    /// <summary>Spec 47: fly FallBackDistance away from the nearest enemy, then repair with what the ship has.</summary>
    bool Away(Ship ship, ShipAI ai, Ship? threat, TacticsTunables t)
    {
        if (threat is null || HunterOrders.EdgeDistance(ship, threat) >= t.FallBackDistance) return RepairHere(ship, ai, t);
        Vector2 away = Vector2.FromAngle(threat.DetWorldCenter.DirectionTo(ship.DetWorldCenter));
        Ai.PersonalityStrategy.MoveGuarded(ship, threat.DetWorldCenter + away * (t.FallBackDistance * 1.25f + threat.WorldBoundingRadius), t.FallBackDistance * 0.25f, threat, t);
        return true;
    }

    /// <summary>Spec 1306: hide behind the rock from the nearest enemy and repair there.</summary>
    bool Cover(Ship ship, ShipAI ai, Ship? threat, TacticsTunables t)
    {
        if (threat is null) return RepairHere(ship, ai, t);
        if (_rock is not { } rock || rock.IsDisposed || rock.Sim != ship.Sim || !CoverStrategy.Usable(ship, threat, rock, t)) return Repick(ship, threat, t, "rock lost");
        var spot = CoverStrategy.Spot(ship, threat, rock, t);
        // The vanilla repair stops the ship, so it holds only where the rock blocks the line to the threat.
        if (spot.DistanceTo(ship.DetWorldCenter) <= t.FallBackArriveDistance && CoverStrategy.Covered(ship, threat, rock))
        {
            if (!_supplied) TacticsLog.Event(ship, Tactic.FallBack, $"in cover from {threat.Metadata.PlainShipName}");
            // In the cover plan the flag marks the arrival, so the log line shows once.
            _supplied = true;
            return RepairHere(ship, ai, t);
        }
        Ai.PersonalityStrategy.MoveGuarded(ship, spot, t.FallBackArriveDistance * 0.5f, threat, t);
        return true;
    }

    /// <summary>
    /// Spec 1307: fly next to the donor and take the resources at once. Then repair as in the away plan, within the trip time.
    /// One donor visit per fall back, so a ship does not drain its allies one share after another.
    /// </summary>
    bool Donor(Ship ship, ShipAI ai, Ship? threat, TacticsTunables t)
    {
        if (_supplied) return Away(ship, ai, threat, t);
        if (HunterOrders.Sim(ship).Ships[_target] is not { } donor || !donor.IsLiveWith(ship) || ship.IsEnemiesWith(donor))
            return Repick(ship, threat, t, "donor lost");
        if (!Resupply.InTransferRange(ship, donor))
        {
            Ai.PersonalityStrategy.MoveGuarded(ship, Resupply.Beside(ship, donor.DetWorldCenter, donor.WorldBoundingRadius, t.FallBackDockGap), t.FallBackDockGap * 0.5f, threat, t);
            return true;
        }
        int moved = Resupply.TakeFrom(ship, donor, t);
        _supplied = true;
        TacticsLog.Event(ship, TakeEvent, $"takes {moved} units from {donor.Metadata.PlainShipName}");
        return Away(ship, ai, threat, t);
    }

    /// <summary>Spec 1308: fly to the station, take its free supply once, then hold there and repair with the station crews.</summary>
    bool Station(Ship ship, ShipAI ai, Ship? threat, TacticsTunables t)
    {
        if (Resupply.Station(ship, _target) is not { } station) return Repick(ship, threat, t, "station lost");
        Resupply.VisitStation(ship, station, ref _supplied, threat, t);
        return true;
    }

    bool Repick(Ship ship, Ship? threat, TacticsTunables t, string why)
    {
        TacticsLog.Event(ship, Tactic.FallBack, $"new plan: {why}");
        PickPlan(ship, threat, t);
        // The new plan acts on the next AI tick, so one tick never picks twice.
        return true;
    }
}

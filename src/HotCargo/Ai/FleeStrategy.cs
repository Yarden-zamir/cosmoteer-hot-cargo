using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.AI.StrategyModules;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;
using Halfling.Timing;
using Cosmoteer.Game;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Tactics;

namespace HotCargo.Ai;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcFlee")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class FleeStrategyRules : AIStrategyModuleRules
{
    /// <summary>True in the hunter AI: the Tactics.FleeEnabled switch applies. A fleeing corporate hunter jumps out of the system; other ships repair and come back.</summary>
    [Serialize(Optional = true), PortableHash] public bool ForHunters;
    [Serialize(Optional = true), PortableHash] public int RepairChecksPerTick = 2;
    [Serialize(Optional = true), PortableHash] public HashSet<ID<SimObjectSpawner>>? AvoidableDoodadTags;
    [Serialize(Optional = true), PortableHash] public float AvoidableDoodadBuffer;

    public override AIStrategyModule CreateModule(AIParameters? parameters) => new FleeStrategy(this);
}

/// <summary>
/// Spec 193-195. A badly damaged ship may decide to run (one roll each time it drops below the flee health),
/// and a ship without offensive weapons always runs. It keeps away from the nearest enemy.
/// A corporate hunter jumps out of the system after FleeJumpSeconds. Any other ship repairs when clear, at a friendly station
/// when it lacks resources or crew (spec 1314-1316), and fights again once recovered.
/// Tell: the ship pulls away, a corporate hunter's label shows the jump countdown, and the ship jumps out with the FTL effect.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class FleeStrategy : AIStrategyModule
{
    readonly FleeStrategyRules _rules;
    readonly AISelfRepairModule _repair;
    [Serialize(Optional = true)] bool _fleeing;
    [Serialize(Optional = true)] bool _rolled;
    [Serialize(Optional = true)] Time _since;
    [Serialize(Optional = true)] Time? _removeAt;
    /// <summary>Spec 1314: true after the once-per-flee check of what the ship lacks.</summary>
    [Serialize(Optional = true)] bool _checked;
    /// <summary>Spec 1314: the friendly station that the ship flees to, or default for none.</summary>
    [Serialize(Optional = true)] ObjectID _station;
    [Serialize(Optional = true)] bool _supplied;

    public FleeStrategy(FleeStrategyRules rules)
    {
        _rules = rules;
        _repair = new AISelfRepairModule(new AISelfRepairModuleRules
        {
            RepairChecksPerTick = rules.RepairChecksPerTick,
            AllowPartialRepair = true,
            AllowPartialRebuild = true,
            AvoidableDoodadTags = rules.AvoidableDoodadTags,
            AvoidableDoodadBuffer = rules.AvoidableDoodadBuffer,
        });
    }

    public bool Fleeing => _fleeing;
    public bool Supplied => _supplied;

    /// <summary>Place and hull radius of the station that the fleeing ship goes to, or null. For autotests.</summary>
    public (Vector2 Center, float Radius)? StationCenter(Ship ship) =>
        _fleeing && _station != default && Resupply.Station(ship, _station) is { } s ? (s.Center, s.Radius) : null;

    /// <summary>Seconds until a fleeing hunter jumps out, or null. For the overlay label.</summary>
    public double? SecondsToJump(Ship ship, TacticsTunables t) =>
        _fleeing && AiGate.IsCorporateHunter(ship) ? Math.Max(0, t.FleeJumpSeconds - (HunterOrders.Sim(ship).LogicalTime - _since).Seconds) : null;

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        _fleeing.GetIntegrityHash(iHash);
        _rolled.GetIntegrityHash(iHash);
        _since.GetIntegrityHash(iHash);
        (_removeAt ?? Time.Zero).GetIntegrityHash(iHash);
        _checked.GetIntegrityHash(iHash);
        _station.GetIntegrityHash(iHash);
        _supplied.GetIntegrityHash(iHash);
    }

    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        var sim = HunterOrders.Sim(ship);
        Time now = sim.LogicalTime;
        if (_removeAt is { } removeAt)
        {
            // Same removal as AIExitSectorModule: the FTL effect plays, then the ship leaves the sim.
            if (now >= removeAt) sim.EnqueueDeterministic(ship.UniqueID, () => sim.Ships.Remove(ship, dispose: true, handleLostShip: false));
            return true;
        }
        if (AiGate.Active(Behavior.Flee, _rules.ForHunters) is not { } t || AiGate.Surrendered(ship) || !AiGate.HostileToPlayers(ship))
        {
            _fleeing = false;
            return false;
        }
        float health = HunterOrders.HealthFraction(ship);
        bool hasWeapons = AiGate.HasOffensiveWeapons(ship);
        // Only corporate hunters jump out. Other ships with the hunter AI (shady trap and event ambushers) are mission targets, so they stay like vanilla ships.
        bool corporate = AiGate.IsCorporateHunter(ship);
        bool wasRolled = _rolled;
        var next = AiModel.NextFlee(new FleeState(_fleeing, _rolled), hasWeapons, health, t.FleeHealthFraction, t.FleeRecoverFraction,
            t.FleeChance, canStop: !corporate, () => ship.Rand.Single());
        if (next.Rolled && !wasRolled && !next.Fleeing) TacticsLog.Event(ship, AiArea.FleeStay, $"stays at health {health:0.00}");
        if (next.Fleeing != _fleeing)
        {
            if (next.Fleeing)
            {
                _since = now;
                (_checked, _station, _supplied) = (false, default, false);
                TacticsLog.Event(ship, AiArea.Flee, $"starts at health {health:0.00} weapons={hasWeapons}");
            }
            else TacticsLog.Event(ship, AiArea.FleeEnd, $"fights again at health {health:0.00}");
        }
        (_fleeing, _rolled) = (next.Fleeing, next.Rolled);
        if (!_fleeing) return false;

        if (corporate && (now - _since).Seconds >= t.FleeJumpSeconds)
        {
            _ = ship.Renderer.AnimateFtlJumpOut(ship.WorldFlightDirection);
            _removeAt = now + ship.Rules.FtlEffects.TotalDuration;
            if (Core.Run.Current is { } run) HunterSpawner.ForgetHeldFuel(run, ship);
            TacticsLog.Event(ship, AiArea.FleeJump, "jumps out of the system");
            AiText.Chat(ship, "JumpedOutFmt");
            return true;
        }
        var threat = AiGate.NearestEnemy(ship);
        if (threat is not null && HunterOrders.EdgeDistance(ship, threat) < t.FleeDistance)
        {
            Vector2 away = Vector2.FromAngle(threat.DetWorldCenter.DirectionTo(ship.DetWorldCenter));
            PersonalityStrategy.MoveGuarded(ship, threat.DetWorldCenter + away * (t.FleeDistance * 1.25f + threat.WorldBoundingRadius), t.FleeDistance * 0.25f, threat, t);
            return true;
        }
        if (corporate) return true;
        // Clear of enemies (spec 1314): a ship that lacks resources or crew goes to a friendly station, else it repairs here.
        if (!_checked)
        {
            _checked = true;
            if (t.FallBackResupplyEnabled && (Resupply.Lacking(ship).Count > 0 || Resupply.ShortOfCrew(ship, t)) && Resupply.FindStation(ship) is { } found)
            {
                _station = found.Id;
                TacticsLog.Event(ship, AiArea.Flee, $"heads for station {found.Live?.Metadata.PlainShipName ?? found.Id.ToString()}");
            }
        }
        if (_station != default && Resupply.Station(ship, _station) is { } station)
        {
            Resupply.VisitStation(ship, station, ref _supplied, threat, t);
            return true;
        }
        _repair.OnAITick(ship, ai);
        return true;
    }

    public override AIStrategyModule Clone(AIParameters? parameters) =>
        new FleeStrategy(_rules)
        {
            _fleeing = _fleeing, _rolled = _rolled, _since = _since, _removeAt = _removeAt, _checked = _checked, _station = _station, _supplied = _supplied,
        };
}

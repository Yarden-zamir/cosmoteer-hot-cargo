using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation.HitEffects;
using Halfling.PortableHashing;
using Halfling.Serialization;
using Halfling.Timing;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Tactics;

namespace HotCargo.Ai;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcCall")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class CallStrategyRules : AIStrategyModuleRules
{
    /// <summary>True in the hunter AI. A hunter call brings the next waiting hunter group; a vanilla call brings allies.</summary>
    [Serialize(Optional = true), PortableHash] public bool ForHunters;
    /// <summary>An answering ally renews its move only when the target moved further than this.</summary>
    [Serialize(Optional = true), PortableHash] public float MoveTolerance = 300f;

    public override AIStrategyModule CreateModule(AIParameters? parameters) => new CallStrategy(this);
}

/// <summary>
/// Spec 196-199. A ship that first spots a player ship, or takes damage with a player ship in sight, calls its allies.
/// The call takes CallSeconds and shows a growing ring, a label and a log line. One disruptor, EMP or ion beam hit stops it,
/// and a storm blocks it. A completed vanilla call sends the allies in range at the target; a completed hunter call brings
/// the next waiting hunter group at once. The caller keeps fighting while it calls: this module takes over movement only
/// for an ally that answers.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class CallStrategy(CallStrategyRules rules) : AIStrategyModule
{
    [Serialize(Optional = true)] bool _calling;
    [Serialize(Optional = true)] Time _callStart;
    [Serialize(Optional = true)] Time _cooldownUntil;
    [Serialize(Optional = true)] bool _sawEnemy;
    [Serialize(Optional = true)] float _lastHealth = -1f;
    [Serialize(Alias = "Target", Optional = true)] Ship? _target;
    [Serialize(Alias = "Assist", Optional = true)] Ship? _assist;
    [Serialize(Optional = true)] Time _assistUntil;
    bool _blockedLogged;

    public bool Calling => _calling;
    public bool Assisting => _assist is not null;

    public float Progress(Ship ship, TacticsTunables t) => AiModel.CallProgress((HunterOrders.Sim(ship).LogicalTime - _callStart).Seconds, t.CallSeconds);

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        _calling.GetIntegrityHash(iHash);
        _callStart.GetIntegrityHash(iHash);
        _cooldownUntil.GetIntegrityHash(iHash);
        _sawEnemy.GetIntegrityHash(iHash);
        _assistUntil.GetIntegrityHash(iHash);
        (_target?.UniqueID ?? default).GetIntegrityHash(iHash);
        (_assist?.UniqueID ?? default).GetIntegrityHash(iHash);
    }

    public override void OnEnteringStasis(Ship ship)
    {
        _calling = false;
        _target = null;
        _assist = null;
    }

    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        if (AiGate.Active(Behavior.Call, rules.ForHunters) is not { } t || AiGate.Surrendered(ship))
        {
            _calling = false;
            _assist = null;
            return false;
        }
        Time now = HunterOrders.Sim(ship).LogicalTime;
        float health = HunterOrders.HealthFraction(ship);
        bool damaged = _lastHealth >= 0f && AiModel.TookDamage(_lastHealth, health, t.CallDamageTrigger);
        _lastHealth = health;
        var spotted = AiGate.NearestHumanEnemy(ship);
        bool triggered = spotted is not null && (!_sawEnemy || damaged);
        _sawEnemy = spotted is not null;

        bool inStorm = AiGate.InStorm(ship, t);
        // Listeners and other callers are looked up only when a call could start, because the lookups walk the ship list.
        bool canStart = !_calling && triggered && now >= _cooldownUntil;
        var step = AiModel.NextCall(_calling, (now - _callStart).Seconds, t.CallSeconds, triggered,
            hasListeners: canStart && HasListeners(ship, t), inStorm, coolingDown: now < _cooldownUntil,
            allyCalling: canStart && AllyCalling(ship, t));
        switch (step)
        {
            case CallStep.Start:
                _calling = true;
                _callStart = now;
                _target = spotted;
                _blockedLogged = false;
                TacticsLog.Event(ship, AiArea.CallStart, $"calls about {spotted?.Metadata.PlainShipName}");
                AiText.Chat(ship, "CallStartFmt");
                break;
            case CallStep.Blocked when _calling:
                Stop(ship, now, t, AiArea.CallBlocked, "storm");
                break;
            case CallStep.Blocked:
                if (!_blockedLogged) TacticsLog.Event(ship, AiArea.CallBlocked, "a storm blocks the call");
                _blockedLogged = true;
                break;
            case CallStep.Complete:
                Complete(ship, now, t);
                break;
        }
        return Assist(ship, now);
    }

    public override AIStrategyModule Clone(AIParameters? parameters) => new CallStrategy(rules)
    {
        _calling = _calling, _callStart = _callStart, _cooldownUntil = _cooldownUntil, _sawEnemy = _sawEnemy,
        _lastHealth = _lastHealth, _target = _target, _assist = _assist, _assistUntil = _assistUntil,
    };

    /// <summary>Stops a running call: a disruptor hit or a storm. The ship cannot call again until its cooldown ends.</summary>
    public void Stop(Ship ship, Time now, TacticsTunables t, string eventName, string reason)
    {
        if (!_calling) return;
        _calling = false;
        _cooldownUntil = now + t.CallCooldownSeconds;
        TacticsLog.Event(ship, eventName, $"call stopped: {reason}");
        AiText.Chat(ship, "CallStoppedFmt");
    }

    void Complete(Ship ship, Time now, TacticsTunables t)
    {
        _calling = false;
        _cooldownUntil = now + t.CallCooldownSeconds;
        int answered = 0;
        if (rules.ForHunters)
        {
            if (Run.Current is { } run && HunterSpawner.PullNextWave(run)) answered = 1;
        }
        else
        {
            foreach (var listener in Listeners(ship, t).ToList())
            {
                listener.Module.Answer(_target, now + t.CallAssistSeconds, now + t.CallCooldownSeconds);
                TacticsLog.Event(listener.Ship, AiArea.CallAssist, $"answers {ship.Metadata.PlainShipName}");
                answered++;
            }
        }
        TacticsLog.Event(ship, AiArea.CallComplete, $"call complete, {answered} answered");
        AiText.Chat(ship, "CallCompleteFmt");
    }

    void Answer(Ship? target, Time assistUntil, Time cooldownUntil)
    {
        _assist = target;
        _assistUntil = assistUntil;
        _cooldownUntil = cooldownUntil > _cooldownUntil ? cooldownUntil : _cooldownUntil;
    }

    /// <summary>An answering ally attacks the caller's target, or flies to it while it is off radar. Its own fight comes first.</summary>
    bool Assist(Ship ship, Time now)
    {
        if (_assist is null) return false;
        if (now >= _assistUntil || _assist.IsDisposed || _assist.IsJunk || !ship.IsEnemiesWith(_assist))
        {
            _assist = null;
            return false;
        }
        if (ship.Commands.CurConfirmedCommand is AttackCommand ac && ac.IsValid() && ac.Target != _assist) return false;
        if (_assist.IsWithinRadarOfTeam(ship.Metadata.Team)) HunterOrders.Attack(ship, _assist);
        else HunterOrders.MoveTo(ship, _assist.DetWorldCenter, rules.MoveTolerance);
        return true;
    }

    bool HasListeners(Ship ship, TacticsTunables t) =>
        rules.ForHunters ? Run.Current?.State.Hunters.Pending.Count > 0 : Listeners(ship, t).Any();

    /// <summary>Allies in call range with this module, outside storms, that are not calling or answering already.</summary>
    static IEnumerable<(Ship Ship, CallStrategy Module)> Listeners(Ship ship, TacticsTunables t)
    {
        foreach (var ally in AiGate.Allies(ship))
        {
            if (ally.AI?.GetStrategyModule<CallStrategy>() is not { } m || m._calling || m.Assisting) continue;
            if (ally.DetWorldCenter.DistanceTo(ship.DetWorldCenter) > t.CallRange || AiGate.InStorm(ally, t)) continue;
            yield return (ally, m);
        }
    }

    /// <summary>One caller at a time in a group: no new call while an ally in call range is calling.</summary>
    static bool AllyCalling(Ship ship, TacticsTunables t) =>
        AiGate.Allies(ship).Any(a => a.AI?.GetStrategyModule<CallStrategy>() is { _calling: true }
                                     && a.DetWorldCenter.DistanceTo(ship.DetWorldCenter) <= t.CallRange);

    // ---- Interrupts (spec 197). The hit effects run inside the sim tick on every client, so the interrupt is deterministic.

    static readonly ID<ResourceRules> Battery = new("battery");
    static string[]? _partSource;
    static HashSet<ID<PartRules>> _interruptParts = [];

    /// <summary>A hit from an enemy stops the hit ship's call.</summary>
    static void OnDisruptHit(Ship? hitShip, Ship? sourceShip, string what)
    {
        if (hitShip?.AI?.GetStrategyModule<CallStrategy>() is not { _calling: true } module) return;
        if (sourceShip is not null && !sourceShip.IsEnemiesWith(hitShip)) return;
        if (Run.Current is not { } run) return;
        module.Stop(hitShip, HunterOrders.Sim(hitShip).LogicalTime, run.T.Tactics, AiArea.CallInterrupt, what);
    }

    /// <summary>Disruptor bolts and the disruptor drain status drain battery power from the hit part.</summary>
    [HarmonyPatch(typeof(ResourceDrainEffectRules), nameof(ResourceDrainEffectRules.DoEffect))]
    static class DrainHit
    {
        static void Postfix(ResourceDrainEffectRules __instance, HitEffectParams effectParams)
        {
            if (__instance.ResourceType != Battery) return;
            try { OnDisruptHit(effectParams.HitShip, effectParams.SourceShip, "disruptor hit"); }
            catch (Exception ex) { Errors.Report("call interrupt", ex.Message, ex); }
        }
    }

    /// <summary>EMP missiles drain battery power in an area. Each drained part reports here.</summary>
    [HarmonyPatch(typeof(ExplosiveResourceDrainEffectRules.Proxy), nameof(ExplosiveResourceDrainEffectRules.Proxy.OnHit))]
    static class EmpHit
    {
        static void Postfix(ref ExplosiveResourceDrainEffectRules.Proxy __instance, HitEffectParams effectParams, in ExplosiveResourceDrainEffectRules effectRules)
        {
            if (effectRules.ResourceType != Battery) return;
            try { OnDisruptHit(__instance.AnchorShip, effectParams.SourceShip, "EMP hit"); }
            catch (Exception ex) { Errors.Report("call interrupt", ex.Message, ex); }
        }
    }

    /// <summary>
    /// Ion beams drain no battery power, so their damage counts by the firing part (Tactics.CallInterruptParts).
    /// This runs on every damage hit, so it checks the part first and allocates nothing.
    /// </summary>
    [HarmonyPatch(typeof(DamageEffectRules), nameof(DamageEffectRules.DoEffect))]
    static class DamageHit
    {
        static void Postfix(HitEffectParams effectParams)
        {
            if (effectParams.SourcePart is not { } part || Run.Current is not { } run) return;
            try
            {
                var parts = run.T.Tactics.CallInterruptParts;
                if (!ReferenceEquals(_partSource, parts))
                {
                    _interruptParts = parts.Select(p => new ID<PartRules>(p)).ToHashSet();
                    _partSource = parts;
                }
                if (_interruptParts.Contains(part.Rules.ID)) OnDisruptHit(effectParams.HitShip, effectParams.SourceShip, "ion beam hit");
            }
            catch (Exception ex) { Errors.Report("call interrupt", ex.Message, ex); }
        }
    }
}

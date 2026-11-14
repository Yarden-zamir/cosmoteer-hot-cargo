using Cosmoteer;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Halfling.PortableHashing;
using Halfling.Serialization;
using HotCargo.Core;

namespace HotCargo.Scavenge;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcScavenge")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class ScavengeStrategyRules : AIStrategyModuleRules
{
    /// <summary>Spec 1336: the ship sells what it collected at a station. False: it keeps everything.</summary>
    [Serialize(Optional = true), PortableHash] public bool Sells = true;

    public override AIStrategyModule CreateModule(AIParameters? parameters) => new ScavengeStrategy(this);
}

/// <summary>
/// Spec 1337, 1350. An idle ship scavenges: it salvages old wrecks and fetches old loose resources, then sells the cargo (Scavenger).
/// It keeps the cargo that it had at its first AI tick, so a faction ship never sells its own stock; a scavenger starts empty and sells
/// everything. It gives control back to the other modules when an enemy shows up, when it has nothing to do, when its side may not
/// scavenge (FactionsScavenge, PiratesScavenge), or with scavenging off.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class ScavengeStrategy(ScavengeStrategyRules rules) : AIStrategyModule
{
    [Serialize(Optional = true)] ScavengeStep _step;
    /// <summary>Spec 1350: the storage bay cargo by type at the first AI tick. Null until then.</summary>
    [Serialize(Optional = true)] Dictionary<string, int>? _keep;

    /// <summary>The step of the last AI tick. For logs and tests.</summary>
    public ScavengeStep Step => _step;

    public bool Sells => rules.Sells;

    /// <summary>Storage bay units above the kept stock (what the ship collected and may sell). For tests.</summary>
    public int Excess(Ship ship) => Autonomy.AutonomyWorld.CargoByType(ship).Sum(kv => Math.Max(0, kv.Value - (_keep?.GetValueOrDefault(kv.Key) ?? 0)));

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        iHash.AddHash((int)_step);
        iHash.AddHash(_keep?.Values.Sum() ?? -1);
    }

    public override void OnEnteringStasis(Ship ship) => _step = ScavengeStep.Idle;

    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        var step = ScavengeStep.Idle;
        if (Run.Current is { } run && run.T.Scavenge.Enabled && SideMay(run.T.Scavenge, ship))
        {
            _keep ??= new Dictionary<string, int>(Autonomy.AutonomyWorld.CargoByType(ship), StringComparer.Ordinal);
            var keep = _keep;
            if (Scavenger.Idle(ship, ai)) step = Scavenger.Step(run, ship, rules.Sells ? id => keep.GetValueOrDefault(id) : _ => int.MaxValue);
        }
        if (step != _step) Log.Info($"scavenge: {ship.Metadata.PlainShipName} {_step} -> {step}");
        _step = step;
        return step != ScavengeStep.Idle;
    }

    /// <summary>Spec 1350, 1351: scavengers always scavenge; vanilla faction ships with FactionsScavenge; every other side (pirates) with PiratesScavenge.</summary>
    static bool SideMay(ScavengeTunables t, Ship ship)
    {
        int index = ship.Metadata.PlayerIndex;
        if (Scavengers.IsScavenger(ship)) return true;
        return PlayerIndexes.IsFactionMilitary(index) || PlayerIndexes.IsFactionCivilian(index) ? t.FactionsScavenge : t.PiratesScavenge;
    }

    public override AIStrategyModule Clone(AIParameters? parameters) =>
        new ScavengeStrategy(rules) { _step = _step, _keep = _keep is null ? null : new Dictionary<string, int>(_keep, StringComparer.Ordinal) };
}

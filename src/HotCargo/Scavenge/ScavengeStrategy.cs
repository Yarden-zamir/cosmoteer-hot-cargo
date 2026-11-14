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
    /// <summary>Spec 1336: the ship sells its cargo at a station. False: it keeps what it collects.</summary>
    [Serialize(Optional = true), PortableHash] public bool Sells = true;

    public override AIStrategyModule CreateModule(AIParameters? parameters) => new ScavengeStrategy(this);
}

/// <summary>
/// Spec 1337. An idle ship scavenges: it salvages old wrecks and fetches old loose resources, then sells the cargo (Scavenger).
/// It gives control back to the other modules when an enemy shows up, when it has nothing to do, or with scavenging off.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class ScavengeStrategy(ScavengeStrategyRules rules) : AIStrategyModule
{
    [Serialize(Optional = true)] ScavengeStep _step;

    /// <summary>The step of the last AI tick. For logs and tests.</summary>
    public ScavengeStep Step => _step;

    public bool Sells => rules.Sells;

    public override void GetIntegrityHash(IntegrityHash iHash) => iHash.AddHash((int)_step);

    public override void OnEnteringStasis(Ship ship) => _step = ScavengeStep.Idle;

    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        var step = Run.Current is { } run && run.T.Scavenge.Enabled && Scavenger.Idle(ship, ai) ? Scavenger.Step(run, ship, rules.Sells) : ScavengeStep.Idle;
        if (step != _step) Log.Info($"scavenge: {ship.Metadata.PlainShipName} {_step} -> {step}");
        _step = step;
        return step != ScavengeStep.Idle;
    }

    public override AIStrategyModule Clone(AIParameters? parameters) => new ScavengeStrategy(rules) { _step = _step };
}

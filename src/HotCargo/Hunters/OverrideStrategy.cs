using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace HotCargo.Hunters;

/// <summary>Rules for the hunter override strategy. Rules name: HcOverride.</summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcOverride")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class OverrideStrategyRules : AIStrategyModuleRules
{
    public override AIStrategyModule CreateModule(AIParameters? parameters) => new OverrideStrategy();
}

/// <summary>
/// Runs HunterOrders.Override above every hunter tactic (team, regroup, cover, bait, grab tech), so a ship with orders from
/// another area keeps them. Only fall back, call and flee come first (mod/tactics/ai_hunter.rules).
/// </summary>
internal sealed class OverrideStrategy : AIStrategyModule
{
    public override bool OnAITick(Ship ship, ShipAI ai) => HunterOrders.Override(ship);

    public override AIStrategyModule Clone(AIParameters? parameters) => new OverrideStrategy();
}

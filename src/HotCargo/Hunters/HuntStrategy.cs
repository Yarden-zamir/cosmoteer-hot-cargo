using Cosmoteer;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Data;
using Halfling.PortableHashing;
using Halfling.Serialization;
using HotCargo.Core;
using HotCargo.Tactics;

namespace HotCargo.Hunters;

/// <summary>Rules for the hunter chase strategy. Rules name: HcHunt.</summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcHunt")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class HuntStrategyRules : AIStrategyModuleRules
{
    /// <summary>The current target counts this many times more, so hunters do not switch targets on small changes.</summary>
    [Serialize(Optional = true), PortableHash] public float TargetSwitchThresholdFactor = 1.5f;
    /// <summary>A chase move is renewed only when the target moved further than this from the last move destination.</summary>
    [Serialize(Optional = true), PortableHash] public float ChaseMoveTolerance = 300f;

    public override AIStrategyModule CreateModule(AIParameters? parameters) => new HuntStrategy(this);
}

/// <summary>
/// Spec 32 and 34. Hunters always know where the human ships are (they track the tech).
/// They pick the nearest human ship, weighted up when it carries a Tech Vault, and attack it.
/// Out of radar range they fly to its location. Tell: hunters fly straight at the vault ship, past escorts.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class HuntStrategy(HuntStrategyRules rules) : AIStrategyModule
{
    static readonly ID<PartRules> VaultPart = new("yarden_zamir.tech_vault");

    [Serialize(Alias = "Target", Optional = true)] Ship? _target;

    public override void GetIntegrityHash(IntegrityHash iHash) => (_target?.UniqueID ?? default).GetIntegrityHash(iHash);

    public override void OnEnteringStasis(Ship ship) => _target = null;

    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        Ship? previous = _target;
        _target = FindTarget(ship);
        if (_target is null) return false;
        if (_target != previous) TacticsLog.Event(ship, TacticsLog.Hunt, $"target {_target.Metadata.PlainShipName} vault={CarriesVault(_target)}");
        if (_target.IsWithinRadarOfTeam(ship.Metadata.Team)) HunterOrders.Attack(ship, _target);
        else HunterOrders.MoveTo(ship, _target.DetWorldCenter, rules.ChaseMoveTolerance);
        return true;
    }

    public override AIStrategyModule Clone(AIParameters? parameters) => new HuntStrategy(rules) { _target = _target };

    Ship? FindTarget(Ship ship)
    {
        float vaultFactor = Run.Current?.T.Hunters.VaultTargetFactor ?? 1f;
        Ship? best = null;
        float bestScore = 0f;
        foreach (var candidate in HunterSpawner.HumanShips(HunterOrders.Sim(ship)))
        {
            if (!ship.IsEnemiesWith(candidate) || !candidate.CanBeAttackedBy(ship)) continue;
            float score = 1f / Math.Max(HunterOrders.EdgeDistance(ship, candidate), 1f);
            if (CarriesVault(candidate)) score *= vaultFactor;
            if (candidate == _target) score *= rules.TargetSwitchThresholdFactor;
            if (score > bestScore) { best = candidate; bestScore = score; }
        }
        return best;
    }

    /// <summary>Matches the Tech area's part by ID, so this works (and finds nothing) when that part is not installed.</summary>
    static bool CarriesVault(Ship ship)
    {
        foreach (var part in ship.Parts)
            if (part.Rules.ID == VaultPart) return true;
        return false;
    }
}

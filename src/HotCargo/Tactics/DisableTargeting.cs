using Cosmoteer;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Weapons;
using Halfling;
using Halfling.Geometry;
using Halfling.PortableHashing;
using Halfling.Serialization;

namespace HotCargo.Tactics;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcDisable")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class DisableTargetingRules : AITargetingModuleRules
{
    public override AITargetingModule CreateModule(AIParameters? parameters) => new DisableTargeting();
}

/// <summary>
/// Spec 46. All weapons aim at the nearest weapon or thruster part of the attack target.
/// When the tactic is off, it assigns nothing and the vanilla ValueTargeter below it in the list decides.
/// Tell: the players see their guns and engines break first, and the target lines point at them.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class DisableTargeting : AITargetingModule
{
    [Serialize(Alias = "PartTarget", Optional = true)] Part? _partTarget;

    public override void GetIntegrityHash(IntegrityHash iHash) =>
        (_partTarget?.Location ?? new IntVector2(int.MinValue, int.MinValue)).GetIntegrityHash(iHash);

    public override void OnEnteringStasis(Ship ship) => _partTarget = null;

    public override void TargetWeapons(Ship ship, ShipAI ai, List<Weapon> weapons)
    {
        var target = (ship.Commands.CurConfirmedCommand as AttackCommand)?.Target;
        if (TacticGate.Active(Tactic.Disable) is not { } t || target?.Physics.Body is null)
        {
            _partTarget = null;
            return;
        }
        var previous = _partTarget;
        _partTarget = FindPart(ship, target, t.DisableSwitchFactor);
        if (_partTarget is null) return;
        if (_partTarget != previous) TacticsLog.Event(ship, Tactic.Disable, $"aim at {_partTarget.Rules.ID} on {target.Metadata.PlainShipName}");
        // Same assignment as AIValueTargeterModule.TargetWeapons.
        for (int i = weapons.Count - 1; i >= 0; i--)
        {
            var weapon = weapons[i];
            if (weapon.Rules.IsShipWideExplicitTargetingSuppressed(ship, _partTarget)) continue;
            if (weapon.ExplicitTargets.CurConfirmedTarget?.Target != _partTarget)
            {
                var wt = new WeaponTarget(weapon, _partTarget, ship.Weapons.AllocGlobalTargetID()) { PlayerTimeGiven = App.Clock.Time };
                weapon.ExplicitTargets.EnqueueTarget(wt, append: false, confirmed: true);
            }
            weapons.RemoveAt(i);
        }
    }

    public override AITargetingModule Clone(AIParameters? parameters) => new DisableTargeting { _partTarget = _partTarget };

    /// <summary>Nearest weapon or thruster part. The current part stays unless another one is switchFactor times closer.</summary>
    Part? FindPart(Ship ship, Ship target, float switchFactor)
    {
        Vector2 from = ship.DetWorldCenter;
        Part? best = null;
        float bestDistance = float.MaxValue;
        void Consider(Part? part)
        {
            if (part is null || part.Ship != target) return;
            float d = from.DistanceTo(part.DetWorldCenter);
            if (part == _partTarget) d /= switchFactor;
            if (d < bestDistance) { best = part; bestDistance = d; }
        }
        foreach (var weapon in target.Weapons) Consider(weapon.Part);
        foreach (var thruster in target.Thrusters) Consider(thruster.Part);
        return best;
    }
}

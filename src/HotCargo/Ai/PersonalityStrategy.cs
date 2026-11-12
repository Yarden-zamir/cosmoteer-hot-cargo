using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Defenses;
using Halfling.PortableHashing;
using Halfling.Serialization;
using Halfling.Timing;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Tactics;

namespace HotCargo.Ai;

[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HcPersonality")]
[ReflectivePortableHashing(Explicit = true)]
internal sealed class PersonalityStrategyRules : AIStrategyModuleRules
{
    public override AIStrategyModule CreateModule(AIParameters? parameters) => new PersonalityStrategy(this);
}

/// <summary>
/// Spec 360-363. The personality of one AI ship. The personality roll and the trait jitter come from the ship ID, so they need no
/// saved state and follow live tunables changes. The build profile is measured in the AI tick and saved with the ship, so every
/// client and a loaded game use the same numbers. This module never takes over the ship: it is last in the strategy list,
/// so it runs first in each AI tick, and it returns false.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class PersonalityStrategy(PersonalityStrategyRules rules) : AIStrategyModule
{
    public const string Hunters = "hunters", Pirates = "pirates";
    static readonly ID<PartCategory> ArmorCategory = new("armor");
    static readonly Dictionary<PartRules, bool> ShieldParts = [];

    [Serialize(Optional = true)] bool _measured;
    [Serialize(Optional = true)] Time _measuredAt;
    [Serialize(Optional = true)] float _accel, _turn, _range, _armor, _radius;
    [Serialize(Optional = true)] int _shields;
    // Log only: the traits at the last log line. Not sim state.
    uint _loggedBits = uint.MaxValue;

    ShipProfile Stored => new(_accel, _turn, _range, _armor, _shields, _radius);

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        _measured.GetIntegrityHash(iHash);
        _measuredAt.GetIntegrityHash(iHash);
        _accel.GetIntegrityHash(iHash);
        _turn.GetIntegrityHash(iHash);
        _range.GetIntegrityHash(iHash);
        _armor.GetIntegrityHash(iHash);
        _radius.GetIntegrityHash(iHash);
        _shields.GetIntegrityHash(iHash);
    }

    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        if (Run.Current is not { } run || !AiGate.HostileToPlayers(ship)) return false;
        var t = run.T.Tactics;
        Time now = HunterOrders.Sim(ship).LogicalTime;
        if (!_measured || (now - _measuredAt).Seconds >= t.ProfileRefreshSeconds)
        {
            (_accel, _turn, _range, _armor, _shields, _radius) = Measure(ship);
            _measured = true;
            _measuredAt = now;
        }
        if (t.PersonalitiesEnabled)
        {
            var traits = Traits(ship, t);
            if (traits.Bits != _loggedBits)
            {
                _loggedBits = traits.Bits;
                var b = PersonalityModel.Scores(Stored, t.Scale);
                TacticsLog.Event(ship, AiArea.PersonalityEvent,
                    $"{Pick(ship, t)?.Name ?? "none"} ({Group(ship)}) traits [{traits}] agility {b.Agility:0.00} turn {b.Turn:0.00} " +
                    $"range {b.Range:0.00} protection {b.Protection:0.00} size {b.Size:0.00} ({Stored})");
            }
        }
        return false;
    }

    public override AIStrategyModule Clone(AIParameters? parameters) => new PersonalityStrategy(rules)
    {
        _measured = _measured, _measuredAt = _measuredAt, _accel = _accel, _turn = _turn, _range = _range, _armor = _armor,
        _shields = _shields, _radius = _radius,
    };

    /// <summary>Personality group of a ship: "hunters" for the hunter AI, its faction ID, or "pirates" for other ships.</summary>
    public static string Group(Ship ship) =>
        AiGate.IsHunterAi(ship) ? Hunters : FactionRules.GetFaction(ship.Metadata.PlayerIndex)?.ID.ToString() ?? Pirates;

    static ulong Seed(Ship ship) => (uint)ship.UniqueID;

    /// <summary>The ship's personality: a weighted roll among the switched-on personalities of its group. Null when none matches.</summary>
    public static Personality? Pick(Ship ship, TacticsTunables t)
    {
        string group = Group(ship);
        var options = t.Personalities.Where(p => p.Groups.Contains(group) && !t.PersonalitiesOff.Contains(p.Name)).ToList();
        int index = PersonalityModel.PickWeighted(options.Select(p => p.Chance).ToList(), PersonalityModel.Unit(Seed(ship), PersonalityModel.PickSlot));
        return index < 0 ? null : options[index];
    }

    /// <summary>The saved build profile, or a live measure when the ship had no AI tick yet. Both read only sim state.</summary>
    public static ShipProfile Profile(Ship ship) =>
        ship.AI?.GetStrategyModule<PersonalityStrategy>() is { _measured: true } m ? m.Stored : Measure(ship);

    public static BuildScores Scores(Ship ship, TacticsTunables t) => PersonalityModel.Scores(Profile(ship), t.Scale);

    /// <summary>
    /// Spec 361-363. The traits that the ship uses now. None without a personality.
    /// Limit: computed on every call, without a cache; group checks (pincer, bait, team) call it for each ally. It is a few small
    /// lists and some arithmetic. Revisit with a per-tick cache if AI ticks show up in a profile of a fight with many hostile ships.
    /// </summary>
    public static TraitSet Traits(Ship ship, TacticsTunables t)
    {
        if (Pick(ship, t) is not { } personality) return TraitSet.None;
        var weights = PersonalityModel.Jittered(personality.Weights.ToArray(), personality.Jitter, Seed(ship));
        return PersonalityModel.Choose(weights, Scores(ship, t), t.PersonalityThreshold);
    }

    /// <summary>Spec 362. Measures the build from the operational thrusters, the parts and the weapons.</summary>
    public static ShipProfile Measure(Ship ship)
    {
        float thrust = 0f, torque = 0f;
        foreach (var thruster in ship.Thrusters)
        {
            thrust += thruster.Force;
            torque += Math.Abs(thruster.GetSRF().Z);
        }
        int cells = 0, armorCells = 0, shields = 0;
        foreach (var part in ship.Parts)
        {
            int area = part.Rules.Size.X * part.Rules.Size.Y;
            cells += area;
            if (part.Rules.TypeCategories.Contains(ArmorCategory)) armorCells += area;
            if (IsShield(part.Rules)) shields++;
        }
        float mass = ship.Parts.Mass;
        float inertia = ship.Physics.Body?.Inertia ?? 0f;
        return new(
            Acceleration: mass > 0f ? thrust / mass : 0f,
            Turn: inertia > 0f ? torque / inertia : 0f,
            Range: HunterOrders.MaxWeaponRange(ship, onlyActive: true),
            Armor: cells > 0 ? (float)armorCells / cells : 0f,
            Shields: shields,
            Radius: ship.WorldBoundingRadius);
    }

    /// <summary>A part with an arc shield component. Cached per part rules, which never change while the game runs.</summary>
    static bool IsShield(PartRules rules)
    {
        lock (ShieldParts)
        {
            if (!ShieldParts.TryGetValue(rules, out bool shield))
                ShieldParts[rules] = shield = rules.Components.Any(c => c is ArcShieldRules);
            return shield;
        }
    }
}

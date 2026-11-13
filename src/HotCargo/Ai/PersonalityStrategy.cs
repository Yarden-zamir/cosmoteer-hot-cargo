using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Defenses;
using Cosmoteer.Ships.Parts.Weapons;
using Halfling.Geometry;
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
    [Serialize(Optional = true)] byte _weakSides, _strongSide;
    // Log only: the traits at the last log line. Not sim state.
    uint _loggedBits = uint.MaxValue;

    ShipProfile Stored => new(_accel, _turn, _range, _armor, _shields, _radius, new SideProfile(_weakSides, _strongSide));

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
        ((int)_weakSides).GetIntegrityHash(iHash);
        ((int)_strongSide).GetIntegrityHash(iHash);
    }

    public override bool OnAITick(Ship ship, ShipAI ai)
    {
        if (Run.Current is not { } run || !AiGate.HostileToPlayers(ship)) return false;
        var t = run.T.Tactics;
        Time now = HunterOrders.Sim(ship).LogicalTime;
        if (!_measured || (now - _measuredAt).Seconds >= t.ProfileRefreshSeconds)
        {
            var p = Measure(ship, t);
            (_accel, _turn, _range, _armor, _shields, _radius, _weakSides, _strongSide) =
                (p.Acceleration, p.Turn, p.Range, p.Armor, p.Shields, p.Radius, p.Sides.Weak, p.Sides.Strong);
            _measured = true;
            _measuredAt = now;
        }
        if (t.PersonalitiesEnabled)
        {
            var traits = Traits(ship, t);
            if (traits.Bits != _loggedBits)
            {
                _loggedBits = traits.Bits;
                var b = Scores(ship, t);
                TacticsLog.Event(ship, AiArea.PersonalityEvent,
                    $"{Pick(ship, t)?.Name ?? "none"} ({Group(ship)}) traits [{traits}] agility {b.Agility:0.00} turn {b.Turn:0.00} " +
                    $"range {b.Range:0.00} protection {b.Protection:0.00} size {b.Size:0.00} weak sides {b.Sides.Weak:b8} strong {b.Sides.Strong} ({Stored})");
            }
        }
        return false;
    }

    public override AIStrategyModule Clone(AIParameters? parameters) => new PersonalityStrategy(rules)
    {
        _measured = _measured, _measuredAt = _measuredAt, _accel = _accel, _turn = _turn, _range = _range, _armor = _armor,
        _shields = _shields, _radius = _radius, _weakSides = _weakSides, _strongSide = _strongSide,
    };

    /// <summary>Personality group of a ship: "hunters" for the hunter AI, its faction ID, or "pirates" for other ships.</summary>
    public static string Group(Ship ship) => AiGate.IsHunterAi(ship) ? Hunters : GroupOf(ship.Metadata.PlayerIndex);

    /// <summary>Personality group of a ship without the hunter AI, from its allegiance: its faction ID, or "pirates".</summary>
    public static string GroupOf(int playerIndex) => FactionRules.GetFaction(playerIndex)?.ID.ToString() ?? Pirates;

    static ulong Seed(ObjectID shipId) => (uint)shipId;

    /// <summary>The ship's personality: a weighted roll among the switched-on personalities of its group. Null when none matches.</summary>
    public static Personality? Pick(Ship ship, TacticsTunables t) => Pick(Group(ship), ship.UniqueID, t);

    /// <summary>The personality from the group and ship ID only, so it works for a ship in stasis too.</summary>
    public static Personality? Pick(string group, ObjectID shipId, TacticsTunables t)
    {
        var options = t.Personalities.Where(p => p.Groups.Contains(group) && !t.PersonalitiesOff.Contains(p.Name)).ToList();
        int index = PersonalityModel.PickWeighted(options.Select(p => p.Chance).ToList(), PersonalityModel.Unit(Seed(shipId), PersonalityModel.PickSlot));
        return index < 0 ? null : options[index];
    }

    /// <summary>Spec 361. The jittered trait weights (index = Trait value) from the group and ship ID, or null without a personality.</summary>
    public static float[]? Weights(string group, ObjectID shipId, TacticsTunables t) =>
        Pick(group, shipId, t) is { } personality ? PersonalityModel.Jittered(personality.Weights.ToArray(), personality.Jitter, Seed(shipId)) : null;

    /// <summary>The saved build profile, or a live measure when the ship had no AI tick yet. Both read only sim state.</summary>
    public static ShipProfile Profile(Ship ship, TacticsTunables t) =>
        ship.AI?.GetStrategyModule<PersonalityStrategy>() is { _measured: true } m ? m.Stored : Measure(ship, t);

    /// <summary>Spec 1030-1032. The build scores with the side profile. With WeakSideEnabled off, no side counts as weak.</summary>
    public static BuildScores Scores(Ship ship, TacticsTunables t)
    {
        var profile = Profile(ship, t);
        if (!t.WeakSideEnabled) profile = profile with { Sides = default };
        return PersonalityModel.Scores(profile, t.Scale, FlightSide(ship));
    }

    /// <summary>The side profile direction of the ship's flight direction (its designed front).</summary>
    public static int FlightSide(Ship ship) => SideProfile.Index((float)ship.FlightDirection);

    /// <summary>
    /// Spec 1033. A move with the threat in mind, like AIAggroEnemiesModule.TacticalRetreat: plain travel turns the ship's front to
    /// the destination. When that would turn a weak side to the threat, the ship keeps its strong side to the threat and strafes.
    /// Limit: a strafing ship moves slower when its thrusters face the wrong way, and the check uses the threat's position now.
    /// Revisit if guarded ships get caught often while they fly away.
    /// </summary>
    public static void MoveGuarded(Ship ship, Vector2 location, float tolerance, Ship? threat, TacticsTunables t)
    {
        var b = Scores(ship, t);
        if (threat is null || b.Sides.Weak == 0)
        {
            HunterOrders.MoveTo(ship, location, tolerance);
            return;
        }
        float travel = (float)ship.DetWorldCenter.DirectionTo(location);
        float toThreat = (float)ship.DetWorldCenter.DirectionTo(threat.DetWorldCenter);
        int facing = SideProfile.Index(toThreat - travel + (float)ship.FlightDirection);
        if (!b.Sides.IsWeak(facing))
        {
            HunterOrders.MoveTo(ship, location, tolerance);
            return;
        }
        HunterOrders.MoveFacing(ship, location, tolerance, threat, SideProfile.Angle(b.Sides.Strong));
    }

    /// <summary>
    /// Spec 361-363. The traits that the ship uses now. None without a personality.
    /// Limit: computed on every call, without a cache; group checks (pincer, bait, team) call it for each ally. It is a few small
    /// lists and some arithmetic. Revisit with a per-tick cache if AI ticks show up in a profile of a fight with many hostile ships.
    /// </summary>
    public static TraitSet Traits(Ship ship, TacticsTunables t) =>
        Weights(Group(ship), ship.UniqueID, t) is { } weights ? PersonalityModel.Choose(weights, Scores(ship, t), t.PersonalityThreshold) : TraitSet.None;

    /// <summary>Spec 362. Measures the build from the operational thrusters, the parts and the weapons.</summary>
    public static ShipProfile Measure(Ship ship, TacticsTunables t)
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
            Radius: ship.WorldBoundingRadius,
            Sides: MeasureSides(ship, t));
    }

    /// <summary>
    /// Spec 1030-1031. Builds the cover grid from the parts (armor cells weigh ArmorCoverWeight, other cells 1, both × part health),
    /// finds the protected parts and counts the operational weapons that can fire toward each side.
    /// </summary>
    static SideProfile MeasureSides(Ship ship, TacticsTunables t)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var part in ship.Parts)
        {
            var r = part.Rect;
            (minX, minY, maxX, maxY) = (Math.Min(minX, r.X), Math.Min(minY, r.Y), Math.Max(maxX, r.X + r.Width), Math.Max(maxY, r.Y + r.Height));
        }
        if (minX > maxX) return default;
        var cover = new float[maxX - minX, maxY - minY];
        var vulnerable = new List<CellRect>();
        var categories = t.VulnerableParts.Select(c => new ID<PartCategory>(c)).ToList();
        foreach (var part in ship.Parts)
        {
            var r = part.Rect;
            float weight = (part.Rules.TypeCategories.Contains(ArmorCategory) ? t.ArmorCoverWeight : 1f)
                           * part.Health / Math.Max(1, part.Rules.MaxHealth);
            for (int x = r.X; x < r.X + r.Width; x++)
                for (int y = r.Y; y < r.Y + r.Height; y++)
                    cover[x - minX, y - minY] = weight;
            if (categories.Any(part.Rules.TypeCategories.Contains)) vulnerable.Add(new CellRect(r.X, r.Y, r.Width, r.Height));
        }
        var weapons = new int[SideProfile.Directions];
        float scale = Math.Max(1e-3f, ship.WorldUniformScale);
        foreach (var weapon in ship.Weapons.OperationalWeapons)
        {
            if (!weapon.ShouldEstimateFirepower || weapon.Part is not { } part) continue;
            float reach = 0.8f * weapon.GetEffectiveRange(WeaponCoverageMode.Confirmed) / scale;
            for (int i = 0; i < SideProfile.Directions; i++)
                if (weapon.IsInCoverage(part.LocalCenter + Vector2.FromAngle(SideProfile.Angle(i)) * reach, WeaponCoverageMode.Confirmed, checkLOS: true, null))
                    weapons[i]++;
        }
        return PersonalityModel.Sides(cover, minX, minY, vulnerable, weapons, FlightSide(ship), t.WeakSideCover, t.WeaponSideWeight);
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

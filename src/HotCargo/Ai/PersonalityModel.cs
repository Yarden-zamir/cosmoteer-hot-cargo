using System.Numerics;

namespace HotCargo.Ai;

/// <summary>
/// The traits of an AI personality (spec 360-369). Tactics.Tactic uses the same values for the traits that are tactics.
/// Call is a trait but not a tactic: it gates the call for help of spec 196.
/// </summary>
public enum Trait { Kite, Pincer, Disable, FallBack, Bait, Team, Cover, Brawl, Call }

/// <summary>A set of traits as bits.</summary>
public readonly record struct TraitSet(uint Bits)
{
    public static readonly TraitSet None = new(0);
    public bool Has(Trait trait) => (Bits & (1u << (int)trait)) != 0;
    public TraitSet With(Trait trait) => new(Bits | (1u << (int)trait));
    public TraitSet Without(Trait trait) => new(Bits & ~(1u << (int)trait));
    public IEnumerable<Trait> All() => Enum.GetValues<Trait>().Where(Has);
    public override string ToString() => string.Join(",", All());
}

/// <summary>
/// Raw build numbers of one ship (spec 362). Acceleration = thrust over mass. Turn = thruster torque over rotational inertia.
/// Range = longest active weapon range. Armor = armor cells over all part cells. Shields = parts with a shield. Radius = bounding radius.
/// </summary>
public readonly record struct ShipProfile(float Acceleration, float Turn, float Range, float Armor, int Shields, float Radius);

/// <summary>
/// Where each build number reaches 0 and 1 on its score (spec 362). Each pair is (low, high). The shield score is shield parts
/// per unit of radius, from 0 to ShieldsPerRadiusFull, so a big ship needs more shields for the same score.
/// </summary>
public readonly record struct ProfileScale(
    float AccelHeavy, float AccelAgile, float TurnSlow, float TurnFast, float RangeShort, float RangeLong,
    float ArmorLight, float ArmorHeavy, float ShieldsPerRadiusFull, float RadiusSmall, float RadiusLarge);

/// <summary>
/// Build scores, each 0 to 1 (spec 362). Agility 0 means a heavy ship: it never kites or baits.
/// Protection is the larger of the armor and shield scores.
/// </summary>
public readonly record struct BuildScores(float Agility, float Turn, float Range, float Protection, float Size)
{
    public bool Heavy => Agility <= 0f;
}

public enum TeamRole { None, Tank, Sniper, FlankLeft, FlankRight, Line }

/// <summary>Pure personality, build and team math (spec 360-369). No game types.</summary>
public static class PersonalityModel
{
    /// <summary>Linear score: 0 at or below low, 1 at or above high.</summary>
    public static float Score(float value, float low, float high) =>
        high <= low ? (value >= high ? 1f : 0f) : Math.Clamp((value - low) / (high - low), 0f, 1f);

    public static BuildScores Scores(ShipProfile p, ProfileScale s) => new(
        Agility: Score(p.Acceleration, s.AccelHeavy, s.AccelAgile),
        Turn: Score(p.Turn, s.TurnSlow, s.TurnFast),
        Range: Score(p.Range, s.RangeShort, s.RangeLong),
        Protection: Math.Max(Score(p.Armor, s.ArmorLight, s.ArmorHeavy), Score(p.Radius > 0f ? p.Shields / p.Radius : 0f, 0f, s.ShieldsPerRadiusFull)),
        Size: Score(p.Radius, s.RadiusSmall, s.RadiusLarge));

    /// <summary>
    /// Spec 363. How well a build suits a trait, 0 to 1. Heavy ships never kite or bait. Fast light ships suit kiting, bait and
    /// flanking. Long range suits kiting. Short range and protection suit brawling. Unprotected small ships suit cover.
    /// </summary>
    public static float Fit(Trait trait, BuildScores b) => trait switch
    {
        Trait.Kite => b.Heavy ? 0f : (0.5f + 0.5f * b.Agility) * (0.3f + 0.7f * b.Range),
        Trait.Bait => b.Heavy ? 0f : b.Agility * (1f - 0.5f * b.Size),
        Trait.Pincer => 0.3f + 0.35f * (b.Agility + b.Turn),
        Trait.Brawl => (1f - b.Range) * (0.5f + 0.5f * b.Protection),
        Trait.Cover => (1f - 0.7f * b.Protection) * (1f - 0.6f * b.Size),
        Trait.Disable or Trait.FallBack or Trait.Team or Trait.Call => 1f,
        _ => throw new ArgumentOutOfRangeException(nameof(trait), trait, null),
    };

    /// <summary>SplitMix64 finalizer: a well spread 64-bit hash.</summary>
    public static ulong Mix(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    /// <summary>A number in [0, 1) from a seed and a slot. The same seed and slot always give the same number.</summary>
    public static float Unit(ulong seed, int slot) => (Mix(seed ^ Mix((ulong)slot + 1)) >> 40) / (float)(1UL << 24);

    /// <summary>Slot of the personality roll. The trait jitters use the slots 0 to 8 (the Trait values).</summary>
    public const int PickSlot = 64;

    /// <summary>Index of a weighted pick: u in [0, 1) walks the cumulative chances. -1 when no chance is above 0.</summary>
    public static int PickWeighted(IReadOnlyList<float> chances, float u)
    {
        float total = 0f;
        foreach (float c in chances) total += Math.Max(0f, c);
        if (total <= 0f) return -1;
        float at = u * total;
        int last = -1;
        for (int i = 0; i < chances.Count; i++)
        {
            if (chances[i] <= 0f) continue;
            last = i;
            at -= chances[i];
            if (at < 0f) return i;
        }
        return last;
    }

    /// <summary>
    /// Spec 361. The weight of each trait for one ship: the personality weight plus jitter × a number in [-1, 1) from the seed,
    /// clamped to 0..1. Index = Trait value.
    /// </summary>
    public static float[] Jittered(IReadOnlyList<float> weights, float jitter, ulong seed)
    {
        var result = new float[weights.Count];
        for (int i = 0; i < weights.Count; i++)
            result[i] = Math.Clamp(weights[i] + jitter * (2f * Unit(seed, i) - 1f), 0f, 1f);
        return result;
    }

    /// <summary>
    /// Spec 361-363. The traits that a ship uses: each trait with weight × build fit at or above the threshold.
    /// Brawl excludes Kite and Cover: the higher score wins.
    /// </summary>
    public static TraitSet Choose(IReadOnlyList<float> weights, BuildScores b, float threshold)
    {
        var traits = Enum.GetValues<Trait>();
        if (weights.Count != traits.Length) throw new ArgumentException($"need {traits.Length} trait weights, got {weights.Count}", nameof(weights));
        var score = new float[traits.Length];
        var set = TraitSet.None;
        foreach (var trait in traits)
        {
            score[(int)trait] = weights[(int)trait] * Fit(trait, b);
            if (score[(int)trait] >= threshold) set = set.With(trait);
        }
        if (set.Has(Trait.Brawl) && (set.Has(Trait.Kite) || set.Has(Trait.Cover)))
        {
            float far = Math.Max(set.Has(Trait.Kite) ? score[(int)Trait.Kite] : 0f, set.Has(Trait.Cover) ? score[(int)Trait.Cover] : 0f);
            set = score[(int)Trait.Brawl] > far ? set.Without(Trait.Kite).Without(Trait.Cover) : set.Without(Trait.Brawl);
        }
        return set;
    }

    /// <summary>
    /// Spec 365. Roles in a team, in member order (callers sort members by ID). The tank is the most protected and largest ship.
    /// The sniper is the longest-range other ship with a range score of at least 0.5. Other ships with an agility score of at
    /// least 0.5 flank, alternating left and right. The rest hold the line. Ties go to the first member.
    /// </summary>
    public static TeamRole[] AssignRoles(IReadOnlyList<BuildScores> members)
    {
        var roles = new TeamRole[members.Count];
        if (members.Count < 2) return roles;
        int tank = ArgMax(members, m => m.Protection + m.Size, _ => true);
        roles[tank] = TeamRole.Tank;
        int sniper = ArgMax(members, m => m.Range, i => i != tank && members[i].Range >= 0.5f);
        if (sniper >= 0) roles[sniper] = TeamRole.Sniper;
        int flankers = 0;
        for (int i = 0; i < members.Count; i++)
        {
            if (roles[i] != TeamRole.None) continue;
            roles[i] = members[i].Agility >= 0.5f ? (flankers++ % 2 == 0 ? TeamRole.FlankLeft : TeamRole.FlankRight) : TeamRole.Line;
        }
        return roles;
    }

    static int ArgMax(IReadOnlyList<BuildScores> members, Func<BuildScores, float> value, Func<int, bool> allowed)
    {
        int best = -1;
        for (int i = 0; i < members.Count; i++)
            if (allowed(i) && (best < 0 || value(members[i]) > value(members[best]))) best = i;
        return best;
    }

    public enum CoverPhase { Peek, Hide }

    /// <summary>Spec 366. A covering ship fights (peeks) for peekSeconds, then hides for hideSeconds, and repeats.</summary>
    public static CoverPhase NextCoverPhase(CoverPhase phase, double phaseSeconds, float peekSeconds, float hideSeconds) => phase switch
    {
        CoverPhase.Peek when phaseSeconds >= peekSeconds => CoverPhase.Hide,
        CoverPhase.Hide when phaseSeconds >= hideSeconds => CoverPhase.Peek,
        _ => phase,
    };

    /// <summary>Spec 366. The hiding spot behind a rock: on the far side from the threat, touching the rock plus a gap.</summary>
    public static Vector2 CoverSpot(Vector2 threat, Vector2 rock, float rockRadius, float shipRadius, float gap)
    {
        Vector2 away = rock - threat;
        float length = away.Length();
        // A threat on the rock's center: any side hides equally badly. Pick +X so the result stays defined.
        Vector2 dir = length > 1e-3f ? away / length : Vector2.UnitX;
        return rock + dir * (rockRadius + shipRadius + gap);
    }

    /// <summary>True when the segment from a to b passes through the circle. For the cover check (line of sight through a rock).</summary>
    public static bool SegmentHitsCircle(Vector2 a, Vector2 b, Vector2 center, float radius)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        float t = lengthSquared <= 0f ? 0f : Math.Clamp(Vector2.Dot(center - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.DistanceSquared(a + ab * t, center) <= radius * radius;
    }
}

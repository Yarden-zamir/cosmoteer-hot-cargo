using System.Numerics;
using HotCargo.Ai;
using Xunit;

namespace HotCargo.Tests;

public class PersonalityModelTests
{
    static readonly ProfileScale Scale = new(AccelHeavy: 4, AccelAgile: 14, TurnSlow: 0.5f, TurnFast: 3, RangeShort: 200, RangeLong: 500,
        ArmorLight: 0.1f, ArmorHeavy: 0.3f, ShieldsPerRadiusFull: 0.25f, RadiusSmall: 15, RadiusLarge: 60);

    static float[] All(float w) => Enum.GetValues<Trait>().Select(_ => w).ToArray();

    [Fact]
    public void Scores_map_each_build_number_between_its_ends()
    {
        var b = PersonalityModel.Scores(new ShipProfile(Acceleration: 9, Turn: 3, Range: 100, Armor: 0.2f, Shields: 3, Radius: 37.5f), Scale);
        Assert.Equal(0.5f, b.Agility, 3);
        Assert.Equal(1f, b.Turn, 3);
        Assert.Equal(0f, b.Range, 3);
        Assert.Equal(0.5f, b.Protection, 3); // armor 0.2 is half way; 3 shields over radius 37.5 give 0.32
        Assert.Equal(0.5f, b.Size, 3);
        // The same shields on a ship of radius 12 give a full shield score.
        Assert.Equal(1f, PersonalityModel.Scores(new ShipProfile(9, 3, 100, 0f, 3, 12f), Scale).Protection, 3);
        Assert.True(PersonalityModel.Scores(new ShipProfile(4, 1, 300, 0, 0, 20), Scale).Heavy);
        Assert.False(PersonalityModel.Scores(new ShipProfile(4.1f, 1, 300, 0, 0, 20), Scale).Heavy);
    }

    [Fact]
    public void Heavy_ships_never_kite_or_bait_whatever_the_weights()
    {
        var heavy = new BuildScores(Agility: 0, Turn: 1, Range: 1, Protection: 0, Size: 0);
        var set = PersonalityModel.Choose(All(1f), heavy, threshold: 0.01f);
        Assert.False(set.Has(Trait.Kite));
        Assert.False(set.Has(Trait.Bait));
        Assert.True(set.Has(Trait.Team));
    }

    [Fact]
    public void Builds_pick_their_styles_fast_long_range_kites_slow_armored_short_range_brawls_unprotected_small_takes_cover()
    {
        float[] weights = All(0.8f);
        var skirmisher = PersonalityModel.Choose(weights, new BuildScores(1, 1, 1, 0, 0), 0.45f);
        Assert.True(skirmisher.Has(Trait.Kite));
        Assert.True(skirmisher.Has(Trait.Bait));
        Assert.False(skirmisher.Has(Trait.Brawl));

        var brick = PersonalityModel.Choose(weights, new BuildScores(0.1f, 0.1f, 0, 1, 1), 0.45f);
        Assert.True(brick.Has(Trait.Brawl));
        Assert.False(brick.Has(Trait.Kite));
        Assert.False(brick.Has(Trait.Cover));

        var scout = PersonalityModel.Choose(weights, new BuildScores(0.6f, 0.5f, 0.3f, 0, 0), 0.45f);
        Assert.True(scout.Has(Trait.Cover));
    }

    [Fact]
    public void Brawl_and_ranged_styles_exclude_each_other_and_the_higher_score_wins()
    {
        // Short range, unprotected, small: brawl fit 0.5 and cover fit 1. Cover wins with equal weights.
        var b = new BuildScores(0.5f, 0.5f, 0, 0, 0);
        var set = PersonalityModel.Choose(All(1f), b, 0.3f);
        Assert.True(set.Has(Trait.Cover));
        Assert.False(set.Has(Trait.Brawl));
        // With cover weighted down, brawl wins and cover goes.
        float[] w = All(1f);
        w[(int)Trait.Cover] = 0.35f;
        w[(int)Trait.Kite] = 0f;
        set = PersonalityModel.Choose(w, b, 0.3f);
        Assert.True(set.Has(Trait.Brawl));
        Assert.False(set.Has(Trait.Cover));
    }

    [Fact]
    public void Jitter_is_deterministic_per_seed_bounded_and_differs_between_seeds()
    {
        float[] w = All(0.5f);
        Assert.Equal(PersonalityModel.Jittered(w, 0.3f, 42), PersonalityModel.Jittered(w, 0.3f, 42));
        Assert.NotEqual(PersonalityModel.Jittered(w, 0.3f, 42), PersonalityModel.Jittered(w, 0.3f, 43));
        for (ulong seed = 0; seed < 200; seed++)
            Assert.All(PersonalityModel.Jittered(w, 0.3f, seed), x => Assert.InRange(x, 0.2f, 0.8f));
        Assert.Equal(w, PersonalityModel.Jittered(w, 0f, 7));
        Assert.All(PersonalityModel.Jittered(All(1f), 1f, 9), x => Assert.InRange(x, 0f, 1f));
    }

    [Fact]
    public void Weighted_pick_follows_the_chances()
    {
        float[] chances = [2, 0, 1];
        Assert.Equal(0, PersonalityModel.PickWeighted(chances, 0f));
        Assert.Equal(0, PersonalityModel.PickWeighted(chances, 0.66f));
        Assert.Equal(2, PersonalityModel.PickWeighted(chances, 0.67f));
        Assert.Equal(2, PersonalityModel.PickWeighted(chances, 0.9999f));
        Assert.Equal(-1, PersonalityModel.PickWeighted([0f, 0f], 0.5f));
        Assert.Equal(-1, PersonalityModel.PickWeighted([], 0.5f));
        // Over many seeds the split is close to 2:1.
        int first = Enumerable.Range(0, 3000).Count(i => PersonalityModel.PickWeighted(chances, PersonalityModel.Unit((ulong)i, PersonalityModel.PickSlot)) == 0);
        Assert.InRange(first, 1850, 2150);
    }

    [Fact]
    public void Team_roles_give_one_tank_one_sniper_and_alternating_flankers()
    {
        var roles = PersonalityModel.AssignRoles(
        [
            new BuildScores(Agility: 0.9f, Turn: 1, Range: 0.2f, Protection: 0.1f, Size: 0.1f), // fast: flanker
            new BuildScores(Agility: 0.1f, Turn: 0, Range: 0.3f, Protection: 1f, Size: 0.8f),   // armored and big: tank
            new BuildScores(Agility: 0.3f, Turn: 0, Range: 0.9f, Protection: 0.2f, Size: 0.3f), // long range: sniper
            new BuildScores(Agility: 0.7f, Turn: 1, Range: 0.4f, Protection: 0f, Size: 0.2f),   // fast: second flanker
            new BuildScores(Agility: 0.2f, Turn: 0, Range: 0.4f, Protection: 0.3f, Size: 0.3f), // slow: line
        ]);
        Assert.Equal([TeamRole.FlankLeft, TeamRole.Tank, TeamRole.Sniper, TeamRole.FlankRight, TeamRole.Line], roles);
        Assert.Equal([TeamRole.None], PersonalityModel.AssignRoles([new BuildScores(1, 1, 1, 1, 1)]));
        // Nobody with long range: no sniper.
        var pair = PersonalityModel.AssignRoles([new BuildScores(0.2f, 0, 0.1f, 0.5f, 0.5f), new BuildScores(0.2f, 0, 0.4f, 0.1f, 0.1f)]);
        Assert.Equal([TeamRole.Tank, TeamRole.Line], pair);
    }

    [Fact]
    public void Cover_spot_is_behind_the_rock_and_the_rock_blocks_the_line()
    {
        var threat = new Vector2(0, 0);
        var rock = new Vector2(100, 0);
        var spot = PersonalityModel.CoverSpot(threat, rock, rockRadius: 30, shipRadius: 20, gap: 10);
        Assert.Equal(new Vector2(160, 0), spot);
        Assert.True(PersonalityModel.SegmentHitsCircle(threat, spot, rock, 30));
        Assert.False(PersonalityModel.SegmentHitsCircle(threat, new Vector2(100, 80), rock, 30));
        Assert.False(PersonalityModel.SegmentHitsCircle(threat, new Vector2(50, 0), rock, 30));
        // A threat on the rock still gives a defined spot.
        Assert.Equal(new Vector2(160, 0), PersonalityModel.CoverSpot(rock, rock, 30, 20, 10));
    }

    [Fact]
    public void Cover_cycles_between_peek_and_hide()
    {
        Assert.Equal(PersonalityModel.CoverPhase.Peek, PersonalityModel.NextCoverPhase(PersonalityModel.CoverPhase.Peek, 7.9, 8, 6));
        Assert.Equal(PersonalityModel.CoverPhase.Hide, PersonalityModel.NextCoverPhase(PersonalityModel.CoverPhase.Peek, 8, 8, 6));
        Assert.Equal(PersonalityModel.CoverPhase.Hide, PersonalityModel.NextCoverPhase(PersonalityModel.CoverPhase.Hide, 5.9, 8, 6));
        Assert.Equal(PersonalityModel.CoverPhase.Peek, PersonalityModel.NextCoverPhase(PersonalityModel.CoverPhase.Hide, 6, 8, 6));
    }

    /// <summary>A grid of w × h part cells with weight 1, armor (weight 3) in the listed columns.</summary>
    static float[,] Grid(int w, int h, params int[] armorColumns)
    {
        var g = new float[w, h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                g[x, y] = armorColumns.Contains(x) ? 3f : 1f;
        return g;
    }

    static readonly int[] NoWeapons = new int[SideProfile.Directions];

    [Fact]
    public void Cockpit_at_the_back_makes_the_back_weak_and_the_armored_front_strong()
    {
        // Flight to +X (direction 0). Cockpit at the back edge (x 0), armor in the front column (x 4).
        var sides = PersonalityModel.Sides(Grid(5, 3, 4), 0, 0, [new CellRect(0, 1, 1, 1)], NoWeapons, flight: 0, weakCover: 4, weaponWeight: 1);
        Assert.True(sides.IsWeak(4));
        Assert.False(sides.IsWeak(0));
        Assert.Equal(0, sides.Strong);
        var b = new BuildScores(Agility: 1, Turn: 1, Range: 1, Protection: 0, Size: 0, Sides: sides, Flight: 0);
        Assert.True(b.WeakBack);
        Assert.False(b.NoGuardedSide);
        Assert.Equal(0f, PersonalityModel.Fit(Trait.Kite, b));
        Assert.True(PersonalityModel.Fit(Trait.Bait, b) > 0f);
    }

    [Fact]
    public void Buried_cockpit_has_no_weak_side_and_no_vulnerable_part_means_no_weak_side()
    {
        var buried = PersonalityModel.Sides(Grid(7, 7, 0, 6), 0, 0, [new CellRect(3, 3, 1, 1)], NoWeapons, flight: 2, weakCover: 3, weaponWeight: 1);
        Assert.Equal(0, buried.Weak);
        var none = PersonalityModel.Sides(Grid(3, 3), 0, 0, [], NoWeapons, flight: 2, weakCover: 3, weaponWeight: 1);
        Assert.Equal(0, none.Weak);
        Assert.Equal(2, none.Strong); // ties go to the flight direction
    }

    [Fact]
    public void Weapons_pick_between_guarded_sides_and_an_exposed_ship_has_no_guarded_side()
    {
        var weapons = new int[SideProfile.Directions];
        weapons[4] = 3;
        var sides = PersonalityModel.Sides(Grid(7, 7), 0, 0, [new CellRect(3, 3, 1, 1)], weapons, flight: 0, weakCover: 2, weaponWeight: 1);
        Assert.Equal(4, sides.Strong);
        var bare = PersonalityModel.Sides(Grid(1, 1), 0, 0, [new CellRect(0, 0, 1, 1)], NoWeapons, flight: 0, weakCover: 1, weaponWeight: 1);
        Assert.Equal(0xFF, bare.Weak);
        Assert.Equal(0f, PersonalityModel.Fit(Trait.Bait, new BuildScores(1, 1, 1, 0, 0, bare)));
        Assert.Equal(4, SideProfile.Index(MathF.PI));
        Assert.Equal(6, SideProfile.Index(-MathF.PI / 2));
    }
}

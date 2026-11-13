using HotCargo.ShipAi;
using Xunit;

namespace HotCargo.Tests;

public class ShipAiModelTests
{
    static ShipFacts Facts(float health = 1f, float? shields = null, float nearest = float.PositiveInfinity, int enemies = 0,
        bool targeted = false, bool allyHit = false, float? ammo = null, float? battery = null, bool tech = false, bool vaultHit = false,
        PartKind[]? lost = null, ShipClass[]? classes = null) =>
        new(health, shields, new HashSet<PartKind>(lost ?? []), nearest, targeted, allyHit, ammo, battery,
            new HashSet<ShipClass>(classes ?? []), enemies, tech, vaultHit);

    static TacticRow Row(Cond c, int cp, Act a, int ap = 0) => new(c, cp, a, ap);

    [Fact]
    public void First_matching_row_of_each_kind_wins_and_later_rows_of_that_kind_are_ignored()
    {
        TacticRow[] rows =
        [
            Row(Cond.HealthBelow, 30, Act.RetreatRepair),
            Row(Cond.Always, 0, Act.CloseIn),
            Row(Cond.Always, 0, Act.TargetWeakest),
            Row(Cond.Always, 0, Act.TargetNearest),
            Row(Cond.Always, 0, Act.Kite),
        ];
        var hurt = ShipAiModel.Evaluate(rows, Facts(health: 0.2f), _ => true);
        Assert.Equal(0, hurt.MoveRow);
        Assert.Equal(2, hurt.TargetRow);
        var fine = ShipAiModel.Evaluate(rows, Facts(health: 0.9f), _ => true);
        Assert.Equal(1, fine.MoveRow);
        Assert.Equal([false, true, true, true, true], fine.Matched);
    }

    [Fact]
    public void A_row_whose_action_cannot_run_is_skipped_like_a_false_condition()
    {
        TacticRow[] rows = [Row(Cond.Always, 0, Act.TargetAllyAttacker), Row(Cond.Always, 0, Act.TargetNearest)];
        var d = ShipAiModel.Evaluate(rows, Facts(), r => r.Act != Act.TargetAllyAttacker);
        Assert.Equal(1, d.TargetRow);
        Assert.False(d.Matched[0]);
    }

    [Fact]
    public void Can_run_is_asked_only_for_rows_whose_condition_holds()
    {
        var asked = new List<Act>();
        TacticRow[] rows = [Row(Cond.TargetedByEnemy, 0, Act.Kite), Row(Cond.Always, 0, Act.CloseIn)];
        ShipAiModel.Evaluate(rows, Facts(), r => { asked.Add(r.Act); return true; });
        Assert.Equal([Act.CloseIn], asked);
    }

    [Theory]
    [InlineData(Cond.HealthBelow, 50, true)]
    [InlineData(Cond.HealthBelow, 40, false)]
    [InlineData(Cond.ShieldsBelow, 30, true)]
    [InlineData(Cond.EnemyWithin, 800, true)]
    [InlineData(Cond.EnemyWithin, 799, false)]
    [InlineData(Cond.EnemyBeyond, 500, true)]
    [InlineData(Cond.EnemiesAtLeast, 3, true)]
    [InlineData(Cond.EnemiesAtLeast, 4, false)]
    [InlineData(Cond.AmmoBelow, 10, false)]
    [InlineData(Cond.BatteryBelow, 60, true)]
    [InlineData(Cond.TargetedByEnemy, 0, true)]
    [InlineData(Cond.AllyUnderAttack, 0, false)]
    [InlineData(Cond.CarryingTech, 0, true)]
    [InlineData(Cond.VaultDamaged, 0, false)]
    [InlineData(Cond.PartLost, (int)PartKind.Thruster, true)]
    [InlineData(Cond.PartLost, (int)PartKind.Weapon, false)]
    [InlineData(Cond.EnemyClassNear, (int)ShipClass.Station, true)]
    [InlineData(Cond.EnemyClassNear, (int)ShipClass.Large, false)]
    public void Conditions_read_their_fact(Cond cond, int param, bool expected)
    {
        var f = Facts(health: 0.45f, shields: 0.2f, nearest: 800f, enemies: 3, targeted: true, ammo: 0.5f, battery: 0.5f, tech: true,
            lost: [PartKind.Thruster], classes: [ShipClass.Station, ShipClass.Small]);
        Assert.Equal(expected, ShipAiModel.Holds(new Term(cond, param), f));
    }

    [Fact]
    public void Missing_shields_ammo_or_battery_never_count_as_low_and_no_enemy_is_infinitely_far()
    {
        var f = Facts();
        Assert.False(ShipAiModel.Holds(new Term(Cond.ShieldsBelow, 100), f));
        Assert.False(ShipAiModel.Holds(new Term(Cond.AmmoBelow, 100), f));
        Assert.False(ShipAiModel.Holds(new Term(Cond.BatteryBelow, 100), f));
        Assert.True(ShipAiModel.Holds(new Term(Cond.EnemyBeyond, 20000), f));
        Assert.False(ShipAiModel.Holds(new Term(Cond.EnemyWithin, 20000), f));
    }

    [Fact]
    public void Weapon_act_takes_the_first_matched_row_of_the_weapons_groups_or_all()
    {
        TacticRow[] rows =
        [
            Row(Cond.Always, 0, Act.HoldFire, 2),
            Row(Cond.Always, 0, Act.TargetEngines, 0),
            Row(Cond.Always, 0, Act.PointDefenseOnly, 3),
        ];
        bool[] all = [true, true, true];
        Assert.Equal(Act.HoldFire, ShipAiModel.WeaponAct(rows, all, new HashSet<int> { 2 }));
        Assert.Equal(Act.TargetEngines, ShipAiModel.WeaponAct(rows, all, new HashSet<int> { 3 }));
        Assert.Equal(Act.TargetEngines, ShipAiModel.WeaponAct(rows, all, new HashSet<int>()));
        Assert.Equal(Act.PointDefenseOnly, ShipAiModel.WeaponAct(rows, [true, false, true], new HashSet<int> { 3 }));
        Assert.Equal(Act.FireAtWill, ShipAiModel.WeaponAct(rows, [false, false, false], new HashSet<int> { 2 }));
    }

    [Fact]
    public void Text_round_trips_every_preset_and_every_condition_and_action()
    {
        foreach (var p in Enum.GetValues<Preset>())
        {
            var rows = ShipAiModel.PresetRows(p);
            Assert.Equal(rows, ShipAiModel.Decode(ShipAiModel.Encode(rows)));
        }
        var every = Enum.GetValues<Cond>().Select((c, i) =>
                ShipAiModel.WithAct(new TacticRow([ShipAiModel.WithCond(new Term(Cond.Always, 0), c)], Act.TargetNearest, 0), Enum.GetValues<Act>()[i % Enum.GetValues<Act>().Length]))
            .ToList();
        foreach (var chunk in every.Chunk(ShipAiModel.MaxRows)) Assert.Equal(chunk, ShipAiModel.Decode(ShipAiModel.Encode(chunk)));
        var acts = Enum.GetValues<Act>().Select(a => ShipAiModel.WithAct(new TacticRow(Cond.Always, 0, Act.TargetNearest, 0), a)).Take(ShipAiModel.MaxRows).ToList();
        Assert.Equal(acts, ShipAiModel.Decode(ShipAiModel.Encode(acts)));
        Assert.Equal("", ShipAiModel.Encode([]));
        Assert.Empty(ShipAiModel.Decode(""));
        Assert.Equal("HC1:HealthBelow=30>RetreatRepair;Always>HoldFire=2",
            ShipAiModel.Encode([Row(Cond.HealthBelow, 30, Act.RetreatRepair), Row(Cond.Always, 0, Act.HoldFire, 2)]));
    }

    [Theory]
    [InlineData("Always>Kite")]
    [InlineData("HC1:Always")]
    [InlineData("HC1:Sometimes>Kite")]
    [InlineData("HC1:HealthBelow>Kite")]
    [InlineData("HC1:HealthBelow=101>Kite")]
    [InlineData("HC1:Always=3>Kite")]
    [InlineData("HC1:Always>HoldFire=11")]
    [InlineData("HC1:Always>3")]
    [InlineData("HC1:healthbelow=10>Kite")]
    public void Bad_text_throws(string text) => Assert.Throws<FormatException>(() => ShipAiModel.Decode(text));

    [Fact]
    public void Too_many_rows_throw()
    {
        var rows = Enumerable.Repeat(Row(Cond.Always, 0, Act.Kite), ShipAiModel.MaxRows + 1).ToList();
        Assert.Throws<FormatException>(() => ShipAiModel.Decode(ShipAiModel.Encode(rows)));
    }

    [Fact]
    public void Every_action_has_a_kind_and_weapon_actions_take_a_group()
    {
        Assert.Equal(ActKind.Target, ShipAiModel.KindOf(Act.TargetVaultAttacker));
        Assert.Equal(ActKind.Move, ShipAiModel.KindOf(Act.Kite));
        Assert.Equal(ActKind.Move, ShipAiModel.KindOf(Act.Guard));
        Assert.Equal(ActKind.Weapons, ShipAiModel.KindOf(Act.FireAtWill));
        Assert.Equal(ActKind.Target, ShipAiModel.KindOf(Act.TargetCaller));
        Assert.Equal(ActKind.Move, ShipAiModel.KindOf(Act.CollectTech));
        Assert.Equal(ActKind.Weapons, ShipAiModel.KindOf(Act.FireDisruptors));
        Assert.Equal(ActKind.Facing, ShipAiModel.KindOf(Act.Face));
        foreach (var a in Enum.GetValues<Act>())
            Assert.Equal(ShipAiModel.KindOf(a) == ActKind.Weapons, ShipAiModel.ParamOf(a) == ParamKind.Group);
    }

    [Fact]
    public void Changing_a_condition_keeps_the_parameter_only_when_its_kind_stays()
    {
        var term = new Term(Cond.HealthBelow, 55, Not: true, Or: true);
        Assert.Equal(55, ShipAiModel.WithCond(term, Cond.ShieldsBelow).Param);
        Assert.Equal(1000, ShipAiModel.WithCond(term, Cond.EnemyWithin).Param);
        Assert.Equal(new Term(Cond.Always, 0, true, true), ShipAiModel.WithCond(term, Cond.Always));
    }

    // ---- Condition chains (spec 416, 417)

    static Term T(Cond c, int p = 0, bool not = false, bool or = false) => new(c, p, not, or);

    static bool Chain(ShipFacts f, params Term[] terms) =>
        ShipAiModel.Evaluate([new TacticRow(terms, Act.Kite, 0)], f, _ => true).Matched[0];

    [Fact]
    public void And_binds_tighter_than_or()
    {
        var f = Facts(targeted: true, tech: true);
        Term yes = T(Cond.TargetedByEnemy), yes2 = T(Cond.CarryingTech), no = T(Cond.AllyUnderAttack), no2 = T(Cond.VaultDamaged);
        // yes OR yes2 AND no = yes or (yes2 and no) = true. Left to right, (yes or yes2) and no would be false.
        Assert.True(Chain(f, yes, yes2 with { Or = true }, no));
        // yes AND no OR no2 AND yes2 = (yes and no) or (no2 and yes2) = false.
        Assert.False(Chain(f, yes, no, no2 with { Or = true }, yes2));
        // no AND no2 OR yes AND yes2 = true.
        Assert.True(Chain(f, no, no2, yes with { Or = true }, yes2));
        // The connector of the first term means nothing.
        Assert.False(Chain(f, no with { Or = true }));
    }

    [Fact]
    public void Not_inverts_one_term()
    {
        var f = Facts(health: 0.5f);
        Assert.True(Chain(f, T(Cond.HealthBelow, 60), T(Cond.CarryingTech, not: true)));
        Assert.False(Chain(f, T(Cond.HealthBelow, 60, not: true)));
        Assert.True(Chain(f, T(Cond.EnemiesAtLeast, 1, not: true)));
    }

    [Fact]
    public void A_long_chain_follows_or_of_and_groups()
    {
        var f = Facts(health: 0.2f, enemies: 2, nearest: 500f);
        // false AND true OR false OR true AND true AND NOT false
        Term[] chain =
        [
            T(Cond.CarryingTech), T(Cond.HealthBelow, 30),
            T(Cond.VaultDamaged, or: true),
            T(Cond.EnemiesAtLeast, 2, or: true), T(Cond.EnemyWithin, 800), T(Cond.AllyUnderAttack, not: true),
        ];
        Assert.True(Chain(f, chain));
        chain[5] = T(Cond.TargetedByEnemy);
        Assert.False(Chain(f, chain));
    }

    [Fact]
    public void Chain_text_round_trips_and_old_text_stays_the_same()
    {
        const string old = "HC1:HealthBelow=30>RetreatRepair;Always>HoldFire=2;EnemyWithin=1500>TargetNearest";
        Assert.Equal(old, ShipAiModel.Encode(ShipAiModel.Decode(old)));
        Assert.Equal(new TacticRow(Cond.HealthBelow, 30, Act.RetreatRepair, 0), ShipAiModel.Decode(old)[0]);
        const string chain = "HC1:!CarryingTech&HealthBelow=40|HeatStageAtLeast=3&!EnemyHoldsTech>CollectTech;StrikeWithin=30|LossTimerRunning>Face=1";
        var rows = ShipAiModel.Decode(chain);
        Assert.Equal(chain, ShipAiModel.Encode(rows));
        Assert.Equal([T(Cond.CarryingTech, not: true), T(Cond.HealthBelow, 40), T(Cond.HeatStageAtLeast, 3, or: true), T(Cond.EnemyHoldsTech, not: true)],
            rows[0].Terms);
        Assert.Equal(Side.BroadsideLeft, (Side)rows[1].ActParam);
    }

    [Theory]
    [InlineData("HC1:&Always>Kite")]
    [InlineData("HC1:Always&>Kite")]
    [InlineData("HC1:Always||Always>Kite")]
    [InlineData("HC1:!!Always>Kite")]
    [InlineData("HC1:Always!>Kite")]
    [InlineData("HC1:HeatStageAtLeast=0>Kite")]
    [InlineData("HC1:Always>Face=4")]
    public void Bad_chain_text_throws(string text) => Assert.Throws<FormatException>(() => ShipAiModel.Decode(text));

    [Fact]
    public void Too_many_terms_throw_and_add_term_stops_at_the_limit()
    {
        var row = new TacticRow(Cond.Always, 0, Act.Kite, 0);
        for (int i = 0; i < 20; i++) row = ShipAiModel.AddTerm(row);
        Assert.Equal(ShipAiModel.MaxTerms, row.Terms.Length);
        string text = ShipAiModel.Encode([row]).Replace(">Kite", "&Always>Kite");
        Assert.Throws<FormatException>(() => ShipAiModel.Decode(text));
        Assert.Single(ShipAiModel.RemoveTerm(new TacticRow(Cond.Always, 0, Act.Kite, 0), 0).Terms);
    }

    [Theory]
    [InlineData(Cond.HeatStageAtLeast, 3, true)]
    [InlineData(Cond.HeatStageAtLeast, 4, false)]
    [InlineData(Cond.HuntersInSystem, 0, true)]
    [InlineData(Cond.StrikeWithin, 20, true)]
    [InlineData(Cond.StrikeWithin, 19, false)]
    [InlineData(Cond.LossTimerRunning, 0, false)]
    [InlineData(Cond.CratesWithin, 400, true)]
    [InlineData(Cond.CratesWithin, 399, false)]
    [InlineData(Cond.EnemyHoldsTech, 0, true)]
    [InlineData(Cond.EnemyCalling, 0, false)]
    public void Hot_cargo_conditions_read_their_fact(Cond cond, int param, bool expected)
    {
        var f = Facts() with { HeatStage = 3, HuntersInSystem = true, StrikeIn = 20f, NearestCrate = 400f, EnemyHoldsTech = true };
        Assert.Equal(expected, ShipAiModel.Holds(new Term(cond, param), f));
        Assert.False(ShipAiModel.Holds(new Term(Cond.StrikeWithin, 3600), Facts()));
    }

    [Fact]
    public void Disruptor_fire_rows_apply_only_to_disruptor_weapons()
    {
        TacticRow[] rows = [Row(Cond.Always, 0, Act.FireDisruptors, 0), Row(Cond.Always, 0, Act.FocusTarget, 0)];
        Assert.Equal(Act.FireDisruptors, ShipAiModel.WeaponAct(rows, [true, true], new HashSet<int>(), disruptor: true));
        Assert.Equal(Act.FocusTarget, ShipAiModel.WeaponAct(rows, [true, true], new HashSet<int>(), disruptor: false));
    }

    [Fact]
    public void First_matching_facing_row_wins()
    {
        TacticRow[] rows = [Row(Cond.CarryingTech, 0, Act.Face, (int)Side.Stern), Row(Cond.Always, 0, Act.Face, (int)Side.BroadsideRight)];
        Assert.Equal(1, ShipAiModel.Evaluate(rows, Facts(), _ => true).FacingRow);
        Assert.Equal(0, ShipAiModel.Evaluate(rows, Facts(tech: true), _ => true).FacingRow);
    }

    // ---- Hysteresis (spec 418)

    [Fact]
    public void A_threshold_term_that_holds_keeps_holding_inside_the_margin()
    {
        TacticRow[] rows = [Row(Cond.HealthBelow, 30, Act.RetreatRepair), Row(Cond.EnemyWithin, 1000, Act.CloseIn)];
        var h = new Hysteresis(0.03f, 0);
        var d = ShipAiModel.Evaluate(rows, Facts(health: 0.29f, nearest: 1000f), _ => true, null, 0, h);
        Assert.Equal([true, true], d.Matched);
        // 32 % health is within 3 points; 1020 m is within 3 %.
        d = ShipAiModel.Evaluate(rows, Facts(health: 0.32f, nearest: 1020f), _ => true, d.Memory, 1, h);
        Assert.Equal([true, true], d.Matched);
        d = ShipAiModel.Evaluate(rows, Facts(health: 0.34f, nearest: 1040f), _ => true, d.Memory, 2, h);
        Assert.Equal([false, false], d.Matched);
        // Off again, it needs the plain threshold: 31 % does not turn it on.
        d = ShipAiModel.Evaluate(rows, Facts(health: 0.31f, nearest: 1010f), _ => true, d.Memory, 3, h);
        Assert.Equal([false, false], d.Matched);
        // Without memory there is no margin.
        Assert.Equal([false, false], ShipAiModel.Evaluate(rows, Facts(health: 0.32f, nearest: 1020f), _ => true, null, 0, h).Matched);
    }

    [Fact]
    public void A_threshold_term_keeps_a_new_value_for_the_minimum_hold_time()
    {
        TacticRow[] rows = [new([T(Cond.HealthBelow, 30, not: true)], Act.Kite, 0)];
        var h = new Hysteresis(0f, 5);
        var d = ShipAiModel.Evaluate(rows, Facts(health: 0.2f), _ => true, null, 0, h);
        Assert.False(d.Matched[0]);
        d = ShipAiModel.Evaluate(rows, Facts(health: 0.5f), _ => true, d.Memory, 2, h);
        Assert.False(d.Matched[0]);
        d = ShipAiModel.Evaluate(rows, Facts(health: 0.5f), _ => true, d.Memory, 5, h);
        Assert.True(d.Matched[0]);
        // A count term has no hysteresis: it follows the fact at once.
        TacticRow[] count = [Row(Cond.EnemiesAtLeast, 2, Act.Kite)];
        d = ShipAiModel.Evaluate(count, Facts(enemies: 2), _ => true, null, 0, h);
        Assert.False(ShipAiModel.Evaluate(count, Facts(enemies: 1), _ => true, d.Memory, 1, h).Matched[0]);
    }

    [Fact]
    public void Memory_of_other_rows_is_ignored()
    {
        TacticRow[] rows = [Row(Cond.HealthBelow, 30, Act.Kite)];
        var d = ShipAiModel.Evaluate(rows, Facts(health: 0.32f), _ => true, [new TermMemory(true, 0), new TermMemory(true, 0)], 1, new Hysteresis(0.03f, 10));
        Assert.False(d.Matched[0]);
        Assert.Single(d.Memory);
    }

    // ---- Personal presets file (spec 422)

    [Fact]
    public void Preset_file_round_trips_and_skips_bad_lines()
    {
        var presets = new List<(string Name, string Text)> { ("Brawler", "HC1:Always>CloseIn"), ("Tech run", "HC1:CratesWithin=2000>CollectTech") };
        string file = ShipAiModel.FormatPresets(presets);
        Assert.Equal(presets, ShipAiModel.ParsePresets(file).Presets);
        var parsed = ShipAiModel.ParsePresets(file + "\nno tab here\nBad\tHC1:Nope>Kite\n\n");
        Assert.Equal(presets, parsed.Presets);
        Assert.Equal(2, parsed.Skipped);
        Assert.Equal("A B", ShipAiModel.CleanPresetName("  A\tB\n "));
    }

    [Fact]
    public void Heat_stage_choices_and_limit_follow_the_number_of_heat_stages()
    {
        Assert.Equal([1, 2, 3], ShipAiModel.ChoicesOf(ParamKind.Stage, 3));
        Assert.Equal([1, 2, 3, 4, 5], ShipAiModel.ChoicesOf(ParamKind.Stage, 5));
        var rows = ShipAiModel.Decode("HC1:HeatStageAtLeast=4>HoldPosition");
        Assert.NotNull(ShipAiModel.StageError(rows, 3));
        Assert.Null(ShipAiModel.StageError(rows, 4));
    }
}

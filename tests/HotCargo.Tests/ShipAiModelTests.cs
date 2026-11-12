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
        Assert.Equal(expected, ShipAiModel.Holds(Row(cond, param, Act.TargetNearest), f));
    }

    [Fact]
    public void Missing_shields_ammo_or_battery_never_count_as_low_and_no_enemy_is_infinitely_far()
    {
        var f = Facts();
        Assert.False(ShipAiModel.Holds(Row(Cond.ShieldsBelow, 100, Act.Kite), f));
        Assert.False(ShipAiModel.Holds(Row(Cond.AmmoBelow, 100, Act.Kite), f));
        Assert.False(ShipAiModel.Holds(Row(Cond.BatteryBelow, 100, Act.Kite), f));
        Assert.True(ShipAiModel.Holds(Row(Cond.EnemyBeyond, 20000, Act.Kite), f));
        Assert.False(ShipAiModel.Holds(Row(Cond.EnemyWithin, 20000, Act.Kite), f));
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
        var every = Enum.GetValues<Cond>().Zip(Enum.GetValues<Act>().Take(Enum.GetValues<Cond>().Length))
            .Select(x => ShipAiModel.WithAct(ShipAiModel.WithCond(new TacticRow(Cond.Always, 0, Act.TargetNearest, 0), x.First), x.Second))
            .ToList();
        Assert.Equal(every, ShipAiModel.Decode(ShipAiModel.Encode(every)));
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
        foreach (var a in Enum.GetValues<Act>())
            Assert.Equal(ShipAiModel.KindOf(a) == ActKind.Weapons, ShipAiModel.ParamOf(a) == ParamKind.Group);
    }

    [Fact]
    public void Changing_a_condition_keeps_the_parameter_only_when_its_kind_stays()
    {
        var row = Row(Cond.HealthBelow, 55, Act.Kite);
        Assert.Equal(55, ShipAiModel.WithCond(row, Cond.ShieldsBelow).CondParam);
        Assert.Equal(1000, ShipAiModel.WithCond(row, Cond.EnemyWithin).CondParam);
        Assert.Equal(0, ShipAiModel.WithCond(row, Cond.Always).CondParam);
    }
}

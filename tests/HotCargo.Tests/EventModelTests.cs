using HotCargo.Events;
using Xunit;

namespace HotCargo.Tests;

public class EventModelTests
{
    static readonly Snapshot Fleet = new()
    {
        Money = 10_000,
        Tech = 3,
        VaultSpace = 1,
        Crew = 4,
        Heat = 40,
        Sector = 3,
        Resources = new Dictionary<string, int> { ["hyperium"] = 25 },
        Parts = new HashSet<string> { "cosmoteer.sensor_array" },
        Flags = new HashSet<string> { "fake_codes" },
    };

    [Fact]
    public void Empty_need_is_met()
    {
        Assert.Null(EventModel.FirstUnmet(new Need(), Fleet));
    }

    [Fact]
    public void Each_need_kind_reports_its_first_failure()
    {
        Assert.Equal(UnmetKind.Money, EventModel.FirstUnmet(new Need { Money = 10_001 }, Fleet)?.Kind);
        Assert.Equal(UnmetKind.Tech, EventModel.FirstUnmet(new Need { Tech = 4 }, Fleet)?.Kind);
        Assert.Equal(UnmetKind.VaultSpace, EventModel.FirstUnmet(new Need { VaultSpace = 2 }, Fleet)?.Kind);
        Assert.Equal(UnmetKind.Crew, EventModel.FirstUnmet(new Need { Crew = 5 }, Fleet)?.Kind);
        Assert.Equal(UnmetKind.MinHeat, EventModel.FirstUnmet(new Need { MinHeat = 41 }, Fleet)?.Kind);
        Assert.Equal(UnmetKind.MaxHeat, EventModel.FirstUnmet(new Need { MaxHeat = 40 }, Fleet)?.Kind);
        Assert.Equal(UnmetKind.MinSector, EventModel.FirstUnmet(new Need { MinSector = 4 }, Fleet)?.Kind);
        Assert.Equal(UnmetKind.MaxSector, EventModel.FirstUnmet(new Need { MaxSector = 2 }, Fleet)?.Kind);
        Assert.Equal(new Unmet(UnmetKind.Resource, "hyperium", 30), EventModel.FirstUnmet(new Need { Resources = [("hyperium", 30)] }, Fleet));
        Assert.Equal(UnmetKind.Resource, EventModel.FirstUnmet(new Need { Resources = [("steel", 1)] }, Fleet)?.Kind);
        Assert.Equal(UnmetKind.Part, EventModel.FirstUnmet(new Need { Parts = ["cosmoteer.railgun_launcher"] }, Fleet)?.Kind);
        Assert.Equal(UnmetKind.Flag, EventModel.FirstUnmet(new Need { Flags = ["defector"] }, Fleet)?.Kind);
        Assert.Equal(UnmetKind.NotFlag, EventModel.FirstUnmet(new Need { NotFlags = ["fake_codes"] }, Fleet)?.Kind);
    }

    [Fact]
    public void Needs_at_the_boundary_are_met()
    {
        var need = new Need { Money = 10_000, Tech = 3, VaultSpace = 1, Crew = 4, MinHeat = 40, MaxHeat = 40.5f, MinSector = 3, MaxSector = 3, Resources = [("hyperium", 25)] };
        Assert.Null(EventModel.FirstUnmet(need, Fleet));
    }

    [Fact]
    public void A_cost_adds_to_the_need_and_merges_resources_by_id()
    {
        var need = new Need { Tech = 1, Resources = [("hyperium", 5)] };
        var total = EventModel.Plus(need, money: 500, tech: 2, crew: 1, [("hyperium", 20), ("steel", 3)]);
        Assert.Equal(3, total.Tech);
        Assert.Equal(500, total.Money);
        Assert.Equal(1, total.Crew);
        Assert.Equal([("hyperium", 25), ("steel", 3)], total.Resources);
        // Keep the last crate: need 1 plus cost 1 is unmet with only 1 crate.
        Assert.Equal(UnmetKind.Tech, EventModel.FirstUnmet(EventModel.Plus(new Need { Tech = 1 }, 0, 1, 0, []), Fleet with { Tech = 1 })?.Kind);
    }

    [Fact]
    public void Weighted_pick_splits_the_roll_by_weight()
    {
        float[] w = [1f, 0f, 3f];
        Assert.Equal(0, EventModel.PickWeighted(w, 0f));
        Assert.Equal(0, EventModel.PickWeighted(w, 0.2499f));
        Assert.Equal(2, EventModel.PickWeighted(w, 0.25f));
        Assert.Equal(2, EventModel.PickWeighted(w, 0.9999f));
        Assert.Equal(-1, EventModel.PickWeighted([0f, 0f], 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => EventModel.PickWeighted(w, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => EventModel.PickWeighted([-1f, 2f], 0.5f));
    }

    [Fact]
    public void Effective_weight_adds_heat_and_tech_terms_and_never_goes_negative()
    {
        Assert.Equal(20f, EventModel.EffectiveWeight(20f, 0.2f, 8f, heat: 0f, tech: 0));
        Assert.Equal(68f, EventModel.EffectiveWeight(20f, 0f, 8f, heat: 0f, tech: 6));
        Assert.Equal(30f, EventModel.EffectiveWeight(25f, 0.2f, 0f, heat: 25f, tech: 0));
        Assert.Equal(0f, EventModel.EffectiveWeight(60f, 0f, -8f, heat: 0f, tech: 9));
    }

    [Fact]
    public void Credits_scale_units_by_standard_pay_and_add_tech_value()
    {
        // Default economy at tier 1: 2,500 standard pay x 2 econ x 2 reward scale.
        Assert.Equal(10_000, EventModel.Credits(1f, 2500f, 4f, 0f, 10_000, 100));
        Assert.Equal(7_500, EventModel.Credits(0f, 2500f, 4f, 0.75f, 10_000, 100));
        Assert.Equal(-6_000, EventModel.Credits(-0.6f, 2500f, 4f, 0f, 0, 100));
        Assert.Equal(1_300, EventModel.Credits(0.125f, 1000f, 10.4f, 0f, 0, 100)); // 1,300 exactly
        Assert.Equal(1_300, EventModel.Credits(0.1251f, 1000f, 10f, 0f, 0, 100)); // 1,251 rounds to 1,300
        Assert.Throws<ArgumentOutOfRangeException>(() => EventModel.Credits(1f, 1f, 1f, 0f, 0, 0));
    }

    [Fact]
    public void Run_limit_and_cooldown()
    {
        Assert.True(EventModel.UnderRunLimit(0, 1));
        Assert.False(EventModel.UnderRunLimit(1, 1));
        Assert.True(EventModel.UnderRunLimit(50, 0));
        Assert.True(EventModel.CooldownOver(10, -1, 120));
        Assert.False(EventModel.CooldownOver(100, 0, 120));
        Assert.True(EventModel.CooldownOver(120, 0, 120));
    }
}

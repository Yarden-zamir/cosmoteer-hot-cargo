using HotCargo.Ux;
using Xunit;

namespace HotCargo.Tests;

public class MissionSelectModelTests
{
    [Fact]
    public void Chains_and_shady_offers_count_as_Hot_Cargo_and_each_filter_keeps_its_kind()
    {
        Assert.True(MissionSelectModel.Passes(MissionFilter.HotCargo, MissionKind.Chain, contract: true));
        Assert.True(MissionSelectModel.Passes(MissionFilter.HotCargo, MissionKind.Shady, contract: false));
        Assert.False(MissionSelectModel.Passes(MissionFilter.HotCargo, MissionKind.Vanilla, contract: true));
        Assert.False(MissionSelectModel.Passes(MissionFilter.Chains, MissionKind.HotCargo, contract: true));
        Assert.False(MissionSelectModel.Passes(MissionFilter.Vanilla, MissionKind.Chain, contract: true));
        Assert.False(MissionSelectModel.Passes(MissionFilter.NoShady, MissionKind.Shady, contract: false));
        Assert.True(MissionSelectModel.Passes(MissionFilter.NoShady, MissionKind.Vanilla, contract: false));
        Assert.True(MissionSelectModel.Passes(MissionFilter.Contracts, MissionKind.Vanilla, contract: true));
        Assert.False(MissionSelectModel.Passes(MissionFilter.Contracts, MissionKind.HotCargo, contract: false));
        foreach (var kind in Enum.GetValues<MissionKind>()) Assert.True(MissionSelectModel.Passes(MissionFilter.All, kind, contract: false));
    }

    [Fact]
    public void A_trap_or_informant_chance_weighs_like_heat_in_the_risk_sort()
    {
        Assert.Equal(5f, MissionSelectModel.RiskKey(5f, 0f, 0f, 20f), 4);
        Assert.Equal(15f, MissionSelectModel.RiskKey(5f, 0.5f, 0f, 20f), 4);
        Assert.Equal(-6f, MissionSelectModel.RiskKey(-8f, 0f, 0.1f, 20f), 4);
        Assert.True(MissionSelectModel.RiskKey(2f, 0.8f, 0f, 20f) > MissionSelectModel.RiskKey(10f, 0f, 0f, 20f));
        Assert.Throws<ArgumentOutOfRangeException>(() => MissionSelectModel.RiskKey(float.NaN, 0f, 0f, 20f));
    }
}

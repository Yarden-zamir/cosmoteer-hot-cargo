using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Halfling;
using Halfling.Geometry;
using Halfling.Gui.Components.Text;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;

namespace HotCargo.Contracts;

/// <summary>
/// The resolved terms of one contract (one mission offer). The economy rolls them once per mission, from the mission ID,
/// so every multiplayer client gets the same terms. The comms missions tab shows them before the player accepts.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
public sealed record ContractTerms
{
    /// <summary>Money that the player pays to accept. 0 = free. The fee is not refunded (purchase model, spec 102).</summary>
    [Serialize] public int Fee;
    /// <summary>Extra money that a paid contract pays on completion. 0 when Fee is 0.</summary>
    [Serialize] public int Bonus;
    /// <summary>Heat added once, when the player accepts.</summary>
    [Serialize] public float FlatHeat;
    /// <summary>How many open contracts this contract counts as for the trail (spec 101). 0 = no trail.</summary>
    [Serialize] public float TrailWeight = 1f;
    /// <summary>Offers in one system with the same non-empty group are rivals: accepting one dismisses the other.</summary>
    [Serialize(Optional = true)] public string RivalGroup = "";
    /// <summary>Chance (0 to 1) that the client sells the player out while the contract is open.</summary>
    [Serialize] public float InformantChance;
}

/// <summary>
/// Rules-side contract terms. Put it in the Rewards list of a mission definition:
/// <code>
/// { Type = HotCargoContract; AcceptFee = 0.5; FlatHeat = 8; TrailWeight = 1; RivalGroup = "smugglers_vs_customs"; InformantChance = 10%; }
/// </code>
/// Every field is optional. A missing field uses the economy default roll (ContractsTunables), like a vanilla mission.
/// AcceptFee is in standard mission pay at the mission tier (the event money unit of spec 73): 0.5 = half a standard mission's pay.
/// 0 means free. A paid contract also pays a completion bonus (ContractsTunables.PaidBonusFactor × fee).
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoContract")]
[TrackMultipleReferences]
[ReflectivePortableHashing(Explicit = true)]
public sealed class ContractTermsRules : RewardSpawner
{
    [Serialize(Optional = true)] public float? AcceptFee;
    [Serialize(Optional = true)] public float? FlatHeat;
    [Serialize(Optional = true)] public float? TrailWeight;
    [Serialize(Optional = true)] public string? RivalGroup;
    [Serialize(Optional = true)] public float? InformantChance;

    public override Reward? CreateReward(Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, Range<int>? tierRange, ID<FactionRules>? faction) =>
        new ContractTermsCarrier { AcceptFee = AcceptFee, FlatHeat = FlatHeat, TrailWeight = TrailWeight, RivalGroup = RivalGroup, InformantChance = InformantChance };
}

/// <summary>
/// Carries the rules-side terms inside the mission, so they live in the save with the mission. It gives nothing and shows nothing:
/// the comms missions tab shows the resolved terms.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoContract")]
[TrackMultipleReferences]
public sealed class ContractTermsCarrier : Reward
{
    [Serialize(Optional = true)] public float? AcceptFee;
    [Serialize(Optional = true)] public float? FlatHeat;
    [Serialize(Optional = true)] public float? TrailWeight;
    [Serialize(Optional = true)] public string? RivalGroup;
    [Serialize(Optional = true)] public float? InformantChance;

    public override void GiveReward(GameRoot game) { }
    public override string? GetRewardText(float lerpT, GameRoot game, Dictionary<int, int>? implicitFame = null) => null;
    public override int GetRewardSortValue(SortValueType valueType, GameRoot game, int implicitFame = 0) => 0;
    public override void GetDisplayLines(List<(ITextProvider Text, ITextProvider? Tooltip)> lines, GameRoot game, Dictionary<int, int>? implicitFame = null) { }

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        iHash.AddHash((int)((AcceptFee ?? -1f) * 1000));
        iHash.AddHash((int)((FlatHeat ?? -1f) * 1000));
        iHash.AddHash((int)((TrailWeight ?? -1f) * 1000));
        iHash.AddHash((int)((InformantChance ?? -1f) * 1000));
    }

    public override int GetTypeIntegrityHash() => 0x4843_4354; // "HCCT"
}

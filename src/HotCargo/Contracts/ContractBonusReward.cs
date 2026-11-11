using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Halfling.Gui.Components.Text;
using Halfling.Serialization;

namespace HotCargo.Contracts;

/// <summary>
/// Spec 102: the completion bonus of a paid contract, in final money (no reward factors on top).
/// The economy adds it to the offer when it saves the terms, so the reward list shows the higher pay before the accept.
/// The player who paid the fee gets the whole bonus, so fee and bonus stay with one player in co-op.
/// If that player is no longer in the game, the bonus is split like vanilla mission money (MoneyReward.GetAmountPerPlayer).
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoContractBonus")]
[TrackMultipleReferences]
public sealed class ContractBonusReward : Reward
{
    public const int NoPayer = -1;

    [Serialize] public int Money { get; private set; }
    /// <summary>Player index of the player who paid the fee. Set by the accept input; NoPayer before the accept.</summary>
    [Serialize(Optional = true)] public int PayerIndex { get; set; } = NoPayer;

    public ContractBonusReward(int money)
    {
        if (money <= 0) throw new ArgumentOutOfRangeException(nameof(money), "must be above 0");
        Money = money;
    }

    // Deserialization.
    ContractBonusReward() { }

    public override void GiveReward(GameRoot game)
    {
        if (PayerIndex != NoPayer && game.HumanPlayersIndexes.Contains(PayerIndex))
        {
            game.GetPlayerState(PayerIndex, deterministic: true).AddMoney(Money);
            return;
        }
        var players = new List<PlayerState>();
        game.GetHumanPlayerStates(players, deterministic: true);
        int each = Money / Math.Max(1, game.LogicalHumanPlayerCount);
        foreach (var p in players) p.AddMoney(each);
    }

    public override string? GetRewardText(float lerpT, GameRoot game, Dictionary<int, int>? implicitFame = null) =>
        WidgetRules.Instance.MoneyTextColor.FormatWithXml(Strings.FormatText("HotCargoContracts/BonusRewardFmt", (int)(Money * Math.Clamp(lerpT, 0f, 1f))));

    public override int GetRewardSortValue(SortValueType valueType, GameRoot game, int implicitFame = 0) =>
        valueType == SortValueType.Money ? Money : 0;

    public override void GetDisplayLines(List<(ITextProvider Text, ITextProvider? Tooltip)> lines, GameRoot game, Dictionary<int, int>? implicitFame = null) =>
        lines.Add((new SimpleTextProvider(GetRewardText(1f, game)!, null), Strings.KeyString("HotCargoContracts/BonusRewardTip")));

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        iHash.AddHash(Money);
        iHash.AddHash(PayerIndex);
    }

    public override int GetTypeIntegrityHash() => 0x4843_4342; // "HCCB"
}

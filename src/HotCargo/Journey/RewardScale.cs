using System.Reflection;
using System.Reflection.Emit;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions.Objectives;
using Cosmoteer.Modes.Career.Missions.Rewards;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Ui;

namespace HotCargo.Journey;

/// <summary>
/// Spec requirement 4: fame and money rewards × RewardScale.
/// Every vanilla read of EconDifficulty.FameRewardFactor or MoneyRewardFactor goes through FameFactor or MoneyFactor.
/// So mission rewards, ship kill fame and the reward text in the UI all agree.
/// The scale comes from the run tunables, which all multiplayer clients share, so it is deterministic.
/// </summary>
[HarmonyPatch]
static class RewardScalePatch
{
    static readonly FieldInfo FameField = AccessTools.Field(typeof(EconDifficultyRules), nameof(EconDifficultyRules.FameRewardFactor));
    static readonly FieldInfo MoneyField = AccessTools.Field(typeof(EconDifficultyRules), nameof(EconDifficultyRules.MoneyRewardFactor));

    public static float FameFactor(EconDifficultyRules econ) => econ.FameRewardFactor * Scale();
    public static float MoneyFactor(EconDifficultyRules econ) => econ.MoneyRewardFactor * Scale();

    static float Scale() => Run.Current?.T.Journey.RewardScale ?? 1f;

    /// <summary>All vanilla methods that read a reward factor (0.30.5a). A new reader after a game update is not scaled until it is added here.</summary>
    static IEnumerable<MethodBase> TargetMethods() =>
    [
        AccessTools.Method(typeof(FameReward), "GetTotalAmount"),
        AccessTools.Method(typeof(MoneyReward), "GetScaledMoney"),
        AccessTools.Method(typeof(CareerSimModeManager), nameof(CareerSimModeManager.GetShipFameInfo)),
        AccessTools.Method(typeof(ObjectiveTargetShip), nameof(ObjectiveTargetShip.GetTargetFameInfo)),
    ];

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var fame = AccessTools.Method(typeof(RewardScalePatch), nameof(FameFactor));
        var money = AccessTools.Method(typeof(RewardScalePatch), nameof(MoneyFactor));
        int replaced = 0;
        var result = instructions.Select(ci =>
        {
            if (ci.LoadsField(FameField)) { replaced++; return new CodeInstruction(OpCodes.Call, fame).MoveLabelsFrom(ci); }
            if (ci.LoadsField(MoneyField)) { replaced++; return new CodeInstruction(OpCodes.Call, money).MoveLabelsFrom(ci); }
            return ci;
        }).ToList();
        if (replaced == 0)
        {
            string error = $"reward scale: no reward factor read found in {original.DeclaringType?.Name}.{original.Name}; rewards there are not scaled";
            Log.Error(error);
            Overlay.FatalError = error;
        }
        return result;
    }
}

namespace HotCargo.Contracts;

/// <summary>Pure contract math on plain numbers. No game types, so unit tests run on the Mac.</summary>
public static class ContractsModel
{
    /// <summary>Spec 101: heat per game minute from open contracts. Only the weight above the free contracts counts.</summary>
    public static float TrailPerMinute(float openWeight, float freeContracts, float heatPerContractMinute) =>
        Math.Max(0f, openWeight - freeContracts) * Math.Max(0f, heatPerContractMinute);

    /// <summary>
    /// Spec 102: a fee in standard mission pay units, turned into money. standardMoney is the vanilla pay of the tier and moneyFactor
    /// the payout factors (difficulty × RewardScale), so a fee keeps the same weight against rewards on every tier and difficulty.
    /// A fee above 0 is at least one rounding step.
    /// </summary>
    public static int Fee(float units, int standardMoney, float moneyFactor, int roundTo)
    {
        if (units <= 0) return 0;
        int step = Math.Max(1, roundTo);
        double raw = units * standardMoney * moneyFactor;
        return (int)Math.Max(step, Math.Round(raw / step) * step);
    }

    /// <summary>Spec 102: the extra pay of a paid contract on completion. Net gain over a free contract = fee × (factor − 1).</summary>
    public static int Bonus(int fee, float factor, int roundTo)
    {
        if (fee <= 0) return 0;
        int step = Math.Max(1, roundTo);
        return (int)(Math.Round(fee * Math.Max(0f, factor) / step) * step);
    }
}

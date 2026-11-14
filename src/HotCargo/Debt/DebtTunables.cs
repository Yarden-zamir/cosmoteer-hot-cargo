using Halfling.Serialization;

namespace HotCargo.Debt;

/// <summary>Debt (spec 1400 to 1439).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class DebtTunables
{
    /// <summary>Spec 1402: off turns loan offers off. Debts that exist keep their interest and collectors.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;
    /// <summary>Spec 1402: chance that a faction trade station offers a loan. The roll holds for the station for the run.</summary>
    [Serialize(Optional = true)] public float LoanStationChance = 0.5f;
    /// <summary>Spec 1402: the loan, in event money units (1 = one standard mission's pay at the sector tier).</summary>
    [Serialize(Optional = true)] public float LoanUnits = 5f;
    /// <summary>Spec 1404: interest per step: 0.05 = the balance grows by 5 % each step.</summary>
    [Serialize(Optional = true)] public float InterestRate = 0.05f;
    /// <summary>Spec 1404: game seconds between interest steps.</summary>
    [Serialize(Optional = true)] public float InterestSeconds = 600f;
    /// <summary>Spec 1405: one step of the pay slider, as a share of the balance.</summary>
    [Serialize(Optional = true)] public float PayStepShare = 0.1f;
    /// <summary>Spec 1406: from this balance ÷ principal, collectors talk in a firm tone.</summary>
    [Serialize(Optional = true)] public float FirmRatio = 2f;
    /// <summary>Spec 1409: from this balance ÷ principal, the lender wants the players and its police come.</summary>
    [Serialize(Optional = true)] public float HostileRatio = 3f;
    /// <summary>Spec 1407: game seconds between collectors.</summary>
    [Serialize(Optional = true)] public float CollectorSeconds = 2400f;
    /// <summary>Spec 1407: a collector jumps in this far from the fleet center, inside comms range like an event carrier (600 to 900).</summary>
    [Serialize(Optional = true)] public float CollectorDistance = 700f;
    /// <summary>Spec 1407: game seconds that a collector waits for an answer after it arrives.</summary>
    [Serialize(Optional = true)] public float CollectorWaitSeconds = 150f;
    /// <summary>Spec 1410: after the lender stops wanting the players, the next collector comes within this many game seconds.</summary>
    [Serialize(Optional = true)] public float ReturnSeconds = 180f;
    /// <summary>Spec 1411: the score loses this many times the sum of the balances.</summary>
    [Serialize(Optional = true)] public float ScoreDebtFactor = 2f;

    /// <summary>Spec 1420: chance that a mission of a lender's faction pays the debt instead of money. The roll holds for the mission.</summary>
    [Serialize(Optional = true)] public float MissionPayoffChance = 0.35f;
    /// <summary>Spec 1421: chance that a trade station offers to buy a debt. The roll holds for the station for the run.</summary>
    [Serialize(Optional = true)] public float RefinanceStationChance = 0.3f;
    /// <summary>Spec 1421: a refinanced debt grows at InterestRate × this.</summary>
    [Serialize(Optional = true)] public float RefinanceRateFactor = 0.6f;
    /// <summary>Spec 1421: the old lender loses this much reputation when another faction buys the debt.</summary>
    [Serialize(Optional = true)] public int RefinanceReputationLoss = 200;
    /// <summary>Spec 1422: off turns the Collect a Debt offers off.</summary>
    [Serialize(Optional = true)] public bool CollectEnabled = true;
    /// <summary>Spec 1422: the collect job pays this many standard missions' money (always off the debt).</summary>
    [Serialize(Optional = true)] public float CollectRewardFactor = 1.5f;

    public void Validate(Action<bool, string> require)
    {
        require(LoanStationChance is >= 0 and <= 1, "Debt.LoanStationChance must be 0 to 1");
        require(LoanUnits > 0 && InterestRate >= 0 && PayStepShare is > 0 and <= 1, "Debt.LoanUnits must be above 0, InterestRate not negative, PayStepShare above 0 and at most 1");
        require(InterestSeconds > 0 && CollectorSeconds > 0 && CollectorWaitSeconds > 0 && ReturnSeconds >= 0, "Debt timers must be above 0 (ReturnSeconds not negative)");
        require(FirmRatio > 1 && HostileRatio >= FirmRatio, "Debt.FirmRatio must be above 1 and HostileRatio at least FirmRatio");
        require(MissionPayoffChance is >= 0 and <= 1 && RefinanceStationChance is >= 0 and <= 1, "Debt.MissionPayoffChance and RefinanceStationChance must be 0 to 1");
        require(RefinanceRateFactor >= 0 && RefinanceReputationLoss >= 0 && CollectRewardFactor >= 0, "Debt.RefinanceRateFactor, RefinanceReputationLoss and CollectRewardFactor must not be negative");
        require(CollectorDistance > 0 && ScoreDebtFactor >= 0, "Debt.CollectorDistance must be above 0, ScoreDebtFactor not negative");
    }
}

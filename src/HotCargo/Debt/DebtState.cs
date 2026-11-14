using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Debt;

/// <summary>One debt to one lender (spec 1401). Times are <see cref="Core.RunState.GameSeconds"/>.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class Debt
{
    [Serialize] public string Faction = "";
    [Serialize] public long Principal;
    [Serialize] public long Balance;
    [Serialize] public double NextInterestAt;
    [Serialize] public double NextCollectorAt;
    /// <summary>The highest stage that was announced. It falls with a payment, so a new rise is announced again.</summary>
    [Serialize] public DebtStage Stage;
    /// <summary>Spec 1410: the lender wanted the players at the last tick.</summary>
    [Serialize] public bool WasWanted;
    /// <summary>Spec 1421: the interest of this debt is InterestRate × this. A refinanced debt has a lower factor.</summary>
    [Serialize(Optional = true)] public float RateFactor = 1f;

    public ID<FactionRules> FactionId => new(Faction);
}

/// <summary>A collector ship in this system (spec 1407).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class Collector
{
    [Serialize] public ObjectID Ship;
    [Serialize] public string Faction = "";
    [Serialize] public bool Hailed;
    [Serialize] public double LeaveAt;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class DebtState
{
    /// <summary>One debt for each lender, in the order the players took them (a stable order for the tick).</summary>
    [Serialize] public List<Debt> Debts = [];
    [Serialize] public List<Collector> Collectors = [];
    /// <summary>Spec 1422: the systems (map node IDs) that offered a Collect a Debt job.</summary>
    [Serialize(Optional = true)] public List<ObjectID> CollectSystems = [];

    public Debt? Of(string faction) => Debts.FirstOrDefault(d => d.Faction == faction);

    public long Total => Debts.Sum(d => d.Balance);
}

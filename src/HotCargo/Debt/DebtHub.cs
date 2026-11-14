using Halfling.Serialization;

namespace HotCargo.Core;

// Debt's parts of the shared Tunables and RunState.

public sealed partial class Tunables
{
    [Serialize(Optional = true)] public Debt.DebtTunables Debt = new();
}

public sealed partial class RunState
{
    /// <summary>Debts, their timers and the collectors in this system (spec 1413).</summary>
    [Serialize(Optional = true)] public Debt.DebtState Debt = new();
}

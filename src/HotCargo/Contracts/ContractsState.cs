using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Contracts;

/// <summary>Contract economy state, saved in the run state. Keys are mission IDs.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ContractsState
{
    /// <summary>
    /// Resolved terms of offers and accepted contracts.
    /// Limit: entries stay after a mission ends or an offer expires. They are small and a run is short. Revisit if saves grow.
    /// </summary>
    [Serialize(Optional = true)] public Dictionary<ObjectID, ContractTerms> Terms = new();
    /// <summary>Spec 104: contracts whose client will sell the players out, with the game time of the sell-out.</summary>
    [Serialize(Optional = true)] public Dictionary<ObjectID, double> Informants = new();
    /// <summary>Spec 104: contracts whose client already sold the players out. The mission log names them.</summary>
    [Serialize(Optional = true)] public HashSet<ObjectID> SoldOut = new();
    /// <summary>Game seconds since the last offer scan.</summary>
    [Serialize(Optional = true)] public double SecondsSinceScan;
}

using Halfling.Serialization;

namespace HotCargo.Dev;

/// <summary>Spec 990 to 1009: rules that must always hold. A broken rule writes one ERROR line to the log. It never changes the game.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class InvariantsTunables
{
    /// <summary>Checks the rules that must always hold and writes an ERROR line to the log when one breaks. It only reads the game.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;
    /// <summary>Game seconds between two checks of the rules that read the whole system.</summary>
    [Serialize(Optional = true)] public float CheckSeconds = 1f;
}

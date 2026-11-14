using Cosmoteer.Game;
using Halfling.Serialization;

namespace HotCargo.Scavenge;

/// <summary>Sim state of scavenging. Saved with the run, so a load and a co-op resync keep the ages.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ScavengeState
{
    /// <summary>Spec 1332: run game seconds at which the age check first saw each live wreck and loose resource, by object ID.</summary>
    [Serialize(Optional = true)] public Dictionary<ObjectID, double> Seen = new();
    /// <summary>Spec 1341: run game seconds until which the scavengers are hostile to the players. 0: never provoked.</summary>
    [Serialize(Optional = true)] public double ProvokedUntil;
}

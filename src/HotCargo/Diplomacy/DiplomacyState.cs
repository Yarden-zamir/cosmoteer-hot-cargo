using Cosmoteer.Game;
using Halfling.Geometry;
using Halfling.Serialization;

namespace HotCargo.Diplomacy;

/// <summary>Kinds of hail options. Each one works once per ship (spec 568), except the ones that <see cref="DiplomacyState.Repeatable"/> lists.</summary>
public enum Offer
{
    HandOver, Bluff, Deal, Refuse,
    DemandSurrender, OfferSurrender, AcceptYield,
    Hold, PayHoldFee, DeclineHoldFee, Return,
    Hire, Merc,
}

/// <summary>Saved diplomacy state (spec 586). Everything here changes only in the sim tick, the AI tick and <see cref="DiplomacyInput"/>.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class DiplomacyState
{
    /// <summary>"shipId:Offer" keys of the offers already made, for the once-per-ship rule.</summary>
    [Serialize(Optional = true)] public HashSet<string> Used = new();
    /// <summary>Ships that already rolled a hail of this kind: "shipId:demand", "shipId:yield", "shipId:merc", "shipId:merchail".</summary>
    [Serialize(Optional = true)] public HashSet<string> Rolled = new();
    /// <summary>Friendly ships that rolled mercenary (spec 584).</summary>
    [Serialize(Optional = true)] public HashSet<ObjectID> Mercs = new();
    /// <summary>Run game seconds of the last hunter demand hail.</summary>
    [Serialize(Optional = true)] public double LastDemandAt = -1e9;
    /// <summary>Run game seconds of the next sim check (CheckSeconds apart). Saved, so a joining client checks at the same ticks.</summary>
    [Serialize(Optional = true)] public double NextCheck;
    [Serialize(Optional = true)] public List<HeldTech> Held = new();
    [Serialize(Optional = true)] public List<Hire> Hires = new();
    /// <summary>Hunters that agreed to leave, or surrendered, and jump out at Due.</summary>
    [Serialize(Optional = true)] public List<Leaving> Leaving = new();
    /// <summary>A friendly ship that asked for a fee before it holds tech.</summary>
    [Serialize(Optional = true)] public List<FeeAsk> FeeAsks = new();
    /// <summary>The last result of each hailed ship, shown at the top of its dialogue.</summary>
    [Serialize(Optional = true)] public List<HailResult> Results = new();
    /// <summary>Counts results, so each result line gets its own number.</summary>
    [Serialize(Optional = true)] public int Serial;

    public static string Key(ObjectID ship, Offer offer) => $"{(uint)ship}:{offer}";
    public static string Key(ObjectID ship, string what) => $"{(uint)ship}:{what}";

    /// <summary>Offers that a ship takes more than once: the return of held tech, extending a mercenary contract, the fee answer.</summary>
    public static bool Repeatable(Offer o) => o is Offer.Return or Offer.Merc or Offer.PayHoldFee or Offer.DeclineHoldFee;

    public bool WasUsed(ObjectID ship, Offer offer) => !Repeatable(offer) && Used.Contains(Key(ship, offer));
}

/// <summary>Tech that a friendly ship holds for a player (spec 573 to 578).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class HeldTech
{
    [Serialize] public ObjectID Ship;
    [Serialize] public string ShipName = "";
    /// <summary>The system of the ship. Held tech in another system counts until the players come back and find the ship gone.</summary>
    [Serialize] public ObjectID Node;
    [Serialize] public int Player;
    [Serialize] public int Units;
    [Serialize] public bool FeePaid;
    /// <summary>Rolled at the hand-over. The return compares it with the betrayal chance at that time.</summary>
    [Serialize] public float BetrayRoll;
    /// <summary>Last known position. If the ship dies, the tech drops here.</summary>
    [Serialize] public Vector2 Location;
}

/// <summary>A friendly ship that fights for a player until Until (spec 579 to 583).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class Hire
{
    [Serialize] public ObjectID Ship;
    [Serialize] public int Player;
    /// <summary>Run game seconds at which the contract ends.</summary>
    [Serialize] public double Until;
    [Serialize] public bool Merc;
    /// <summary>The ship ran from damage. It runs until clear, then the contract ends.</summary>
    [Serialize] public bool Fleeing;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class Leaving
{
    [Serialize] public ObjectID Ship;
    [Serialize] public double Due;
    /// <summary>True after the jump-out effect started; the ship leaves the sim at RemoveAt.</summary>
    [Serialize] public bool Jumping;
    [Serialize] public double RemoveAt;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class FeeAsk
{
    [Serialize] public ObjectID Ship;
    [Serialize] public int Player;
    [Serialize] public int Units;
    [Serialize] public int Fee;
}

[ReflectiveSerialization(Explicit = true)]
public sealed class HailResult
{
    [Serialize] public ObjectID Ship;
    [Serialize] public int Serial;
    /// <summary>XML text, ready for the dialogue label.</summary>
    [Serialize] public string Text = "";
}

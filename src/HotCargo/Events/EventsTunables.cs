using Halfling.Serialization;

namespace HotCargo.Events;

/// <summary>Event frequency, cooldowns and global scales (live reload). Event content is in mod/events/events.rules (restart).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class EventsTunables
{
    /// <summary>Events that open by themselves (Start, Arrival, Station, Signal, Wanted). Off: none of them opens.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;
    /// <summary>Chance that the first arrival in a system opens an Arrival event.</summary>
    [Serialize(Optional = true)] public float ArrivalEventChance = 0.6f;
    /// <summary>Game seconds after arrival before the Arrival event opens, so the players see the system first.</summary>
    [Serialize(Optional = true)] public float ArrivalDelaySeconds = 6f;
    /// <summary>Chance, once per system visit, that the first trade station a player ship comes near opens a Station event.</summary>
    [Serialize(Optional = true)] public float StationEventChance = 0.5f;
    [Serialize(Optional = true)] public float StationTriggerDistance = 1500f;
    /// <summary>Chance, once per system for the run, that the first trade station a player ship comes near offers a Courier Job.</summary>
    [Serialize(Optional = true)] public float CourierChance = 0.5f;
    /// <summary>Heat that each Tracer Beacon in a player ship adds once a minute (spec 1116).</summary>
    [Serialize(Optional = true)] public float TracerHeatPerMinute = 3f;
    [Serialize(Optional = true)] public float SignalTriggerDistance = 900f;
    /// <summary>Arrival and Station events wait this many game seconds after the last triggered event. A new run starts it. Signal events ignore it but start it.</summary>
    [Serialize(Optional = true)] public float CooldownSeconds = 120f;
    /// <summary>Arrival and Station events in one system, all visits together. Signal events do not count, because the player chose to go there.</summary>
    [Serialize(Optional = true)] public int MaxEventsPerSystem = 2;
    /// <summary>The wanted-and-hunted warning shows when the players are wanted and heat reached this stage (1 = the first stage).</summary>
    [Serialize(Optional = true)] public int WantedWarningMinStage = 1;
    /// <summary>Chance that the wanted-and-hunted warning also brings a Wanted event (a way to clear wanted).</summary>
    [Serialize(Optional = true)] public float WantedEventChance = 0.7f;
    /// <summary>Research trade: one tech unit buys up to this many times its tech value in research.</summary>
    [Serialize(Optional = true)] public float ResearchRate = 1.5f;
    /// <summary>Research trade: the research must be worth at least this many times the tech value of the units. Above 1, so it beats buying.</summary>
    [Serialize(Optional = true)] public float ResearchMinRate = 1.2f;
    /// <summary>Research trade: the most tech units one offer asks for.</summary>
    [Serialize(Optional = true)] public int ResearchMaxUnits = 2;
    /// <summary>Research offers: the most techs in one offer.</summary>
    [Serialize(Optional = true)] public int ResearchMaxTechs = 3;
    /// <summary>Research sale: the buyer pays this many times the research cost of the techs.</summary>
    [Serialize(Optional = true)] public float ResearchSalePremium = 1.3f;
    /// <summary>Research sale: the highest price, in event money units (standard mission pay at the tier).</summary>
    [Serialize(Optional = true)] public float ResearchSaleCap = 1.5f;
    /// <summary>Multiplies the trigger weight of research events: 0 turns them off, 2 doubles them.</summary>
    [Serialize(Optional = true)] public float ResearchWeightScale = 1f;
    /// <summary>Chance that coming near a vanilla abandoned ship opens an Abandoned event (once per ship).</summary>
    [Serialize(Optional = true)] public float AbandonedEventChance = 0.6f;
    [Serialize(Optional = true)] public float AbandonedTriggerDistance = 800f;
    /// <summary>One relic ship per run holds 1 tech unit. Off: new runs get none.</summary>
    [Serialize(Optional = true)] public bool RelicEnabled = true;
    /// <summary>The relic ship appears in a random sector from this one up to the sector before the Buyer.</summary>
    [Serialize(Optional = true)] public int RelicMinSector = 2;
    /// <summary>Distance at which the vault senses the relic ship and the special message shows.</summary>
    [Serialize(Optional = true)] public float RelicMessageDistance = 3000f;
    /// <summary>Distance at which the relic event opens.</summary>
    [Serialize(Optional = true)] public float RelicBoardDistance = 900f;
    /// <summary>Events of the Unavoidable category (every choice costs something) per run.</summary>
    [Serialize(Optional = true)] public int MaxUnavoidablePerRun = 1;
    /// <summary>Global scales on event outcomes, on top of the per-event amounts.</summary>
    [Serialize(Optional = true)] public float MoneyScale = 1f;
    [Serialize(Optional = true)] public float FameScale = 1f;
    [Serialize(Optional = true)] public float HeatScale = 1f;
    /// <summary>Event money amounts round to this many credits.</summary>
    [Serialize(Optional = true)] public int MoneyRoundTo = 100;
    /// <summary>Single player: an event dialog pauses the game until the players choose.</summary>
    [Serialize(Optional = true)] public bool PauseOnEvent = true;
}

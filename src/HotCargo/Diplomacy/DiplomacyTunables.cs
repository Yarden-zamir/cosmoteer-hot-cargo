using Halfling.Serialization;

namespace HotCargo.Diplomacy;

/// <summary>Hails and deals: hunter demands, surrenders, friendly ships that hold tech or fight for pay, mercenaries (spec 560 to 589).</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class DiplomacyTunables
{
    /// <summary>Turns every Hot Cargo hail option and hunter demand off. Vanilla comms stay.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;

    // ---- Shared situation

    /// <summary>Radius around the hailed ship that counts as its area: ships inside it count for strength and numbers.</summary>
    [Serialize(Optional = true)] public float AreaRadius = 3000f;
    /// <summary>Reputation that counts as full trust (+1) or full hate (-1) in every chance. The vanilla "trusted" title starts at 2500.</summary>
    [Serialize(Optional = true)] public float ReputationScale = 2500f;
    /// <summary>Game seconds between the sim checks for hails, mercenaries and held tech.</summary>
    [Serialize(Optional = true)] public float CheckSeconds = 1f;

    // ---- Hunter demands (spec 561 to 566)

    /// <summary>Chance that a hunter that first comes into comms range hails the players to demand the tech. One open demand at a time.</summary>
    [Serialize(Optional = true)] public float DemandHailChance = 0.8f;
    /// <summary>Game seconds after a hunter demand hail before another hunter can hail.</summary>
    [Serialize(Optional = true)] public float DemandHailGapSeconds = 60f;
    /// <summary>Game seconds after the hunters accept a deal, or a hunter surrenders, before they jump out of the system.</summary>
    [Serialize(Optional = true)] public float HunterLeaveSeconds = 8f;
    /// <summary>Chance that the hunters accept a hand-over of 1 unit out of many. The chance rises linearly to HandOverChanceMax for all but one unit.</summary>
    [Serialize(Optional = true)] public float HandOverChanceMin = 0.15f;
    /// <summary>Chance that the hunters accept a hand-over of all units.</summary>
    [Serialize(Optional = true)] public float HandOverChanceMax = 0.95f;
    /// <summary>Chance lost when the largest pile of tech left after the hand-over is as big as the whole stock. Scales with that pile's share.</summary>
    [Serialize(Optional = true)] public float HandOverVisiblePenalty = 0.5f;
    /// <summary>Chance added per point of leverage (-1 when the hunters are 4 times stronger, +1 when the players are).</summary>
    [Serialize(Optional = true)] public float HandOverPerLeverage = 0.15f;
    /// <summary>Base chance that a bluff works ("That's all I have").</summary>
    [Serialize(Optional = true)] public float BluffChanceBase = 0.6f;
    /// <summary>Bluff chance added when the hand-over is all units. Scales with the share handed over.</summary>
    [Serialize(Optional = true)] public float BluffPerShare = 0.2f;
    /// <summary>Bluff chance lost when the largest pile left is as big as the whole stock. Scales with that pile's share. Spread the tech to bluff.</summary>
    [Serialize(Optional = true)] public float BluffVisiblePenalty = 1.2f;
    /// <summary>Heat added when the hunters see through a bluff. They keep the units and fight.</summary>
    [Serialize(Optional = true)] public float BluffCaughtHeat = 10f;
    /// <summary>Tech vaults closer than this (world units, part center to part center) count as one pile.</summary>
    [Serialize(Optional = true)] public float PileDistance = 5f;
    /// <summary>Chance of a deal (pay the money value instead of tech) when no other hunter is in the area and the money covers all units.</summary>
    [Serialize(Optional = true)] public float DealChanceAlone = 0.85f;
    /// <summary>With more other hunters than this in the area, a deal never works. Scales with co-op enemy scaling.</summary>
    [Serialize(Optional = true)] public int DealMaxOtherHunters = 4;
    /// <summary>Deal chance factor when the money covers 1 unit out of many. It rises linearly to 1 for all units.</summary>
    [Serialize(Optional = true)] public float DealShareMin = 0.4f;
    /// <summary>Deal price per unit, times the tech value.</summary>
    [Serialize(Optional = true)] public float DealPriceFactor = 1f;

    // ---- Surrender (spec 567 to 573)

    /// <summary>Base chance that an enemy accepts the players' demand to surrender.</summary>
    [Serialize(Optional = true)] public float SurrenderChanceBase = 0.1f;
    /// <summary>Surrender chance per point of leverage (-1 to +1).</summary>
    [Serialize(Optional = true)] public float SurrenderPerLeverage = 0.35f;
    /// <summary>Surrender chance per lost health share of the enemy ship (0 to 1).</summary>
    [Serialize(Optional = true)] public float SurrenderPerDamage = 0.6f;
    /// <summary>Surrender chance at the top fame title. Scales with the title.</summary>
    [Serialize(Optional = true)] public float SurrenderPerFame = 0.15f;
    /// <summary>Surrender chance per point of reputation with the enemy's faction (-1 to +1).</summary>
    [Serialize(Optional = true)] public float SurrenderPerReputation = 0.1f;
    /// <summary>Surrender chance lost for each ally of the enemy in the area.</summary>
    [Serialize(Optional = true)] public float SurrenderPerAlly = 0.1f;
    /// <summary>Surrender chance lost when the enemy's faction wants the players.</summary>
    [Serialize(Optional = true)] public float SurrenderWantedPenalty = 0.3f;
    /// <summary>Hunters only: surrender chance lost at heat 100. Scales with heat.</summary>
    [Serialize(Optional = true)] public float HunterSurrenderPerHeat = 0.3f;
    /// <summary>Surrender payout times the vanilla payout (ship value × the AI's AISurrenderPayoutMultiple).</summary>
    [Serialize(Optional = true)] public float SurrenderPayoutScale = 1f;
    /// <summary>Extra payout share per point of positive leverage. A stronger fleet gets more.</summary>
    [Serialize(Optional = true)] public float SurrenderPayoutPerLeverage = 0.5f;
    /// <summary>Chance that a surrendering ship also gives one other resource that it carries (only what fits).</summary>
    [Serialize(Optional = true)] public float ExtraLootChance = 0.5f;
    /// <summary>Share of that other resource that the ship gives.</summary>
    [Serialize(Optional = true)] public float ExtraLootShare = 0.5f;
    /// <summary>A hunter below this health may hail to offer its surrender.</summary>
    [Serialize(Optional = true)] public float HunterYieldHealth = 0.35f;
    /// <summary>Chance, rolled once per hunter when it drops below HunterYieldHealth, that it offers its surrender.</summary>
    [Serialize(Optional = true)] public float HunterYieldChance = 0.4f;
    /// <summary>Base chance that an enemy accepts the players' surrender.</summary>
    [Serialize(Optional = true)] public float OfferChanceBase = 0.5f;
    /// <summary>Chance added per point of the enemy's leverage (they are stronger).</summary>
    [Serialize(Optional = true)] public float OfferPerLeverage = 0.3f;
    /// <summary>Chance per point of reputation with the enemy's faction.</summary>
    [Serialize(Optional = true)] public float OfferPerReputation = 0.2f;
    /// <summary>Chance lost when the enemy's faction wants the players.</summary>
    [Serialize(Optional = true)] public float OfferWantedPenalty = 0.2f;
    /// <summary>Price of the players' surrender in event money units (a standard mission's pay at the sector tier).</summary>
    [Serialize(Optional = true)] public float OfferPriceUnits = 1f;
    /// <summary>Price factor when the enemy's faction wants the players.</summary>
    [Serialize(Optional = true)] public float OfferWantedPriceFactor = 1.5f;

    // ---- Friendly ships hold tech (spec 574 to 578)

    /// <summary>Base chance that a friendly ship agrees to hold tech.</summary>
    [Serialize(Optional = true)] public float HoldChanceBase = 0.7f;
    /// <summary>Hold chance per point of reputation with the ship's faction.</summary>
    [Serialize(Optional = true)] public float HoldPerReputation = 0.3f;
    /// <summary>Hold chance lost at heat 100. Scales with heat.</summary>
    [Serialize(Optional = true)] public float HoldPerHeat = 0.5f;
    /// <summary>Hold chance lost while any hunter is in the system.</summary>
    [Serialize(Optional = true)] public float HoldHuntersPenalty = 0.3f;
    /// <summary>Hold chance lost per unit asked.</summary>
    [Serialize(Optional = true)] public float HoldPerUnit = 0.03f;
    /// <summary>No ship holds tech while a hunter is this close to it.</summary>
    [Serialize(Optional = true)] public float HoldHunterRadius = 4000f;
    /// <summary>Base chance that the ship asks for a fee first.</summary>
    [Serialize(Optional = true)] public float FeeChanceBase = 0.3f;
    /// <summary>Fee chance added at heat 100.</summary>
    [Serialize(Optional = true)] public float FeePerHeat = 0.4f;
    /// <summary>Fee chance per point of reputation (good reputation lowers it).</summary>
    [Serialize(Optional = true)] public float FeePerReputation = -0.2f;
    /// <summary>Fee per unit held, times the tech value.</summary>
    [Serialize(Optional = true)] public float FeePerUnit = 0.1f;
    /// <summary>Base chance that the ship keeps the tech when the players ask for it back. Rolled at the hand-over, judged at the return.</summary>
    [Serialize(Optional = true)] public float BetrayChanceBase = 0.1f;
    /// <summary>Betrayal chance added at heat 100 at the time of the return.</summary>
    [Serialize(Optional = true)] public float BetrayPerHeat = 0.3f;
    /// <summary>Betrayal chance added per hunter in the system at the time of the return.</summary>
    [Serialize(Optional = true)] public float BetrayPerHunter = 0.1f;
    /// <summary>Betrayal chance per point of reputation (good reputation lowers it).</summary>
    [Serialize(Optional = true)] public float BetrayPerReputation = -0.15f;
    /// <summary>Betrayal chance change when the players paid a fee.</summary>
    [Serialize(Optional = true)] public float BetrayFeePaid = -0.05f;

    // ---- Hired help and mercenaries (spec 579 to 583)

    /// <summary>Fair price for help, in event money units. The offer slider goes up to twice this.</summary>
    [Serialize(Optional = true)] public float HireFairUnits = 1f;
    /// <summary>Base hire chance before the offer counts.</summary>
    [Serialize(Optional = true)] public float HireChanceBase = -0.1f;
    /// <summary>Hire chance per fair price offered, up to 1.5 fair prices.</summary>
    [Serialize(Optional = true)] public float HirePerOffer = 0.6f;
    /// <summary>Hire chance per point of reputation with the ship's faction.</summary>
    [Serialize(Optional = true)] public float HirePerReputation = 0.3f;
    /// <summary>Hire chance lost at heat 100.</summary>
    [Serialize(Optional = true)] public float HirePerHeat = 0.4f;
    /// <summary>Seconds that a hired ship helps: about one mission.</summary>
    [Serialize(Optional = true)] public float HireSeconds = 300f;
    /// <summary>A hired ship below this health stops helping and runs.</summary>
    [Serialize(Optional = true)] public float HiredFleeHealth = 0.5f;
    /// <summary>A hired ship attacks enemies of the players within this distance of its employer's nearest ship.</summary>
    [Serialize(Optional = true)] public float HelpRadius = 2500f;
    /// <summary>A hired ship with no target stays within this distance of its employer's nearest ship.</summary>
    [Serialize(Optional = true)] public float FollowDistance = 700f;
    /// <summary>A fleeing hired ship runs until the nearest enemy is this far away, then goes back to its own business.</summary>
    [Serialize(Optional = true)] public float HiredFleeDistance = 2000f;
    /// <summary>Chance that an armed friendly ship near a station is a mercenary. Rolled once per ship.</summary>
    [Serialize(Optional = true)] public float MercChance = 0.25f;
    /// <summary>A ship counts as near a station within this distance.</summary>
    [Serialize(Optional = true)] public float MercStationRadius = 3000f;
    /// <summary>Chance, rolled once per mercenary, that it hails the players to offer its services when they come into comms range.</summary>
    [Serialize(Optional = true)] public float MercHailChance = 0.5f;
    /// <summary>Mercenary price per minute, in event money units.</summary>
    [Serialize(Optional = true)] public float MercRateUnits = 0.2f;
    /// <summary>Most minutes that the slider offers for a mercenary contract.</summary>
    [Serialize(Optional = true)] public int MercMaxMinutes = 10;
    /// <summary>A mercenary below this health stops helping and runs. Lower than HiredFleeHealth: they are braver.</summary>
    [Serialize(Optional = true)] public float MercFleeHealth = 0.25f;
}

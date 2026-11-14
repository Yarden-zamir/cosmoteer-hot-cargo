using Halfling.Serialization;
using HotCargo.Ai;

namespace HotCargo.Tactics;

/// <summary>
/// Spec 43-49 and 360-369. Each tactic has an on/off switch and a minimum stage: a Heat.Stages name, or "" for no minimum.
/// The minimum stage applies to the hunter AI only: a hunter tactic works only while the current system has started it.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class TacticsTunables
{
    [Serialize(Optional = true)] public bool KiteEnabled = true;
    [Serialize(Optional = true)] public string KiteMinStage = "";
    /// <summary>A kiting ship holds its nearest edge at this fraction of its longest weapon range from the target's edge.</summary>
    [Serialize(Optional = true)] public float KiteRangeFraction = 0.9f;

    [Serialize(Optional = true)] public bool PincerEnabled = true;
    [Serialize(Optional = true)] public string PincerMinStage = "";
    /// <summary>Angle between the attack sides of two hunters on the same target. Capped at 360 / group size.</summary>
    [Serialize(Optional = true)] public float PincerAngleDegrees = 120f;

    [Serialize(Optional = true)] public bool DisableEnabled = true;
    [Serialize(Optional = true)] public string DisableMinStage = "Hunt";
    /// <summary>The current weapon or thruster target stays until another one is this many times closer.</summary>
    [Serialize(Optional = true)] public float DisableSwitchFactor = 1.5f;

    [Serialize(Optional = true)] public bool FallBackEnabled = true;
    [Serialize(Optional = true)] public string FallBackMinStage = "";
    /// <summary>A ship falls back below this health fraction (built value over blueprint value).</summary>
    [Serialize(Optional = true)] public float FallBackHealthFraction = 0.35f;
    /// <summary>A ship returns at this health fraction, or after FallBackMaxSeconds.</summary>
    [Serialize(Optional = true)] public float FallBackRecoverFraction = 0.6f;
    [Serialize(Optional = true)] public float FallBackMaxSeconds = 45f;
    /// <summary>Distance that a ship flies away from the nearest player ship before it repairs.</summary>
    [Serialize(Optional = true)] public float FallBackDistance = 1500f;
    /// <summary>
    /// A ship that falls back picks where it repairs (spec 1305): behind a rock, with resources from a donor ship, or at a friendly
    /// station. Off: it always flies FallBackDistance away and repairs with what it has.
    /// </summary>
    [Serialize(Optional = true)] public bool FallBackResupplyEnabled = true;
    /// <summary>Time limit of a fall back to a donor ship or a station. Cover and away use FallBackMaxSeconds.</summary>
    [Serialize(Optional = true)] public float FallBackTripSeconds = 180f;
    /// <summary>A ship with less crew than this fraction of its crew places goes to a station for crew.</summary>
    [Serialize(Optional = true)] public float FallBackCrewFraction = 0.5f;
    /// <summary>A ship within this distance of its cover spot holds there and repairs.</summary>
    [Serialize(Optional = true)] public float FallBackArriveDistance = 100f;
    /// <summary>Friendly AI ships within this edge-to-edge distance can give a falling back ship the resources it lacks.</summary>
    [Serialize(Optional = true)] public float FallBackDonorRange = 3000f;
    /// <summary>A donor gives at most this fraction of its stock of each resource.</summary>
    [Serialize(Optional = true)] public float FallBackDonorShare = 0.5f;
    /// <summary>A ship stops this far from the hull of a donor or a station. Must stay under the vanilla transfer distance (400).</summary>
    [Serialize(Optional = true)] public float FallBackDockGap = 150f;

    [Serialize(Optional = true)] public bool BaitEnabled = true;
    [Serialize(Optional = true)] public string BaitMinStage = "Strike";
    /// <summary>The bait closes to this edge-to-edge distance from the target.</summary>
    [Serialize(Optional = true)] public float BaitCloseDistance = 150f;
    /// <summary>The bait pulls back after this time, even if it did not get close.</summary>
    [Serialize(Optional = true)] public float BaitApproachSeconds = 20f;
    /// <summary>The bait stays near the group this long, then comes close again.</summary>
    [Serialize(Optional = true)] public float BaitPullBackSeconds = 12f;

    [Serialize(Optional = true)] public bool TeamEnabled = true;
    [Serialize(Optional = true)] public string TeamMinStage = "";
    /// <summary>Allies with the Team trait within this distance of a ship form its team.</summary>
    [Serialize(Optional = true)] public float TeamRadius = 2500f;
    /// <summary>Flankers attack from this many degrees left or right of the side where the tank attacks.</summary>
    [Serialize(Optional = true)] public float TeamFlankDegrees = 90f;

    [Serialize(Optional = true)] public bool CoverEnabled = true;
    [Serialize(Optional = true)] public string CoverMinStage = "";
    /// <summary>A ship looks for rocks to hide behind within this distance of itself.</summary>
    [Serialize(Optional = true)] public float CoverSearchRange = 2000f;
    /// <summary>The cover cycle runs only while the target is within this edge-to-edge distance.</summary>
    [Serialize(Optional = true)] public float CoverEngageRange = 1500f;
    /// <summary>A rock gives cover only when its radius is at least this fraction of the ship's radius.</summary>
    [Serialize(Optional = true)] public float CoverMinRockFactor = 0.5f;
    /// <summary>Space between the rock and the hiding ship.</summary>
    [Serialize(Optional = true)] public float CoverGap = 30f;
    [Serialize(Optional = true)] public float CoverPeekSeconds = 8f;
    [Serialize(Optional = true)] public float CoverHideSeconds = 6f;

    [Serialize(Optional = true)] public bool BrawlEnabled = true;
    [Serialize(Optional = true)] public string BrawlMinStage = "";
    /// <summary>Brawlers and team tanks close to this fraction of their longest weapon range, edge to edge.</summary>
    [Serialize(Optional = true)] public float BrawlRangeFraction = 0.5f;

    // Personalities (spec 360-369).

    /// <summary>
    /// On: every hostile ship uses the traits that its personality and build choose. Off: hunters use every tactic (spec 43-48)
    /// and the vanilla AIs use none.
    /// </summary>
    [Serialize(Optional = true)] public bool PersonalitiesEnabled = true;
    /// <summary>A trait is on when its jittered weight × build fit is at least this.</summary>
    [Serialize(Optional = true)] public float PersonalityThreshold = 0.45f;
    /// <summary>Personality names that are switched off. A ship rolls only among the others of its group.</summary>
    [Serialize(Optional = true)] public string[] PersonalitiesOff = [];
    /// <summary>A ship measures its build again after this long (damage changes it).</summary>
    [Serialize(Optional = true)] public float ProfileRefreshSeconds = 10f;
    /// <summary>Where each build number reaches its score 0 and 1 (spec 362).</summary>
    [Serialize(Optional = true)] public ProfileScaleTunables Profile = new();
    [Serialize(Optional = true)] public Personality[] Personalities = DefaultPersonalities();

    // Weak sides (spec 1030-1034).

    /// <summary>On: ships keep their strong side to the threat when they fly away or reposition, and a weak back or no guarded side blocks kite and bait.</summary>
    [Serialize(Optional = true)] public bool WeakSideEnabled = true;
    /// <summary>Part categories that a ship protects: its control rooms, reactors and the tech vault. Part IDs count as categories.</summary>
    [Serialize(Optional = true)] public string[] VulnerableParts = ["command", "reactor", "yarden_zamir.tech_vault"];
    /// <summary>A side is weak when a protected part has less cover than this toward it. One part cell gives 1, one armor cell ArmorCoverWeight.</summary>
    [Serialize(Optional = true)] public float WeakSideCover = 4f;
    /// <summary>Cover of one armor cell between a protected part and the hull edge. Other part cells give 1. Damage lowers both.</summary>
    [Serialize(Optional = true)] public float ArmorCoverWeight = 3f;
    /// <summary>Score of each weapon that can fire toward a side, when the ship picks the side to turn toward a threat.</summary>
    [Serialize(Optional = true)] public float WeaponSideWeight = 1f;

    // Enemy AI behaviors (spec 193-202). "Enabled" switches the behavior for hunters (HotCargoHunter AI),
    // "OnVanilla" for the vanilla combat AIs (Normal, Easy, Wanderer, StationGuard, Police: pirates, bounties, faction ships).

    [Serialize(Optional = true)] public bool FleeEnabled = true;
    [Serialize(Optional = true)] public bool FleeOnVanilla = true;
    /// <summary>A ship below this health fraction rolls FleeChance once to flee. A ship without offensive weapons always flees.</summary>
    [Serialize(Optional = true)] public float FleeHealthFraction = 0.25f;
    [Serialize(Optional = true)] public float FleeChance = 0.5f;
    /// <summary>A fleeing ship keeps at least this edge-to-edge distance from the nearest enemy.</summary>
    [Serialize(Optional = true)] public float FleeDistance = 1500f;
    /// <summary>A fleeing hunter jumps out of the system this long after it starts to flee.</summary>
    [Serialize(Optional = true)] public float FleeJumpSeconds = 25f;
    /// <summary>A fleeing vanilla ship repairs when clear and fights again at this health fraction, if it has weapons.</summary>
    [Serialize(Optional = true)] public float FleeRecoverFraction = 0.6f;

    [Serialize(Optional = true)] public bool CallEnabled = true;
    [Serialize(Optional = true)] public bool CallOnVanilla = true;
    /// <summary>
    /// A call for help takes this long before allies answer. One disruptor, EMP or ion beam hit stops it. Long enough that the
    /// players can jam the caller or kill it.
    /// </summary>
    [Serialize(Optional = true)] public float CallSeconds = 22f;
    /// <summary>
    /// A call takes this long instead of CallSeconds when, at its start, a human enemy ship has a CallInterceptParts part
    /// whose radar range reaches the caller (spec 1300).
    /// </summary>
    [Serialize(Optional = true)] public float CallInterceptSeconds = 30f;
    /// <summary>Part IDs that intercept a call early when their radar range reaches the caller: the call takes CallInterceptSeconds.</summary>
    [Serialize(Optional = true)] public string[] CallInterceptParts = ["cosmoteer.sensor_array"];
    /// <summary>Allies within this distance of the caller hear the call.</summary>
    [Serialize(Optional = true)] public float CallRange = 6000f;
    /// <summary>A ship that called, was stopped or answered a call does not call again for this long.</summary>
    [Serialize(Optional = true)] public float CallCooldownSeconds = 60f;
    /// <summary>An ally that answered a call hunts the caller's target for this long.</summary>
    [Serialize(Optional = true)] public float CallAssistSeconds = 60f;
    /// <summary>A ship that loses this health fraction between two AI checks took damage, and calls.</summary>
    [Serialize(Optional = true)] public float CallDamageTrigger = 0.02f;
    /// <summary>Nebula types that block calls: a caller or listener inside one at CallStormStrength or more cannot call or hear.</summary>
    [Serialize(Optional = true)] public string[] CallBlockedInNebulas = ["ion_storm", "electron"];
    [Serialize(Optional = true)] public float CallStormStrength = 0.5f;
    /// <summary>Part IDs whose damage stops a call. Any hit that drains battery power (disruptors, EMP missiles) stops a call too.</summary>
    [Serialize(Optional = true)] public string[] CallInterruptParts = ["cosmoteer.ion_beam_emitter", "cosmoteer.ion_beam_prism"];

    [Serialize(Optional = true)] public bool FocusEnabled = true;
    [Serialize(Optional = true)] public bool FocusOnVanilla = true;
    /// <summary>Attack value of a target × (1 + FocusWeakFactor × its lost health fraction). Higher focuses fire on weakened ships more.</summary>
    [Serialize(Optional = true)] public float FocusWeakFactor = 1.5f;

    [Serialize(Optional = true)] public bool RegroupEnabled = true;
    [Serialize(Optional = true)] public bool RegroupOnVanilla = true;
    /// <summary>A ship with less than this fraction of its target's firepower waits for allies before it attacks.</summary>
    [Serialize(Optional = true)] public float RegroupFirepowerRatio = 0.75f;
    /// <summary>Allies within this distance count as close.</summary>
    [Serialize(Optional = true)] public float RegroupRadius = 1200f;
    /// <summary>Only allies within this distance count as cover. Allies further away do not make the ship wait.</summary>
    [Serialize(Optional = true)] public float RegroupSearchRange = 5000f;
    /// <summary>The ship attacks once this many ships (itself included) are close, or all its allies are.</summary>
    [Serialize(Optional = true)] public int RegroupMinGroup = 2;
    /// <summary>The ship attacks anyway after this long, and does not regroup again for the same time.</summary>
    [Serialize(Optional = true)] public float RegroupMaxSeconds = 25f;

    /// <summary>Called from TunablesFile.Validate (spec 53).</summary>
    public void Validate(Action<bool, string> require)
    {
        require(FleeHealthFraction is >= 0 and <= 1 && FleeRecoverFraction is >= 0 and <= 1, "Tactics.FleeHealthFraction and FleeRecoverFraction must be 0 to 1");
        require(FleeChance is >= 0 and <= 1, "Tactics.FleeChance must be 0 to 1");
        require(CallSeconds > 0 && CallInterceptSeconds > 0 && CallRange > 0, "Tactics.CallSeconds, CallInterceptSeconds and CallRange must be above 0");
        require(CallStormStrength is > 0 and <= 1, "Tactics.CallStormStrength must be above 0 and at most 1");
        require(RegroupMinGroup >= 1, "Tactics.RegroupMinGroup must be 1 or more");
        require(FallBackTripSeconds > 0 && FallBackArriveDistance > 0 && FallBackDonorRange >= 0,
            "Tactics.FallBackTripSeconds and FallBackArriveDistance must be above 0, FallBackDonorRange 0 or more");
        require(FallBackCrewFraction is >= 0 and <= 1 && FallBackDonorShare is >= 0 and <= 1, "Tactics.FallBackCrewFraction and FallBackDonorShare must be 0 to 1");
        require(FallBackDockGap is >= 0 and < 400, "Tactics.FallBackDockGap must be 0 or more and below the vanilla transfer distance 400");
        require(TeamRadius > 0 && CoverSearchRange > 0 && CoverPeekSeconds > 0 && CoverHideSeconds > 0,
            "Tactics.TeamRadius, CoverSearchRange, CoverPeekSeconds and CoverHideSeconds must be above 0");
        require(BrawlRangeFraction is > 0 and <= 1, "Tactics.BrawlRangeFraction must be above 0 and at most 1");
        require(ProfileRefreshSeconds > 0, "Tactics.ProfileRefreshSeconds must be above 0");
        require(WeakSideCover > 0 && ArmorCoverWeight >= 0 && WeaponSideWeight >= 0,
            "Tactics.WeakSideCover must be above 0, ArmorCoverWeight and WeaponSideWeight 0 or more");
        var p = Profile;
        require(p.AccelAgile > p.AccelHeavy && p.TurnFast > p.TurnSlow && p.RangeLong > p.RangeShort && p.RadiusLarge > p.RadiusSmall
                && p.ArmorHeavy > p.ArmorLight && p.ShieldsPerRadiusFull > 0, "Tactics.Profile: each high value must be above its low value, ShieldsPerRadiusFull above 0");
        var names = new HashSet<string>();
        foreach (var personality in Personalities)
        {
            require(personality.Name.Length > 0 && names.Add(personality.Name), $"Tactics.Personalities: name '{personality.Name}' is empty or used twice");
            require(personality.Groups.Length > 0, $"Tactics.Personalities '{personality.Name}' needs Groups");
            require(personality.Chance >= 0 && personality.Jitter is >= 0 and <= 1, $"Tactics.Personalities '{personality.Name}': Chance must be 0 or more, Jitter 0 to 1");
            require(personality.Weights.ToArray().All(w => w is >= 0 and <= 1), $"Tactics.Personalities '{personality.Name}': weights must be 0 to 1");
        }
        foreach (string off in PersonalitiesOff)
            require(names.Contains(off), $"Tactics.PersonalitiesOff names '{off}', which is not a personality");
    }

    public ProfileScale Scale => new(Profile.AccelHeavy, Profile.AccelAgile, Profile.TurnSlow, Profile.TurnFast, Profile.RangeShort,
        Profile.RangeLong, Profile.ArmorLight, Profile.ArmorHeavy, Profile.ShieldsPerRadiusFull, Profile.RadiusSmall, Profile.RadiusLarge);

    /// <summary>
    /// Default personalities (spec 361). Groups: a faction ID, "pirates" (hostile ships of no faction) or "hunters" (the hunter AI).
    /// Weights in Trait order: Kite, Pincer, Disable, FallBack, Bait, Team, Cover, Brawl, Call.
    /// </summary>
    static Personality[] DefaultPersonalities() =>
    [
        new("Monolith Phalanx", ["monolith"], 2, new(Kite: 0.1f, Pincer: 0.3f, Disable: 0.3f, FallBack: 0.6f, Bait: 0f, Team: 1f, Cover: 0.2f, Brawl: 0.8f, Call: 0.9f)),
        new("Monolith Lancers", ["monolith"], 1, new(Kite: 0.7f, Pincer: 0.4f, Disable: 0.6f, FallBack: 0.5f, Bait: 0f, Team: 0.9f, Cover: 0.3f, Brawl: 0.1f, Call: 0.8f)),
        new("Cabal Zealots", ["cabal"], 2, new(Kite: 0.1f, Pincer: 0.8f, Disable: 0.2f, FallBack: 0.1f, Bait: 0.3f, Team: 0.3f, Cover: 0f, Brawl: 1f, Call: 0.5f)),
        new("Cabal Inquisitors", ["cabal"], 1, new(Kite: 0.3f, Pincer: 0.6f, Disable: 1f, FallBack: 0.3f, Bait: 0f, Team: 0.5f, Cover: 0.2f, Brawl: 0.4f, Call: 0.6f)),
        new("Imperial Line", ["imperium"], 2, new(Kite: 0.6f, Pincer: 0.5f, Disable: 0.4f, FallBack: 0.7f, Bait: 0f, Team: 1f, Cover: 0f, Brawl: 0.3f, Call: 1f)),
        new("Imperial Hussars", ["imperium"], 1, new(Kite: 0.5f, Pincer: 1f, Disable: 0.3f, FallBack: 0.5f, Bait: 0.7f, Team: 0.6f, Cover: 0.1f, Brawl: 0.2f, Call: 0.8f)),
        new("Fringe Skirmishers", ["fringe"], 2, new(Kite: 0.9f, Pincer: 0.4f, Disable: 0.2f, FallBack: 0.8f, Bait: 0.7f, Team: 0.1f, Cover: 0.9f, Brawl: 0.1f, Call: 0.3f)),
        new("Fringe Scrappers", ["fringe"], 1, new(Kite: 0.2f, Pincer: 0.5f, Disable: 0.3f, FallBack: 0.6f, Bait: 0.3f, Team: 0.1f, Cover: 0.6f, Brawl: 0.9f, Call: 0.3f)),
        new("Io Disruptors", ["io"], 2, new(Kite: 0.7f, Pincer: 0.3f, Disable: 1f, FallBack: 0.6f, Bait: 0f, Team: 0.5f, Cover: 0.6f, Brawl: 0.1f, Call: 0.7f)),
        new("Io Swarm", ["io"], 1, new(Kite: 0.3f, Pincer: 1f, Disable: 0.4f, FallBack: 0.3f, Bait: 0.6f, Team: 0.9f, Cover: 0.2f, Brawl: 0.5f, Call: 0.7f)),
        new("Pirate Raiders", ["pirates"], 2, new(Kite: 0.3f, Pincer: 0.8f, Disable: 0.5f, FallBack: 0.9f, Bait: 0.8f, Team: 0.2f, Cover: 0.6f, Brawl: 0.5f, Call: 0.5f)),
        new("Pirate Ambushers", ["pirates"], 1, new(Kite: 0.6f, Pincer: 0.3f, Disable: 0.3f, FallBack: 0.7f, Bait: 0.5f, Team: 0.3f, Cover: 1f, Brawl: 0.2f, Call: 0.6f)),
        new("Corporate Hunters", ["hunters"], 2, new(Kite: 0.6f, Pincer: 0.8f, Disable: 0.8f, FallBack: 0.7f, Bait: 0.7f, Team: 0.8f, Cover: 0.3f, Brawl: 0.3f, Call: 1f)),
        new("Corporate Sweepers", ["hunters"], 1, new(Kite: 0.2f, Pincer: 0.5f, Disable: 0.6f, FallBack: 0.5f, Bait: 0.2f, Team: 1f, Cover: 0.3f, Brawl: 0.8f, Call: 1f)),
    ];
}

/// <summary>
/// Spec 361. One AI personality: a weighted mix of traits for the ships of its groups. A ship of a group rolls one of the group's
/// personalities by Chance, then each trait weight moves by up to ±Jitter from the ship's seed.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class Personality
{
    [Serialize] public string Name = "";
    /// <summary>Faction IDs (monolith, cabal, imperium, fringe, io), "pirates" or "hunters".</summary>
    [Serialize] public string[] Groups = [];
    [Serialize(Optional = true)] public float Chance = 1f;
    [Serialize(Optional = true)] public float Jitter = 0.3f;
    [Serialize(Optional = true)] public TraitWeights Weights = new();

    public Personality() { }

    public Personality(string name, string[] groups, float chance, TraitWeights weights) =>
        (Name, Groups, Chance, Weights) = (name, groups, chance, weights);
}

/// <summary>Weight of each trait, 0 to 1. The field names are the Trait names.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class TraitWeights
{
    [Serialize(Optional = true)] public float Kite, Pincer, Disable, FallBack, Bait, Team, Cover, Brawl, Call;

    public TraitWeights() { }

    public TraitWeights(float Kite, float Pincer, float Disable, float FallBack, float Bait, float Team, float Cover, float Brawl, float Call) =>
        (this.Kite, this.Pincer, this.Disable, this.FallBack, this.Bait, this.Team, this.Cover, this.Brawl, this.Call) =
        (Kite, Pincer, Disable, FallBack, Bait, Team, Cover, Brawl, Call);

    public float Get(Trait trait) => trait switch
    {
        Trait.Kite => Kite, Trait.Pincer => Pincer, Trait.Disable => Disable, Trait.FallBack => FallBack, Trait.Bait => Bait,
        Trait.Team => Team, Trait.Cover => Cover, Trait.Brawl => Brawl, Trait.Call => Call,
        _ => throw new ArgumentOutOfRangeException(nameof(trait), trait, null),
    };

    public void Set(Trait trait, float value)
    {
        switch (trait)
        {
            case Trait.Kite: Kite = value; break;
            case Trait.Pincer: Pincer = value; break;
            case Trait.Disable: Disable = value; break;
            case Trait.FallBack: FallBack = value; break;
            case Trait.Bait: Bait = value; break;
            case Trait.Team: Team = value; break;
            case Trait.Cover: Cover = value; break;
            case Trait.Brawl: Brawl = value; break;
            case Trait.Call: Call = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(trait), trait, null);
        }
    }

    /// <summary>Weights indexed by Trait value.</summary>
    public float[] ToArray() => Enum.GetValues<Trait>().Select(Get).ToArray();
}

/// <summary>
/// Spec 362. Low and high ends of each build score. Units: acceleration in thrust per mass, turn in torque per inertia.
/// Defaults come from a census of the built-in combat ships (autotest command aicensus): acceleration 11 to 90 (median about 33),
/// turn 0.25 to 15 (median about 2), range 150 to 700, armor 0.02 to 0.46 of the cells, radius 5 to 39.
/// With AccelHeavy 20, about one built-in ship in ten is heavy, mostly big tier 10 to 12 ships.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class ProfileScaleTunables
{
    /// <summary>At or below this acceleration a ship is heavy: agility 0, it never kites or baits.</summary>
    [Serialize(Optional = true)] public float AccelHeavy = 20f;
    [Serialize(Optional = true)] public float AccelAgile = 50f;
    [Serialize(Optional = true)] public float TurnSlow = 0.5f;
    [Serialize(Optional = true)] public float TurnFast = 4f;
    [Serialize(Optional = true)] public float RangeShort = 220f;
    [Serialize(Optional = true)] public float RangeLong = 600f;
    /// <summary>Armor cells over all part cells: armor score 0 at ArmorLight, 1 at ArmorHeavy.</summary>
    [Serialize(Optional = true)] public float ArmorLight = 0.15f;
    [Serialize(Optional = true)] public float ArmorHeavy = 0.45f;
    /// <summary>Shield parts per unit of radius for a full shield score.</summary>
    [Serialize(Optional = true)] public float ShieldsPerRadiusFull = 0.25f;
    [Serialize(Optional = true)] public float RadiusSmall = 8f;
    [Serialize(Optional = true)] public float RadiusLarge = 35f;
}

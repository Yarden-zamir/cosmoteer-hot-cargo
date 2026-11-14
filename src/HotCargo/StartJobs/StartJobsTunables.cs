using Halfling.Serialization;
using HotCargo.RunModes;

namespace HotCargo.StartJobs;

/// <summary>Start jobs (spec 1200 to 1207): the choice at the run start that changes the run.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class StartJobsTunables
{
    /// <summary>Spec 1200: a new run offers start jobs in the run intro panel. Off: no offer, and the run plays as before.</summary>
    [Serialize(Optional = true)] public bool Enabled = true;
    /// <summary>Spec 1200: jobs in one offer. The Always jobs come first; a draw from the galaxy seed fills the rest.</summary>
    [Serialize(Optional = true)] public int OfferCount = 3;
    /// <summary>
    /// Spec 1201, 1206: the jobs that an offer draws from. Each ID needs the strings HotCargoStartJobs/(ID)Name and (ID)Text.
    /// Tech and Heat apply at the pick; the other rules apply while the run lasts.
    /// </summary>
    [Serialize(Optional = true)] public StartJobRow[] Jobs =
    [
        new() { Id = "contract", Always = true },
        new() { Id = "prototype", Tech = -4, TechValue = 1.75f },
        new() { Id = "deep_space", Sectors = 2, BuyerPrice = 1.4f },
        new() { Id = "rush", Sectors = -2, BuyerPrice = 0.7f },
        new() { Id = "loud_exit", Heat = 20f, TechValue = 1.25f },
        new() { Id = "grudge", HunterTiers = 1, BuyerPrice = 1.3f },
        new() { Id = "inside_man", HeatRate = 0.8f, BuyerPrice = 0.8f },
    ];

    /// <summary>Called from TunablesFile.Validate (spec 53, 1207).</summary>
    public void Validate(Action<bool, string> require)
    {
        require(OfferCount >= 1, "StartJobs.OfferCount must be 1 or more");
        require(Jobs.Count(j => j.Always) <= OfferCount, "StartJobs: more Always jobs than OfferCount");
        require(Jobs.Select(j => j.Id).Distinct().Count() == Jobs.Length, "StartJobs.Jobs has an ID twice");
        foreach (var j in Jobs)
        {
            string name = $"StartJobs job '{j.Id}'";
            require(j.Id.Length > 0, "StartJobs.Jobs needs an Id on each job");
            require(j.Tech <= 0, $"{name}: Tech must be 0 or less (a run builds no vault, spec 1014)");
            require(j.BuyerPrice > 0 && j.HunterShips > 0 && j.TechValue > 0, $"{name}: BuyerPrice, HunterShips and TechValue must be above 0");
            require(j.HeatRate >= 0 && j.ContractFees >= 0, $"{name}: HeatRate and ContractFees must be 0 or more");
        }
    }
}

/// <summary>Spec 1201: one start job. 0, and 1 for the factors, means no change.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class StartJobRow
{
    /// <summary>Job ID. The strings HotCargoStartJobs/(ID)Name and (ID)Text give its name and text.</summary>
    [Serialize] public string Id = "";
    /// <summary>True: every offer has this job, before the drawn ones.</summary>
    [Serialize(Optional = true)] public bool Always;
    /// <summary>Tech units added at the pick: 0 or less, because a run builds no vault (spec 1014).</summary>
    [Serialize(Optional = true)] public int Tech;
    /// <summary>Heat added at the pick.</summary>
    [Serialize(Optional = true)] public float Heat;
    /// <summary>Added to Journey.RunSectorCount for this run: the Buyer's sector moves. The run stays at least 1 sector.</summary>
    [Serialize(Optional = true)] public int Sectors;
    /// <summary>Times the Buyer unit price (spec 7), so also the score.</summary>
    [Serialize(Optional = true)] public float BuyerPrice = 1f;
    /// <summary>Times the heat from time and from fame (spec 681), like a difficulty row.</summary>
    [Serialize(Optional = true)] public float HeatRate = 1f;
    /// <summary>Times the ships of each hunter stage response (spec 682), like a difficulty row.</summary>
    [Serialize(Optional = true)] public float HunterShips = 1f;
    /// <summary>Added to the tier of each hunter stage response (spec 682), like a difficulty row.</summary>
    [Serialize(Optional = true)] public int HunterTiers;
    /// <summary>Times the contract accept fee (spec 682), like a difficulty row.</summary>
    [Serialize(Optional = true)] public float ContractFees = 1f;
    /// <summary>Times the tech value (spec 683), like a difficulty row.</summary>
    [Serialize(Optional = true)] public float TechValue = 1f;

    public Pressure ToPressure() => new(HeatRate, HunterShips, HunterTiers, ContractFees, TechValue);
}

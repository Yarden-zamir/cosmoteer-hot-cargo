using System.Security.Cryptography;
using Halfling;
using Halfling.ObjectText;

namespace HotCargo.Core;

/// <summary>
/// Reads tunables.rules and notices when it changes.
/// Polls the file time once per second. A file watcher is not needed at this rate.
/// </summary>
public static class TunablesFile
{
    static DateTime _lastWrite;
    static DateTime _nextPoll;

    public static string FilePath => Path.Combine(Mod.ModRoot, "tunables.rules");

    /// <summary>Error text of the last failed read, or null after a good read.</summary>
    public static string? LastError { get; private set; }

    /// <summary>Valid tunables, their short hash and the file bytes that they come from (the multiplayer input sends the bytes).</summary>
    public sealed record Loaded(Tunables Values, string Hash, byte[] Bytes);

    /// <summary>
    /// Reads the file. Returns null and sets LastError when the file is missing or invalid.
    /// A bad file is a player input error, not a mod bug: it logs as INFO, and the overlay shows LastError (spec 53).
    /// </summary>
    public static Loaded? Read()
    {
        try
        {
            if (!File.Exists(FilePath)) throw new FileNotFoundException("tunables.rules not found", FilePath);
            _lastWrite = File.GetLastWriteTimeUtc(FilePath);
            var loaded = Parse(File.ReadAllBytes(FilePath));
            LastError = null;
            return loaded;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Log.Info($"tunables rejected: {ex.Message}");
            return null;
        }
    }

    /// <summary>Writes the file and records its time, so that the change watcher does not read the same values back.</summary>
    public static void Write(byte[] bytes)
    {
        File.WriteAllBytes(FilePath, bytes);
        _lastWrite = File.GetLastWriteTimeUtc(FilePath);
        LastError = null;
    }

    /// <summary>Parses and validates the content of a tunables file. Throws when it is invalid.</summary>
    public static Loaded Parse(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var values = App.ObjectTextSerializer.Read<Tunables>(new OTFile(stream))
            ?? throw new InvalidDataException("tunables.rules is empty");
        Validate(values);
        return new Loaded(values, Convert.ToHexString(SHA256.HashData(bytes))[..8], bytes);
    }

    /// <summary>Counts the file as read without reading it, so only a later write counts as a change.</summary>
    public static void MarkSeen() => _lastWrite = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : default;

    /// <summary>True once per changed file write. Call once per frame.</summary>
    /// <summary>The next ChangedSinceLastRead checks the file at once. For autotests: the headless sim runs faster than the real-time poll.</summary>
    internal static void PollNow() => _nextPoll = DateTime.MinValue;

    public static bool ChangedSinceLastRead()
    {
        if (DateTime.UtcNow < _nextPoll) return false;
        _nextPoll = DateTime.UtcNow.AddSeconds(1);
        return File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) != _lastWrite;
    }

    /// <summary>Rejects values that would break sim logic later, so that the last valid values stay in effect (spec 53).</summary>
    public static void Validate(Tunables t)
    {
        static void Require(bool ok, string what) { if (!ok) throw new InvalidDataException(what); }
        var stages = t.Heat.Stages.Select(s => s.Name).ToList();
        Require(t.Heat.HeatMultiplierByUnits.Length > 0, "Heat.HeatMultiplierByUnits must not be empty");
        for (int i = 1; i < t.Heat.Stages.Length; i++)
            Require(t.Heat.Stages[i].Heat >= t.Heat.Stages[i - 1].Heat, "Heat.Stages must be ordered by Heat");
        Require(t.Heat.HeatCarryOverFraction is >= 0 and <= 1, "Heat.HeatCarryOverFraction must be 0 to 1");
        Require(t.Ux.CommsMaxScreenShare is >= 0.2f and <= 1, "Ux.CommsMaxScreenShare must be 0.2 to 1");
        foreach (string name in stages)
            Require(t.Hunters.Stages.Any(r => r.Name == name), $"Hunters.Stages has no response for heat stage '{name}'");
        foreach (var r in t.Hunters.Stages)
            Require(stages.Contains(r.Name), $"Hunters.Stages names '{r.Name}', which is not a Heat.Stages name");
        Require(t.Hunters.SpawnDistanceMin > 0 && t.Hunters.SpawnDistanceMax >= t.Hunters.SpawnDistanceMin, "Hunters.SpawnDistanceMin must be above 0 and at most SpawnDistanceMax");
        t.Hunters.Validate(Require);
        t.Tactics.Validate(Require);
        foreach (var tactic in Enum.GetValues<Tactics.Tactic>())
        {
            string min = Tactics.TacticGate.Settings(t.Tactics, tactic).MinStage;
            Require(min.Length == 0 || stages.Contains(min), $"Tactics.{tactic}MinStage '{min}' is not a Heat.Stages name");
        }
        Require(t.Tech.TechShipFraction > 0, "Tech.TechShipFraction must be above 0");
        Require(t.Tech.TechShipFractionPerGameHour >= 0 && t.Tech.TechShipFractionPer100Notoriety >= 0, "Tech value growth must not be negative: the value only goes up (spec 19)");
        Require(t.Tech.BuyBackFactor >= 1, "Tech.BuyBackFactor must be 1 or more, or players can sell and buy back for profit");
        var c = t.Contracts;
        Require(c.FeeMin >= 0 && c.FeeMax >= c.FeeMin, "Contracts.FeeMin must be 0 or more and at most FeeMax");
        Require(c.InformantChanceMin >= 0 && c.InformantChanceMax >= c.InformantChanceMin && c.InformantChanceMax <= 1, "Contracts.InformantChanceMin/Max must be 0 to 1, min at most max");
        Require(c.InformantDelayMin >= 0 && c.InformantDelayMax >= c.InformantDelayMin, "Contracts.InformantDelayMin must be 0 or more and at most InformantDelayMax");
        Require(c.MoneyRoundTo >= 1, "Contracts.MoneyRoundTo must be 1 or more");
        Require(c.Metatypes.All(m => m.Metatype.Length > 0), "Contracts.Metatypes entries need a Metatype");
        Require(t.Journey.RunSectorCount >= 1, "Journey.RunSectorCount must be 1 or more");
        Require(t.Journey.RewardScale > 0 && t.Journey.TechCostScale >= 0, "Journey.RewardScale must be above 0 and Journey.TechCostScale must not be negative");
        Require(t.Balance.StationFuelPerMillion >= 0 && t.Balance.StationFuelMinimum >= 0, "Balance.StationFuelPerMillion and StationFuelMinimum must not be negative");
        Require(t.Chains.MaxNewChainsPerSystem >= 0, "Chains.MaxNewChainsPerSystem must not be negative");
        Missions.Mechanics.MechanicsTunables.Validate(t.Mechanics);
        // Level IDs are checked once the game rules exist; the boot-time check of the defaults runs before that.
        t.RunModes.Validate(Require, Cosmoteer.GameApp.Assets?.Rules is null ? null : RunModes.Difficulty.IsVanillaLevel);
        t.Factions.Validate(Require);
        t.Relays.Validate(Require);
        t.Debris.Validate(Require);
        t.Stash.Validate(Require);
        t.Telemetry.Validate(Require);
        t.Pursuit.Validate(Require, stages);
        t.StartJobs.Validate(Require);
    }
}

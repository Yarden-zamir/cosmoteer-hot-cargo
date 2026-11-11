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

    public sealed record Loaded(Tunables Values, string Hash);

    /// <summary>Reads the file. Returns null and sets LastError when the file is missing or invalid.</summary>
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
            Log.Error("tunables read failed", ex);
            return null;
        }
    }

    /// <summary>Reads the file bytes or null when the file is missing. For the host's tunables input in multiplayer.</summary>
    public static byte[]? ReadBytes() => File.Exists(FilePath) ? File.ReadAllBytes(FilePath) : null;

    /// <summary>Parses and validates the content of a tunables file. Throws when it is invalid.</summary>
    public static Loaded Parse(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var values = App.ObjectTextSerializer.Read<Tunables>(new OTFile(stream))
            ?? throw new InvalidDataException("tunables.rules is empty");
        Validate(values);
        return new Loaded(values, Convert.ToHexString(SHA256.HashData(bytes))[..8]);
    }

    /// <summary>True once per changed file write. Call once per frame.</summary>
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
        foreach (string name in stages)
            Require(t.Hunters.Stages.Any(r => r.Name == name), $"Hunters.Stages has no response for heat stage '{name}'");
        foreach (var r in t.Hunters.Stages)
            Require(stages.Contains(r.Name), $"Hunters.Stages names '{r.Name}', which is not a Heat.Stages name");
        Require(t.Hunters.SpawnDistanceMin > 0 && t.Hunters.SpawnDistanceMax >= t.Hunters.SpawnDistanceMin, "Hunters.SpawnDistanceMin must be above 0 and at most SpawnDistanceMax");
        foreach (var tactic in Enum.GetValues<Tactics.Tactic>())
        {
            string min = Tactics.TacticGate.Settings(t.Tactics, tactic).MinStage;
            Require(min.Length == 0 || stages.Contains(min), $"Tactics.{tactic}MinStage '{min}' is not a Heat.Stages name");
        }
        Require(t.Tech.VaultDamagePerUnitLost > 0, "Tech.VaultDamagePerUnitLost must be above 0");
        Require(t.Tech.BuyBackFactor >= 1, "Tech.BuyBackFactor must be 1 or more, or players can sell and buy back for profit");
        Require(t.Journey.RunSectorCount >= 1, "Journey.RunSectorCount must be 1 or more");
    }
}

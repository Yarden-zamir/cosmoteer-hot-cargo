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
            byte[] bytes = File.ReadAllBytes(FilePath);
            using var stream = new MemoryStream(bytes);
            var values = App.ObjectTextSerializer.Read<Tunables>(new OTFile(stream))
                ?? throw new InvalidDataException("tunables.rules is empty");
            Validate(values);
            LastError = null;
            return new Loaded(values, Convert.ToHexString(SHA256.HashData(bytes))[..8]);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Log.Error("tunables read failed", ex);
            return null;
        }
    }

    /// <summary>True once per changed file write. Call once per frame.</summary>
    public static bool ChangedSinceLastRead()
    {
        if (DateTime.UtcNow < _nextPoll) return false;
        _nextPoll = DateTime.UtcNow.AddSeconds(1);
        return File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) != _lastWrite;
    }

    static void Validate(Tunables t)
    {
        if (t.Heat.HeatMultiplierByUnits.Length == 0) throw new InvalidDataException("Heat.HeatMultiplierByUnits must not be empty");
        for (int i = 1; i < t.Heat.Stages.Length; i++)
            if (t.Heat.Stages[i].Heat < t.Heat.Stages[i - 1].Heat) throw new InvalidDataException("Heat.Stages must be ordered by Heat");
        if (t.Heat.HeatCarryOverFraction is < 0 or > 1) throw new InvalidDataException("Heat.HeatCarryOverFraction must be 0 to 1");
    }
}

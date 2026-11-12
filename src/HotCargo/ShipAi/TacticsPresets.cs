using Cosmoteer;

namespace HotCargo.ShipAi;

/// <summary>
/// Spec 422. The player's own named tactics presets: a local preference file next to the game settings, never sim state.
/// Each player has their own list; a preset changes a ship only through the tactics input, like any other edit.
/// </summary>
internal static class TacticsPresets
{
    static string FilePath => (Paths.RootSavePath / "hot_cargo_tactics_presets.txt").ToString();

    /// <summary>The presets in file order. A bad line is skipped and logged; the file stays as it is until the next save.</summary>
    public static List<(string Name, string Text)> Load()
    {
        if (!File.Exists(FilePath)) return [];
        var (presets, skipped) = ShipAiModel.ParsePresets(File.ReadAllText(FilePath));
        if (skipped > 0) Log.Info($"tactics presets: skipped {skipped} bad lines in {FilePath}");
        return presets;
    }

    /// <summary>Saves a preset. A preset with the same name is replaced in place. Returns the clean name.</summary>
    public static string Save(string name, string text)
    {
        string clean = ShipAiModel.CleanPresetName(name);
        if (clean.Length == 0) throw new ArgumentException("a preset needs a name", nameof(name));
        ShipAiModel.Decode(text);
        var presets = Load();
        int at = presets.FindIndex(p => p.Name == clean);
        if (at >= 0) presets[at] = (clean, text);
        else presets.Add((clean, text));
        Write(presets);
        return clean;
    }

    public static bool Delete(string name)
    {
        var presets = Load();
        if (presets.RemoveAll(p => p.Name == name) == 0) return false;
        Write(presets);
        return true;
    }

    static void Write(List<(string Name, string Text)> presets)
    {
        File.WriteAllText(FilePath, ShipAiModel.FormatPresets(presets));
        Log.Info($"tactics presets: {presets.Count} saved to {FilePath}");
    }
}

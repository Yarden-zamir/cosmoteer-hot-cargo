using System.Collections.Concurrent;
using Halfling.IO;
using Halfling.ObjectText;
using HarmonyLib;

/// <summary>
/// The game's rules refer to some files with another letter case than the file has (for example "&lt;tractor_Beam.rules&gt;"
/// for tractor_beam.rules). Windows and macOS file systems ignore case; Linux (CI) does not. On Linux, a rules file reference
/// that does not exist as written resolves to the file whose path matches without case.
/// Limit: only rules references (OTFile.GetReferencedFile). Images and sounds are placeholders in the sim anyway. Revisit if
/// another file kind fails only on Linux with a "not found" error.
/// </summary>
static class CaseInsensitivePaths
{
    static readonly ConcurrentDictionary<string, string[]> Entries = new();

    public static void Apply(Harmony harmony)
    {
        if (!OperatingSystem.IsLinux()) return;
        harmony.Patch(AccessTools.Method(typeof(OTFile), nameof(OTFile.GetReferencedFile)),
            prefix: new HarmonyMethod(typeof(CaseInsensitivePaths), nameof(GetReferencedFilePrefix)));
    }

    /// <summary>Replaces a reference that does not exist as written with the path that matches it without case.</summary>
    static void GetReferencedFilePrefix(OTFile __instance, ref FilePath file)
    {
        string? full = file.IsRooted || file.IsExplicitCurrentDirectory ? (string)new AbsolutePath(file)
            : __instance.FilePath is { } own ? (string)new AbsolutePath(own.Directory, file) : null;
        if (full is null || File.Exists(full)) return;
        if (Resolve(full) is { } match) file = new AbsolutePath(match);
    }

    /// <summary>The existing path that matches the path without case, segment by segment, or null.</summary>
    public static string? Resolve(string path)
    {
        string full = Path.GetFullPath(path);
        string current = Path.GetPathRoot(full) ?? "/";
        foreach (string part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            string exact = Path.Combine(current, part);
            if (File.Exists(exact) || Directory.Exists(exact)) { current = exact; continue; }
            if (!Directory.Exists(current)) return null;
            string[] names = Entries.GetOrAdd(current, dir => Directory.GetFileSystemEntries(dir).Select(Path.GetFileName).OfType<string>().ToArray());
            string? found = names.FirstOrDefault(n => string.Equals(n, part, StringComparison.OrdinalIgnoreCase));
            if (found is null) return null;
            current = Path.Combine(current, found);
        }
        return current;
    }
}

using Cosmoteer.Generators.Simulation;
using Halfling;
using Halfling.ObjectText;

namespace HotCargo.Worlds;

/// <summary>
/// Spawners in mod/worlds/spawns.rules, read on first use. The file is not part of the game rules tree.
/// Limit: it cannot use game root references (&amp;/...). Revisit if a spawn needs them.
/// </summary>
static class WorldsSpawns
{
    static readonly Dictionary<string, SimulationGenerator> Cache = new(StringComparer.Ordinal);

    public static string FilePath => Path.Combine(Mod.ModRoot, "worlds", "spawns.rules");

    public static SimulationGenerator Get(string name)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var g)) return g;
            g = App.ObjectTextSerializer.ReadFromPath<SimulationGenerator>(new OTFile(FilePath), name)
                ?? throw new InvalidDataException($"{name} missing in {FilePath}");
            if (g.Spawners is not { Length: > 0 }) throw new InvalidDataException($"{name}/Spawners is empty in {FilePath}");
            Cache[name] = g;
            return g;
        }
    }
}

using Cosmoteer;
using Halfling;
using Cosmoteer.Generators.Simulation;
using Halfling.ObjectText;
using HotCargo.Core;

namespace HotCargo.Missions;

/// <summary>
/// An ambush spawner in a rules file in the mod's missions folder, under the root node "Ambush". It is not part of the game rules tree.
/// The shady trap (spec 40) and the convoy raiders (spec 36) spawn from these files when they spring.
/// </summary>
internal sealed class AmbushFile(string fileName)
{
    public static readonly AmbushFile Shady = new("shady_ambush.rules");
    public static readonly AmbushFile Convoy = new("convoy_ambush.rules");

    SimulationGenerator? _generator;

    public string FilePath => Path.Combine(Mod.ModRoot, "missions", fileName);

    public SimulationGenerator Generator => _generator ??= Load();

    SimulationGenerator Load()
    {
        var generator = App.ObjectTextSerializer.ReadFromPath<SimulationGenerator>(new OTFile(FilePath), "Ambush")
            ?? throw new InvalidDataException($"Ambush missing in {FilePath}");
        if (generator.Spawners is not { Length: > 0 }) throw new InvalidDataException($"Ambush/Spawners is empty in {FilePath}");
        Log.Info($"ambush loaded from {fileName}: {generator.Spawners.Length} spawners");
        return generator;
    }
}

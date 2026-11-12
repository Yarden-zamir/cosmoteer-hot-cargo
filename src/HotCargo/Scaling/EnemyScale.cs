using Cosmoteer.Game;
using Cosmoteer.Modes.Career;
using Cosmoteer.Simulation;
using Halfling.Random;
using HotCargo.Core;

namespace HotCargo.Scaling;

/// <summary>
/// Co-op enemy scaling (spec 330 to 336). Hostile ship counts × <see cref="Factor"/>.
/// Rules spawns go through <see cref="SpawnScalePatches"/>. C# spawns of hostile ships call <see cref="Count(int, SimRoot)"/>.
/// </summary>
public static class EnemyScale
{
    /// <summary>Test only: replaces the human player count, so a single-player autotest can check 2 and 3 players.</summary>
    internal static int? TestPlayers;

    /// <summary>
    /// The setting of the game: the run state once the run is active. Before that (the first system of a new career) it is the
    /// new-game value in <see cref="HotCargoCareerInit"/>. A game without either (mod off, or a test career) has no scaling.
    /// </summary>
    public static float Setting(GameRoot game) =>
        Run.Current is { } run && run.Game == game ? run.State.EnemyScale
        : game.Mode is CareerGameModeManager { Init: HotCargoCareerInit init } ? init.EnemyScale
        : ScalingModel.MinSetting;

    /// <summary>Human players, counted per person: two players who share a team count as two. Lockstep state, the same on every machine.</summary>
    public static int Players(GameRoot game) => TestPlayers ?? game.HumanPlayerCount;

    public static double Factor(SimRoot sim) => ScalingModel.Factor(Setting(sim.Game), Players(sim.Game));

    /// <summary>A hostile ship count for a C# spawn, scaled for the player count. Sim logic only: it rolls sim.Rand for a fraction.</summary>
    public static int Count(int count, SimRoot sim) => Count(count, sim, sim.Rand);

    internal static int Count(int count, SimRoot sim, Rand rand) => ScalingModel.Scale(count, Factor(sim), rand.Double);

    /// <summary>A hostile ship cap that the game checks again every tick. Rounds half up, without a roll.</summary>
    public static int Cap(int count, SimRoot sim) => ScalingModel.Cap(count, Factor(sim));
}

using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Music;
using Halfling.Application;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.RunModes;

/// <summary>
/// Spec 688: during the last heat stage the music plays its battle layers. Vanilla music is one FSM of track clusters
/// (Data/music); each track has a battle layer with the condition "battle == True or wanted == True", and the fade-out waits while
/// battle is true. MusicParameters.Update sets battle each frame from the ships in sight. This sets it to true while the run is at
/// the last stage. Local audio only: it reads run state and writes no sim state.
/// </summary>
[HarmonyPatch(typeof(MusicParameters), nameof(MusicParameters.Update))]
static class StrikeMusic
{
    /// <summary>True while the override holds. For autotests and the log.</summary>
    internal static bool Active { get; private set; }

    static void Postfix(MusicParameters __instance, IAppState state) => Errors.Guard("strike music", () =>
    {
        bool on = state is GameRoot game && Run.Current is { } run && run.Game == game && AtLastStage(run);
        if (on) __instance.Battle = true;
        if (on == Active) return;
        Active = on;
        Log.Info($"music: strike override {(on ? "on, battle layers" : "off, vanilla")}; track {TrackPath()}");
    });

    static bool AtLastStage(Run run) =>
        run.T.RunModes.StrikeMusic && run.PressureActive && run.T.Heat.Stages.Length > 0 && run.State.StagesStarted == run.T.Heat.Stages.Length;

    /// <summary>The chain of playing tracks, for the log. The audio thread changes the tracks, so this is a snapshot.</summary>
    internal static string TrackPath()
    {
        if (GameApp.Music is not { } music) return "(no music stream)";
        var names = new List<string>();
        MusicTrack? track = music._rootTrack, last = null;
        while (track is not null)
        {
            names.Add(track.DebugName);
            last = track;
            track = track is MusicFsmTrack fsm ? fsm._curTrack : null;
        }
        // The conditional layers (build, battle) whose condition holds now: these play on top of the base layers.
        var layers = last is MusicLayersTrack l
            ? l._layers.Where(x => x.Rules.DynamicCondition?.Invoke(music.Parameters) == true).Select(x => x.DebugName).ToList()
            : [];
        return string.Join(" > ", names) + (layers.Count > 0 ? $" + [{string.Join(", ", layers)}]" : "");
    }
}

using Cosmoteer.Game;
using Cosmoteer.Modes.Career;
using Cosmoteer.Simulation;
using Halfling.Events;
using HotCargo.RunModes;

namespace HotCargo.Core;

/// <summary>
/// The Hot Cargo state of the running career game. One instance for each game.
/// Simulation logic runs only from deterministic sources: sim ticks, fame changes and system switches.
/// </summary>
public sealed class Run
{
    public static Run? Current { get; private set; }

    /// <summary>Area hooks. Each area sets its hook in its Register method, called from Mod.Boot.</summary>
    public static class Hooks
    {
        /// <summary>Counts tech units that human players hold in the current simulation. Set by the Tech area.</summary>
        public static Func<SimRoot, int> CountFleetTech = _ => 0;
        /// <summary>Runs inside the sim tick when heat crosses a stage threshold. Arguments: run, sim, stage index.</summary>
        public static event Action<Run, SimRoot, int>? StageStarted;
        /// <summary>Runs inside the sim tick for each repeat wave of the last stage.</summary>
        public static event Action<Run, SimRoot>? StrikeWave;
        /// <summary>Runs after the players switch to another system. Argument firstVisit.</summary>
        public static event Action<Run, bool>? SystemEntered;
        /// <summary>Runs once per new career, after the starting ship exists.</summary>
        public static event Action<Run>? NewRunStarted;
        /// <summary>Runs inside each sim tick while the run is Running, after FleetTech is updated. Arguments: run, sim, tick seconds.</summary>
        public static event Action<Run, SimRoot, double>? Tick;
        /// <summary>Runs once when the run is won or lost, after State.Result and State.EndReason are set. Sim logic.</summary>
        public static event Action<Run>? RunEnded;

        internal static void RaiseRunEnded(Run r) => Each(RunEnded, h => h(r));
        internal static void RaiseStageStarted(Run r, SimRoot s, int i) => Each(StageStarted, h => h(r, s, i));
        internal static void RaiseStrikeWave(Run r, SimRoot s) => Each(StrikeWave, h => h(r, s));
        internal static void RaiseSystemEntered(Run r, bool first) => Each(SystemEntered, h => h(r, first));
        internal static void RaiseNewRunStarted(Run r) => Each(NewRunStarted, h => h(r));
        internal static void RaiseTick(Run r, SimRoot s, double dt) => Each(Tick, h => h(r, s, dt));

        /// <summary>Calls each handler on its own, so a failure in one area does not skip the hooks of the other areas.</summary>
        static void Each<T>(T? hooks, Action<T> call) where T : Delegate
        {
            if (hooks is null) return;
            foreach (T h in hooks.GetInvocationList().Cast<T>())
                Errors.Guard($"hook {h.Method.DeclaringType?.Name}.{h.Method.Name}", () => call(h));
        }
    }

    public GameRoot Game { get; }
    public CareerGameModeManager Mode { get; }
    public RunState State { get; }
    public Tunables T => State.Tunables;
    public bool IsMultiplayer => Game.NetManager.IsMultiplayer;

    ObjectID? _nodeBeforeSwitch;

    /// <summary>A new multiplayer run waits for the host to send its tunables (TunablesInput). Local, not saved.</summary>
    internal bool HostTunablesPending;

    /// <summary>The host sent its file and the input has not executed yet. The settings menu waits, so its edit does not start from the defaults.</summary>
    internal bool HostTunablesInFlight;

    Run(GameRoot game, CareerGameModeManager mode, RunState state)
    {
        Game = game;
        Mode = mode;
        State = state;
    }

    /// <summary>State read from the save by the load constructor patch, waiting for OnGameStarting.</summary>
    internal static RunState? PendingLoadedState;

    internal static void Start(CareerGameModeManager mode, bool loaded)
    {
        Current = null;
        RunState? state = loaded ? PendingLoadedState : null;
        PendingLoadedState = null;
        if (!Activation.IsModEnabled())
        {
            Log.Info("Hot Cargo mod is not enabled in the game's mod list; staying inactive");
            return;
        }
        // A save from before the mod was enabled has no state. The run then starts at this point of the career, like a new run.
        bool newRun = state is null;
        if (state is null)
        {
            if (loaded) Log.Info("save has no Hot Cargo state; a new run starts at this point of the career");
            state = new RunState();
            // In multiplayer each machine builds the new game itself and has its own tunables.rules. So every machine starts
            // with the defaults, and the host sends its file as an MP input on its first frame (TunablesInput).
            var file = mode.Game.NetManager.IsMultiplayer ? null : TunablesFile.Read();
            if (file is not null) { state.Tunables = file.Values; state.TunablesHash = file.Hash; }
            else state.TunablesHash = "defaults";
        }
        var run = new Run(mode.Game, mode, state) { HostTunablesPending = newRun && mode.Game.NetManager.IsMultiplayer };
        Current = run;
        // Validates the vanilla tier table now, so that a data problem shows at game start and not mid-run.
        GameData.StandardMissionFame(1);
        GameData.TierValueMaximum(1);
        run.State.VisitedNodes.Add(mode.Game.CurrentMapNode.UniqueID);
        mode.Game.EventHub.Tick += run.OnTick;
        mode.Game.EventHub.PlayerFameChanged += run.OnFameChanged;
        mode.Game.Popping += (_, _) => Stop(mode.Game);
        Log.Info($"run active: loaded={loaded} sector={state.SectorIndex} heat={state.Heat:0.0} tunables={state.TunablesHash} mp={run.IsMultiplayer}");
        if (newRun) Hooks.RaiseNewRunStarted(run);
    }

    internal static void Stop(GameRoot game)
    {
        if (Current?.Game != game) return;
        game.EventHub.Tick -= Current.OnTick;
        game.EventHub.PlayerFameChanged -= Current.OnFameChanged;
        Current = null;
    }

    public bool PressureActive => State.Result == RunResult.Running && State.HeatEnabled;

    public int CurrentTier => Mode.CurrentMapNodeData.TierRange?.Min ?? 1;

    void OnTick(object? sender, SimRoot sim)
    {
        if (sim != Game.Sim || State.Result != RunResult.Running) return;
        double dt = sim.FixedUpdater.Interval.Seconds;
        State.GameSeconds += dt;
        State.FleetTech = Hooks.CountFleetTech(sim);
        if (CurrentTier > State.PeakTier)
            State.PeakTier = Math.Max(State.PeakTier, Math.Min(CurrentTier, Journey.TierCapSpawner.PeakTierCap(Mode, State.SectorIndex) ?? int.MaxValue));
        Hooks.RaiseTick(this, sim, dt);
        if (State.Result != RunResult.Running || !State.HeatEnabled) return;
        AddHeat(HeatModel.TimeHeat(T.Heat.HeatPerGameMinute, HeatMultiplier(State.FleetTech), dt), sim);
        TickStrikeWaves(sim, dt);
    }

    void OnFameChanged(object? sender, ValueChangedEventArgs<int> e)
    {
        if (!PressureActive) return;
        int gained = e.NewValue - e.OldValue;
        if (gained <= 0) return;
        AddHeat(HeatModel.FameHeat(T.Heat.HeatPerStandardMission, HeatMultiplier(State.FleetTech), gained, (int)Math.Round(StandardMissionFamePaid(CurrentTier))), Game.Sim);
    }

    /// <summary>
    /// Multiplier of time and fame heat (spec 24, 25) for a fleet tech count: the tech table (spec 26) × the difficulty heat rate (spec 681).
    /// Every heat gain from time or fame and every shown heat rate uses it.
    /// </summary>
    public float HeatMultiplier(int units) => HeatModel.Multiplier(T.Heat.HeatMultiplierByUnits, units) * this.Pressure().HeatRate;

    /// <summary>
    /// Fame that a standard mission of the tier pays: the vanilla table value times the same factors as the payout
    /// (difficulty fame factor and RewardScale, see Journey/RewardScale.cs). So "heat per standard mission" holds on every difficulty.
    /// </summary>
    public float StandardMissionFamePaid(int tier) =>
        GameData.StandardMissionFame(tier) * Mode.EconDifficulty.FameRewardFactor * T.Journey.RewardScale;

    /// <summary>Adds heat and starts any stages it crosses. Call only from simulation logic (or single-player debug keys).</summary>
    public void AddHeat(float amount, SimRoot sim)
    {
        float before = State.Heat;
        State.Heat = HeatModel.Clamp(State.Heat + amount);
        // Spec 19: notoriety sums every real heat gain. It never goes down, so the tech value only ratchets up.
        if (State.Heat > before) State.Notoriety += State.Heat - before;
        int reached = HeatModel.StagesReached(State.Heat, T.Heat.Thresholds());
        while (State.StagesStarted < reached)
        {
            int stage = State.StagesStarted++;
            Log.Info($"stage {stage} ({T.Heat.Stages[stage].Name}) at heat {State.Heat:0.0}");
            Hooks.RaiseStageStarted(this, sim, stage);
        }
        // Heat can drop below thresholds (debug keys). Stages then re-arm.
        if (State.StagesStarted > reached) State.StagesStarted = reached;
    }

    /// <summary>Starts the next stage at once, by raising heat to its threshold.</summary>
    public void ForceNextStage(SimRoot sim)
    {
        if (State.StagesStarted >= T.Heat.Stages.Length) { Hooks.RaiseStrikeWave(this, sim); return; }
        AddHeat(Math.Max(0f, T.Heat.Stages[State.StagesStarted].Heat - State.Heat), sim);
    }

    void TickStrikeWaves(SimRoot sim, double dt)
    {
        bool atTop = T.Heat.Stages.Length > 0 && State.StagesStarted == T.Heat.Stages.Length && State.Heat >= HeatModel.MaxHeat;
        if (!atTop) { State.SecondsSinceStrikeWave = 0; return; }
        State.SecondsSinceStrikeWave += dt;
        if (State.SecondsSinceStrikeWave < T.Hunters.StrikeWaveIntervalSeconds) return;
        State.SecondsSinceStrikeWave = 0;
        Log.Info("strike wave");
        Hooks.RaiseStrikeWave(this, sim);
    }

    internal void BeforeSystemSwitch() => _nodeBeforeSwitch = Game.CurrentMapNode?.UniqueID;

    internal void AfterSystemSwitch()
    {
        ObjectID newNode = Game.CurrentMapNode.UniqueID;
        if (_nodeBeforeSwitch is not { } oldNode || oldNode.Equals(newNode)) return;
        _nodeBeforeSwitch = null;
        State.NodeHeat[oldNode] = State.Heat;
        bool firstVisit = State.VisitedNodes.Add(newNode);
        if (firstVisit) State.SectorIndex++;
        float stored = State.NodeHeat.TryGetValue(newNode, out float h) ? h : 0f;
        State.Heat = HeatModel.OnEnterSystem(State.Heat, stored, T.Heat.HeatCarryOverFraction);
        // Stages below the entry heat do not fire again on arrival. This gives the players a moment after each jump.
        State.StagesStarted = HeatModel.StagesReached(State.Heat, T.Heat.Thresholds());
        State.SecondsSinceStrikeWave = 0;
        Log.Info($"entered system {Game.CurrentMapNode.Name}: sector={State.SectorIndex} first={firstVisit} heat={State.Heat:0.0}");
        Hooks.RaiseSystemEntered(this, firstVisit);
    }

    /// <summary>Ends the run as lost. Deterministic callers only (sim logic).</summary>
    public void Lose(string reason)
    {
        if (State.Result != RunResult.Running) return;
        if (Dev.Autotest.GodMode) { Log.Info($"run loss blocked by autotest godmode: {reason}"); return; }
        State.Result = RunResult.Lost;
        State.EndReason = reason;
        Log.Info($"run lost: {reason}");
        Hooks.RaiseRunEnded(this);
    }

    /// <summary>Ends the run as won. Heat stops; the career can continue.</summary>
    public void Win(string reason)
    {
        if (State.Result != RunResult.Running) return;
        State.Result = RunResult.Won;
        State.HeatEnabled = false;
        State.EndReason = reason;
        Log.Info($"run won: {reason}");
        Hooks.RaiseRunEnded(this);
    }

    /// <summary>
    /// Runs a GUI action on the main thread at the end of the next frame. Use it for GUI work from hooks that can run while
    /// a game is built: a multiplayer launch, resync or load builds the game on a worker thread.
    /// </summary>
    public static void OnNextFrame(Action action)
    {
        EventHandler<EventArgs>? handler = null;
        handler = (_, _) =>
        {
            Halfling.App.Director.FrameEnded -= handler;
            Errors.Guard("next frame action", action);
        };
        Halfling.App.Director.FrameEnded += handler;
    }

    /// <summary>Replaces the tunables of the run. Deterministic callers only: single-player reload or the host's MP input.</summary>
    internal void ApplyTunables(TunablesFile.Loaded file)
    {
        State.Tunables = file.Values;
        State.TunablesHash = file.Hash;
        // New stage thresholds re-baseline like a system entry: stages at or below the current heat count as started and
        // spawn nothing, higher stages wait for heat. This keeps StagesStarted inside a shorter Heat.Stages table too.
        int reached = HeatModel.StagesReached(State.Heat, T.Heat.Thresholds());
        if (reached != State.StagesStarted) Log.Info($"tunables: stages started {State.StagesStarted} -> {reached} for the new thresholds");
        State.StagesStarted = reached;
        Log.Info($"tunables in effect: {file.Hash}");
    }

    /// <summary>Single player only: swaps in new tunables from the file.</summary>
    internal void ReloadTunables()
    {
        if (IsMultiplayer) return;
        var file = TunablesFile.Read();
        if (file is null) return;
        ApplyTunables(file);
        Game.Gui.ShowAreaName("Hot Cargo tunables reloaded");
    }
}

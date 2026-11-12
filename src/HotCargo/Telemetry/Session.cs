using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Game;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Stasis;
using Halfling.Events;
using Halfling.Serialization;
using HarmonyLib;
using HotCargo.Contracts;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Journey;
using HotCargo.Tech;

namespace HotCargo.Telemetry;

/// <summary>Telemetry data that the career save keeps (path HotCargoTelemetry), so a loaded game continues the same run ID.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class TelemetrySaveData
{
    [Serialize] public string RunId = "";
    [Serialize(Optional = true)] public int Sessions;
}

/// <summary>
/// One telemetry session: one game from its start or load until it closes. It writes one JSONL file (TELEMETRY.md).
/// It reads game state from the sim tick, game events and the patches in TelemetryPatches. It never changes game state,
/// so co-op machines can differ in telemetry settings without a desync.
/// </summary>
sealed class Session
{
    public static Session? Current { get; private set; }

    /// <summary>Set by the load constructor patch, read by the next Start.</summary>
    internal static TelemetrySaveData? PendingLoaded;

    /// <summary>Set by Run.Hooks.NewRunStarted, which runs inside Run.Start before the session starts.</summary>
    internal static bool NewRunPending;

    public Run Run { get; }
    public GameRoot Game { get; }
    public string RunId { get; }
    public string SessionId { get; }
    public int SessionNumber { get; }
    public string FilePath { get; }
    readonly Stopwatch _real = Stopwatch.StartNew();
    public double RealSeconds => _real.Elapsed.TotalSeconds;
    public double SimSeconds => _simSeconds;
    TelemetryTunables T => Run.T.Telemetry;
    TelemetryTunables? _maskFrom;

    // ---- Context, cached so that events from any thread read only plain fields.
    string _system = "", _arch = "";
    int _tier;
    /// <summary>Sim seconds of this session, summed from ticks. Unlike RunState.GameSeconds it keeps counting after a win.</summary>
    double _simSeconds;

    // ---- Change detection
    RunResult _result;
    int _fleetTech = -1, _players;
    int _pendingSerial, _resultSerial;
    readonly Dictionary<ObjectID, (string Chain, string Stage, string Pending)> _chains = [];
    long _techValue = -1;
    double _nextSecond, _nextAccrual, _nextScan, _nextSnapshot;
    readonly Dictionary<int, int> _money = [];

    // ---- System visit
    double _enteredSim, _enteredReal;
    int _missionsDoneInSystem, _missionsAcceptedInSystem;
    float _heatBeforeJump;

    // ---- Heat accrual of continuous sources and hints for tech count changes
    readonly Dictionary<string, double> _heatAccrual = [];
    readonly List<(string Cause, double At)> _techHints = [];

    // ---- Missions
    readonly HashSet<ObjectID> _offersSeen = [];
    readonly Dictionary<ObjectID, double> _accepted = [];
    MissionManager? _log;

    // ---- Ships
    SimRoot? _hookedSim;
    readonly Dictionary<ObjectID, HashSet<ObjectID>> _shipsSeen = [];
    HashSet<ObjectID> _seenHere = [];

    Session(Run run, string runId, int number)
    {
        Run = run;
        Game = run.Game;
        RunId = runId;
        SessionNumber = number;
        SessionId = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId % 100000}";
        FilePath = "";
        RefreshPlace();
        _result = run.State.Result;
        _pendingSerial = run.State.Events.Pending?.Serial ?? 0;
        _resultSerial = run.State.Events.LastResult?.Serial ?? 0;
        foreach (int p in Game.HumanPlayersIndexes) _money[p] = Game.GetPlayerState(p, deterministic: true).Money;
        _players = _money.Count;
        _heatBeforeJump = run.State.Heat;
        string name = $"{RunId}__{SessionId}";
        string folder = Path.Combine(Mod.ModRoot, "telemetry");
        var t = run.T.Telemetry;
        string partHeader = Tele.Json(w =>
        {
            w.WriteStartObject();
            w.WriteString("e", "file.part");
            w.WriteNumber("part", 0);
            w.WriteString("run", RunId);
            w.WriteString("session", SessionId);
            w.WriteEndObject();
        });
        FilePath = TelemetryWriter.Open(folder, name, t.MaxFileMegabytes, t.MaxFolderMegabytes, partHeader).FilePath;
    }

    // ---- Lifecycle

    internal static void Start(CareerGameModeManager mode, bool loaded)
    {
        End("replaced");
        var saved = PendingLoaded;
        PendingLoaded = null;
        bool newRun = NewRunPending;
        NewRunPending = false;
        if (Run.Current is not { } run || run.Mode != mode) return;
        if (!run.T.Telemetry.Enabled) { Tele.SetMask(null); return; }
        string runId = !newRun && saved is { RunId.Length: > 0 } ? saved.RunId : $"r{DateTime.Now:yyyyMMdd-HHmmss}-{Random.Shared.Next(0x10000):x4}";
        var s = new Session(run, runId, newRun ? 1 : (saved?.Sessions ?? 0) + 1);
        Current = s;
        Stats.Reset();
        Perf.ResetSession();
        TelemetryArea.OnSessionStarted(run);
        s.SyncMask();
        s.Subscribe();
        s.WriteHeader(loaded, newRun, idFromSave: !newRun && saved is { RunId.Length: > 0 });
        if (newRun) Tele.Emit(Cat.Run, "run.new", w => w.WriteNumber("starting_tech", run.T.Tech.StartingTechUnits));
        else Tele.Emit(Cat.Run, "run.loaded", w => { w.WriteString("result", run.State.Result.ToString()); w.WriteNumber("sessions", s.SessionNumber); });
        // In co-op a load of a running game is a resync (after a desync) or a join; the game built a new game from the host's state.
        if (loaded && run.IsMultiplayer) Tele.Emit(Cat.Coop, "mp.game_loaded", w => { w.WriteBoolean("host", run.Game.NetManager.IsHost); w.WriteNumber("players", run.Game.HumanPlayersIndexes.Count()); });
        Log.Info($"telemetry: session {s.SessionId} of run {s.RunId} writes {s.FilePath}");
    }

    static readonly Lock EndGate = new();

    internal static void End(string reason)
    {
        lock (EndGate) EndLocked(reason);
    }

    static void EndLocked(string reason)
    {
        if (Current is not { } s) return;
        Tele.Emit(Cat.Run, "session.end", w =>
        {
            w.WriteString("reason", reason);
            w.WriteString("result", s.Run.State.Result.ToString());
            Tele.Num(w, "real_s", s.RealSeconds);
            Tele.Num(w, "sim_s", s._simSeconds);
            w.WriteNumber("events", Interlocked.Read(ref Stats.Events) + 1);
            w.WriteNumber("dropped", Interlocked.Read(ref Stats.Dropped));
            Tele.Num(w, "overhead_ms", Stats.Ms(Interlocked.Read(ref Stats.OverheadTicks)));
            Tele.Num(w, "writer_ms", Stats.Ms(Interlocked.Read(ref Stats.WriterTicks)));
            w.WriteNumber("frames", Perf.SessionFrames);
            Tele.Num(w, "frame_ms", Perf.SessionFrameMs);
            w.WriteNumber("ticks", Perf.SessionTicks);
            Tele.Num(w, "tick_ms", Perf.SessionTickMs);
            Tele.Num(w, "overhead_pct_wall", Stats.Ms(Interlocked.Read(ref Stats.OverheadTicks)) * 100 / (s.RealSeconds * 1000));
            w.WriteNumber("trace_missing", Stats.TraceMissing);
        });
        s.Unsubscribe();
        Current = null;
        Tele.SetMask(null);
        TelemetryWriter.Close();
    }

    void Subscribe()
    {
        Game.EventHub.Tick += OnTick;
        Game.EventHub.PlayerFameChanged += OnFameChanged;
        Game.EventHub.FactionReputationChanged += OnReputationChanged;
        Game.EventHub.POIDiscovered += OnPoiDiscovered;
        Game.EventHub.PlayerMoneyChanged += TelemetryPatches.OnMoneyChanged;
        Game.EventHub.ResourceTradeDelivery += TelemetryPatches.OnTradeDelivery;
        Game.EventHub.ResourceTradePickup += TelemetryPatches.OnTradePickup;
        Game.Popping += OnPopping;
        _log = Run.Mode.Missions;
        _log.MissionAdded += OnMissionAdded;
        _log.MissionRemoved += OnMissionRemoved;
    }

    void Unsubscribe()
    {
        Game.EventHub.Tick -= OnTick;
        Game.EventHub.PlayerFameChanged -= OnFameChanged;
        Game.EventHub.FactionReputationChanged -= OnReputationChanged;
        Game.EventHub.POIDiscovered -= OnPoiDiscovered;
        Game.EventHub.PlayerMoneyChanged -= TelemetryPatches.OnMoneyChanged;
        Game.EventHub.ResourceTradeDelivery -= TelemetryPatches.OnTradeDelivery;
        Game.EventHub.ResourceTradePickup -= TelemetryPatches.OnTradePickup;
        Game.Popping -= OnPopping;
        if (_log is not null) { _log.MissionAdded -= OnMissionAdded; _log.MissionRemoved -= OnMissionRemoved; }
        if (_hookedSim is not null) { _hookedSim.Ships.Added -= OnShipAdded; _hookedSim.Crew.CrewKilled -= TelemetryPatches.OnCrewKilled; }
    }

    void OnPopping(object? sender, EventArgs e) => End(Run.State.Result == RunResult.Lost ? "game_closed_after_loss" : "game_closed");

    void SyncMask()
    {
        if (ReferenceEquals(_maskFrom, T)) return;
        _maskFrom = T;
        Tele.SetMask(T);
    }

    void RefreshPlace()
    {
        _system = Game.CurrentMapNode?.Name ?? "";
        _arch = Run.Mode.CurrentSectorType?.ID.ToString() ?? "";
        _tier = Run.CurrentTier;
    }

    /// <summary>The common fields of every event (TELEMETRY.md). Reads plain fields only, so it cannot throw.</summary>
    public void WriteContext(Utf8JsonWriter w)
    {
        var s = Run.State;
        Tele.Num(w, "gs", s.GameSeconds);
        Tele.Num(w, "ss", _simSeconds);
        w.WriteNumber("sec", s.SectorIndex);
        w.WriteString("sys", _system);
        w.WriteNumber("tier", _tier);
        w.WriteString("arch", _arch);
        Tele.Num(w, "heat", s.Heat);
        w.WriteNumber("tech", s.FleetTech);
    }

    void WriteHeader(bool loaded, bool newRun, bool idFromSave)
    {
        var mode = Run.Mode;
        var t = Run.T;
        string tunables = Tele.Json(w => Reflect.WriteObject(w, t));
        string fullHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tunables)))[..8];
        var init = mode.Init as CareerInit;
        Tele.Emit(Cat.Run, "session.start", w =>
        {
            w.WriteNumber("schema", 1);
            w.WriteString("run", RunId);
            w.WriteString("session", SessionId);
            w.WriteNumber("session_no", SessionNumber);
            w.WriteBoolean("new_run", newRun);
            w.WriteBoolean("loaded", loaded);
            w.WriteBoolean("run_id_from_save", idFromSave);
            w.WriteString("wall_utc", DateTime.UtcNow.ToString("O"));
            w.WriteString("game_version", GameVersion());
            w.WriteString("mod_version", ModVersion());
            w.WriteString("tunables_hash", Run.State.TunablesHash);
            w.WriteString("tunables_full_hash", fullHash);
            if (init is not null)
            {
                w.WriteString("seed", init.RandSeed.ToString());
                w.WriteNumber("map_size_index", init.MapSizeIndex);
                var sizes = GameApp.Rules.CareerMode.MapSizes;
                if (init.MapSizeIndex >= 0 && init.MapSizeIndex < sizes.Length) w.WriteString("map", sizes[init.MapSizeIndex].NameKey.Key);
            }
            w.WriteNumber("map_nodes", Run.State.VisitedNodes.Count);
            w.WriteString("combat_difficulty", mode.CombatDifficulty.ID.ToString());
            w.WriteString("econ_difficulty", mode.EconDifficulty.ID.ToString());
            w.WriteString("crew_scarcity", mode.CrewScarcity.ID.ToString());
            Tele.Num(w, "money_reward_factor", mode.EconDifficulty.MoneyRewardFactor);
            Tele.Num(w, "fame_reward_factor", mode.EconDifficulty.FameRewardFactor);
            w.WriteNumber("players", Game.HumanPlayersIndexes.Count());
            w.WriteNumber("local_player", Game.LocalPlayerIndex);
            w.WriteBoolean("mp", Run.IsMultiplayer);
            w.WriteBoolean("host", !Run.IsMultiplayer || Game.NetManager.IsHost);
            w.WriteBoolean("autotest", Dev.Autotest.IsActive);
            w.WriteString("label", TelemetryArea.Label);
            w.WriteNumber("cores", Environment.ProcessorCount);
            w.WriteString("os", Environment.OSVersion.VersionString);
            w.WritePropertyName("tunables");
            w.WriteRawValue(tunables, skipInputValidation: true);
        });
    }

    static string GameVersion() =>
        typeof(GameApp).Assembly.GetType("Cosmoteer.Versions")?.GetField("GameVersionBuild")?.GetValue(null) as string ?? "unknown";

    /// <summary>
    /// The assembly version with the git commit that the SDK adds at build time ("1.0.0+&lt;sha&gt;").
    /// Limit: it names the HEAD commit and does not show uncommitted changes. Revisit if test builds from dirty trees need telling apart.
    /// </summary>
    static string ModVersion() =>
        typeof(Session).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    // ---- Sim tick

    void OnTick(object? sender, SimRoot sim)
    {
        if (sim != Game.Sim || Current != this) return;
        long t0 = Stats.Enter();
        try { Tick(sim); }
        catch (Exception ex) { Errors.Report("telemetry tick", ex.Message, ex); }
        finally { Stats.Exit(t0); }
    }

    void Tick(SimRoot sim)
    {
        SyncMask();
        _simSeconds += sim.FixedUpdater.Interval.Seconds;
        var state = Run.State;
        if (sim != _hookedSim) HookSim(sim);
        if (state.Result != _result)
        {
            _result = state.Result;
            string what = _result switch { RunResult.Won => "run.won", RunResult.Lost => "run.lost", _ => "run.result" };
            Tele.Emit(Cat.Run, what, w =>
            {
                w.WriteString("reason", state.EndReason);
                w.WriteNumber("score", state.Score);
                w.WriteNumber("delivered", state.DeliveredUnits);
                Tele.Num(w, "notoriety", state.Notoriety);
                w.WriteNumber("peak_tier", state.PeakTier);
                WriteMoney(w);
            });
            Snapshot(sim, "result");
        }
        if (state.FleetTech != _fleetTech)
        {
            int before = _fleetTech;
            _fleetTech = state.FleetTech;
            TechChanged(before, _fleetTech);
        }
        if (_simSeconds >= _nextSecond)
        {
            _nextSecond = _simSeconds + 1;
            EverySecond(sim);
        }
        if (_simSeconds >= _nextAccrual)
        {
            _nextAccrual = _simSeconds + T.AccrualSeconds;
            FlushAccrual();
        }
        if (_simSeconds >= _nextScan)
        {
            _nextScan = _simSeconds + T.ScanSeconds;
            ScanOffers(sim);
            ScanStasis(sim);
        }
        if (_simSeconds >= _nextSnapshot)
        {
            _nextSnapshot = _simSeconds + T.SnapshotSeconds;
            Snapshot(sim, "timer");
        }
    }

    void EverySecond(SimRoot sim)
    {
        _tier = Run.CurrentTier;
        long value = TechPrice.Sell(Run);
        if (value != _techValue)
        {
            long before = _techValue;
            _techValue = value;
            Tele.Emit(Cat.Tech, "tech.value", w =>
            {
                w.WriteNumber("from", before);
                w.WriteNumber("to", value);
                w.WriteNumber("buyer_unit", Buyer.UnitPrice(Run));
                w.WriteNumber("buyback", TechPrice.Buy(Run));
                Tele.Num(w, "notoriety", Run.State.Notoriety);
                w.WriteNumber("peak_tier", Run.State.PeakTier);
            });
        }
        // Money that changed without PlayerState.AddMoney (direct sets: tech unlocks, career start).
        foreach (int p in Game.HumanPlayersIndexes)
        {
            int now = Game.GetPlayerState(p, deterministic: true).Money;
            int known;
            lock (_money) { if (!_money.TryGetValue(p, out known)) known = now; _money[p] = now; }
            if (known != now)
            {
                Tele.Emit(Cat.Economy, "econ.money", w =>
                {
                    w.WriteNumber("player", p);
                    w.WriteNumber("amount", now - known);
                    w.WriteNumber("after", now);
                    w.WriteString("src", "untracked");
                });
            }
        }
        StoryDiff();
        int players = Game.HumanPlayersIndexes.Count();
        if (players != _players)
        {
            int before = _players;
            _players = players;
            if (before > 0) Tele.Emit(Cat.Coop, "mp.players", w => { w.WriteNumber("from", before); w.WriteNumber("to", players); });
        }
    }

    // ---- Heat

    /// <summary>Called by the AddHeat patch. Continuous sources (time, trail) are summed; every other change is its own event.</summary>
    internal void HeatChanged(string src, string? caller, float requested, float before, float after)
    {
        if (src is "time" or "trail")
        {
            lock (_heatAccrual) _heatAccrual[src] = _heatAccrual.GetValueOrDefault(src) + (after - before);
            return;
        }
        string ctx = Tele.Path;
        Tele.Emit(Cat.Heat, "heat.change", w =>
        {
            w.WriteString("src", src);
            if (caller is not null) w.WriteString("caller", caller);
            if (ctx.Length > 0) w.WriteString("ctx", ctx);
            Tele.Num(w, "amount", requested);
            Tele.Num(w, "delta", after - before);
            Tele.Num(w, "before", before);
            Tele.Num(w, "after", after);
            w.WriteNumber("stages", Run.State.StagesStarted);
        });
    }

    void FlushAccrual()
    {
        KeyValuePair<string, double>[] sums;
        lock (_heatAccrual)
        {
            sums = [.. _heatAccrual];
            _heatAccrual.Clear();
        }
        if (sums.Length > 0 && sums.Any(kv => kv.Value != 0))
        {
            Tele.Emit(Cat.Heat, "heat.accrual", w =>
            {
                Tele.Num(w, "window_s", T.AccrualSeconds);
                foreach (var (src, sum) in sums) Tele.Num(w, src, sum);
                Tele.Num(w, "mult", HeatModel.Multiplier(Run.T.Heat.HeatMultiplierByUnits, Run.State.FleetTech));
                Tele.Num(w, "trail_per_min", ContractBook.TrailPerMinute(Run, ContractBook.OpenWeight(Run)));
            });
        }
        TelemetryPatches.FlushDamage();
    }

    internal static void OnStageStarted(Run run, SimRoot sim, int stage)
    {
        if (Current?.Run != run) return;
        Tele.Emit(Cat.Heat, "heat.stage", w =>
        {
            w.WriteNumber("stage", stage);
            w.WriteString("name", stage < run.T.Heat.Stages.Length ? run.T.Heat.Stages[stage].Name : "?");
            w.WriteNumber("live_hunters", HunterSpawner.Hunters(sim).Count());
            Tele.Num(w, "since_entry_s", Current._simSeconds - Current._enteredSim);
        });
    }

    internal static void OnStrikeWave(Run run, SimRoot sim)
    {
        if (Current?.Run != run) return;
        Tele.Emit(Cat.Heat, "heat.strike_wave", w => w.WriteNumber("live_hunters", HunterSpawner.Hunters(sim).Count()));
    }

    // ---- Jumps and systems

    internal void BeforeSwitch()
    {
        _heatBeforeJump = Run.State.Heat;
        Tele.Emit(Cat.Run, "run.jump", w =>
        {
            w.WriteString("from", _system);
            Tele.Num(w, "stay_s", _simSeconds - _enteredSim);
            w.WriteNumber("open_contracts", ContractBook.Open(Run).Count());
        });
        Snapshot(Game.Sim, "jump");
    }

    internal static void OnSystemEntered(Run run, bool firstVisit)
    {
        if (Current is not { } s || s.Run != run) return;
        string from = s._system;
        double staySim = s._simSeconds - s._enteredSim, stayReal = s.RealSeconds - s._enteredReal;
        int done = s._missionsDoneInSystem, accepted = s._missionsAcceptedInSystem;
        s.RefreshPlace();
        Tele.Emit(Cat.Run, "system.enter", w =>
        {
            w.WriteString("from", from);
            w.WriteBoolean("first_visit", firstVisit);
            Tele.Num(w, "prev_stay_s", staySim);
            Tele.Num(w, "prev_stay_real_s", stayReal);
            w.WriteNumber("prev_missions_done", done);
            w.WriteNumber("prev_missions_accepted", accepted);
            Tele.Num(w, "heat_before", s._heatBeforeJump);
            w.WriteNumber("stages", run.State.StagesStarted);
            w.WriteNumber("peak_tier", run.State.PeakTier);
            w.WriteNumber("visited", run.State.VisitedNodes.Count);
        });
        s._enteredSim = s._simSeconds;
        s._enteredReal = s.RealSeconds;
        s._missionsDoneInSystem = s._missionsAcceptedInSystem = 0;
        s._offersSeen.Clear();
        s._nextScan = s._simSeconds + 2;
    }

    // ---- Tech

    internal void AddTechHint(string cause)
    {
        lock (_techHints) _techHints.Add((cause, _simSeconds));
    }

    void TechChanged(int before, int after)
    {
        const double HintSeconds = 5;
        string[] recent;
        lock (_techHints)
        {
            _techHints.RemoveAll(h => _simSeconds - h.At > HintSeconds);
            recent = [.. _techHints.Select(h => h.Cause).Distinct()];
        }
        Tele.Emit(Cat.Tech, "tech.count", w =>
        {
            if (before >= 0) w.WriteNumber("from", before); else w.WriteNull("from");
            w.WriteNumber("to", after);
            w.WriteNumber("delta", before >= 0 ? after - before : 0);
            w.WriteString("cause", before < 0 ? "initial" : recent.Length > 0 ? recent[^1] : "other");
            w.WriteStartArray("hints");
            foreach (string h in recent) w.WriteStringValue(h);
            w.WriteEndArray();
        });
    }

    // ---- Events and chains: diffs of the saved state, so every page, result and stage change shows whatever code caused it.

    void StoryDiff()
    {
        if (!Tele.On(Cat.Story)) return;
        var ev = Run.State.Events;
        if (ev.Pending is { } p && p.Serial != _pendingSerial)
        {
            _pendingSerial = p.Serial;
            Tele.Emit(Cat.Story, "event.shown", w =>
            {
                w.WriteString("event", p.EventId);
                w.WriteNumber("serial", p.Serial);
                w.WriteNumber("page_tier", p.Tier);
                w.WriteNumber("page_tech_value", p.TechValue);
                w.WriteBoolean("chain_page", p.Lead.Length > 0);
            });
        }
        if (ev.LastResult is { } r && r.Serial != _resultSerial)
        {
            _resultSerial = r.Serial;
            Tele.Emit(Cat.Story, "event.result", w =>
            {
                w.WriteNumber("serial", r.Serial);
                w.WriteString("title", r.Title);
                w.WriteStartArray("lines");
                foreach (string line in r.Lines) w.WriteStringValue(line);
                w.WriteEndArray();
            });
        }
        foreach (var c in Run.State.Chains.Instances)
        {
            var now = (c.ChainId, c.Stage, c.PendingResult);
            bool known = _chains.TryGetValue(c.StationId, out var was);
            if (known && was == now) continue;
            _chains[c.StationId] = now;
            Tele.Emit(Cat.Story, known ? "chain.stage" : "chain.start", w =>
            {
                w.WriteString("chain", c.ChainId);
                w.WriteString("station", c.StationId.ToString());
                if (known) w.WriteString("from_stage", was.Stage);
                w.WriteString("stage", c.Stage);
                w.WriteNumber("part", c.Part);
                w.WriteBoolean("ended", c.Ended);
                if (c.PendingResult.Length > 0) w.WriteString("result", c.PendingResult);
                w.WriteStartArray("flags");
                foreach (string f in c.Flags.Order()) w.WriteStringValue(f);
                w.WriteEndArray();
                if (c.Story.Count > 0) w.WriteString("story", c.Story[^1]);
            });
        }
    }

    internal static void OnOutcomeChosen(Run run, SimRoot sim, int serial, int player, Events.OutcomeRules outcome)
    {
        if (Current?.Run != run) return;
        Tele.Emit(Cat.Story, "event.choice", w =>
        {
            w.WriteNumber("serial", serial);
            w.WriteNumber("player", player);
            w.WritePropertyName("outcome");
            Reflect.WriteObject(w, outcome);
        });
    }

    // ---- Economy

    /// <summary>The money of a player before the current change. A player that this session did not know yet starts at the current money.</summary>
    internal int KnownMoney(int player)
    {
        lock (_money)
        {
            if (_money.TryGetValue(player, out int known)) return known;
            int now = Game.GetPlayerState(player, deterministic: true).Money;
            _money[player] = now;
            return now;
        }
    }

    internal void MoneyChanged(int player, int amount, int before, int after, string src, string caller)
    {
        lock (_money) _money[player] = after;
        string ctx = Tele.Path;
        Tele.Emit(Cat.Economy, "econ.money", w =>
        {
            w.WriteNumber("player", player);
            w.WriteNumber("amount", amount);
            w.WriteNumber("before", before);
            w.WriteNumber("after", after);
            w.WriteString("src", src);
            w.WriteString("caller", caller);
            if (ctx.Length > 0) w.WriteString("ctx", ctx);
        });
    }

    void OnFameChanged(object? sender, ValueChangedEventArgs<int> e)
    {
        long t0 = Stats.Enter();
        try
        {
            string caller = TelemetryPatches.Caller();
            string src = TelemetryPatches.Source(Tele.Top, caller);
            Tele.Emit(Cat.Economy, "econ.fame", w =>
            {
                w.WriteNumber("amount", e.NewValue - e.OldValue);
                w.WriteNumber("after", e.NewValue);
                w.WriteString("src", src);
                w.WriteString("caller", caller);
                Tele.Num(w, "std_mission_fame", Run.StandardMissionFamePaid(Run.CurrentTier));
            });
        }
        finally { Stats.Exit(t0); }
    }

    void OnReputationChanged(object? sender, FactionReputationChangedEventArgs e) =>
        Tele.Emit(Cat.Economy, "econ.reputation", w =>
        {
            w.WriteString("faction", e.Faction.ToString());
            w.WriteNumber("from", e.OldValue);
            w.WriteNumber("to", e.NewValue);
        });

    void OnPoiDiscovered(object? sender, Cosmoteer.Simulation.Doodads.POIDoodad poi) =>
        Tele.Emit(Cat.Run, "system.poi", w => w.WriteString("poi", poi.GetType().Name));

    void WriteMoney(Utf8JsonWriter w)
    {
        w.WriteStartObject("money");
        foreach (int p in Game.HumanPlayersIndexes) w.WriteNumber(p.ToString(), Game.GetPlayerState(p, deterministic: true).Money);
        w.WriteEndObject();
    }

    // ---- Missions

    void OnMissionAdded(object? sender, Mission m)
    {
        _accepted[m.MissionID] = _simSeconds;
        _missionsAcceptedInSystem++;
        Tele.Emit(Cat.Missions, "mission.accepted", w =>
        {
            WriteMission(w, m);
            WriteTerms(w, ContractBook.Find(Run, m));
            w.WriteNumber("open_contracts", ContractBook.Open(Run).Count() + (m.Completion == CompletionState.InProgress ? 0 : 1));
        });
    }

    void OnMissionRemoved(object? sender, Mission m)
    {
        string result = MissionResult(m);
        if (result == "success") _missionsDoneInSystem++;
        double? since = _accepted.Remove(m.MissionID, out double at) ? _simSeconds - at : null;
        Tele.Emit(Cat.Missions, "mission.ended", w =>
        {
            WriteMission(w, m);
            w.WriteString("result", result);
            if (since is { } d) Tele.Num(w, "duration_s", d);
            WriteTerms(w, ContractBook.Find(Run, m));
            w.WriteBoolean("sold_out", Run.State.Contracts.SoldOut.Contains(m.MissionID));
        });
    }

    static readonly MethodInfo? ChainMissionEnd = AccessTools.Method(typeof(Chains.ChainRunner), "End");

    /// <summary>The chain result names (success, timeout, lost, fail, abandoned) through the Chains area's classifier.</summary>
    static string MissionResult(Mission m)
    {
        if (ChainMissionEnd?.Invoke(null, [m]) is Chains.MissionEnd end) return Chains.ChainModel.Classify(end);
        return m.Completion switch { CompletionState.Completed => "success", CompletionState.Failed => "fail", _ => "abandoned" };
    }

    void ScanOffers(SimRoot sim)
    {
        if (!Tele.On(Cat.Missions)) return;
        foreach (var (m, list) in ContractBook.Offers(sim))
        {
            if (!_offersSeen.Add(m.MissionID)) continue;
            Tele.Emit(Cat.Missions, "mission.offered", w =>
            {
                WriteMission(w, m);
                w.WriteString("list", list is MissionManager ? "log" : list == ((CareerSimModeManager)sim.Mode).SimGlobalAvailableMissions ? "system" :
                    list == Run.Mode.GameGlobalAvailableMissions ? "galaxy" : "station");
                WriteTerms(w, ContractBook.Preview(Run, m));
            });
        }
    }

    static void WriteMission(Utf8JsonWriter w, Mission m)
    {
        w.WriteString("mission", m.MissionID.ToString());
        w.WriteString("type", m.MetatypeID.ToString());
        w.WriteString("name", m.Name.Text);
        w.WriteString("category", m.Metatype.CategoryID.ToString());
        if (m.Faction is { } f) w.WriteString("faction", f.ToString());
        if (m.GetTierRange(m.Game.Sim) is { } tr) { w.WriteNumber("tier_min", tr.Min); w.WriteNumber("tier_max", tr.Max); }
        w.WriteNumber("reward_money", m.GetRewardSortValue(Reward.SortValueType.Money));
        w.WriteNumber("reward_fame", m.GetRewardSortValue(Reward.SortValueType.Fame));
        if (m.GetTimeRemaining() is { } left) Tele.Num(w, "time_left_s", left.Seconds);
    }

    static void WriteTerms(Utf8JsonWriter w, ContractTerms? t)
    {
        if (t is null) return;
        w.WriteStartObject("terms");
        w.WriteNumber("fee", t.Fee);
        w.WriteNumber("bonus", t.Bonus);
        Tele.Num(w, "flat_heat", t.FlatHeat);
        Tele.Num(w, "trail", t.TrailWeight);
        w.WriteString("rival", t.RivalGroup);
        Tele.Num(w, "informant", t.InformantChance);
        w.WriteEndObject();
    }

    // ---- Ships

    void HookSim(SimRoot sim)
    {
        if (_hookedSim is not null) { _hookedSim.Ships.Added -= OnShipAdded; _hookedSim.Crew.CrewKilled -= TelemetryPatches.OnCrewKilled; }
        _hookedSim = sim;
        sim.Ships.Added += OnShipAdded;
        sim.Crew.CrewKilled += TelemetryPatches.OnCrewKilled;
        var node = sim.NodeId();
        bool known = _shipsSeen.TryGetValue(node, out var seen);
        _seenHere = seen ??= [];
        _shipsSeen[node] = seen;
        if (known) return;
        // Baseline: every ship that exists at the first visit is part of the system, not a spawn.
        var sides = new Dictionary<string, int>();
        var factions = new Dictionary<string, int>();
        int stasis = 0;
        foreach (Ship ship in sim.Ships)
        {
            seen.Add(ship.UniqueID);
            string side = Side(ship);
            sides[side] = sides.GetValueOrDefault(side) + 1;
            if (FactionRules.GetFaction(ship.Metadata.PlayerIndex) is { } f) factions[f.ID.ToString()] = factions.GetValueOrDefault(f.ID.ToString()) + 1;
        }
        foreach (var s in sim.Stasis)
            if (s is StasisShipSpawner or SimStasisManager.SerializedStasisShip) { seen.Add(s.ReservedObjectID); stasis++; }
        Tele.Emit(Cat.Combat, "combat.census", w =>
        {
            w.WriteStartObject("live");
            foreach (var (k, v) in sides) w.WriteNumber(k, v);
            w.WriteEndObject();
            w.WriteStartObject("factions");
            foreach (var (k, v) in factions) w.WriteNumber(k, v);
            w.WriteEndObject();
            w.WriteNumber("stasis_ships", stasis);
        });
    }

    void OnShipAdded(object? sender, Ship ship)
    {
        if (ship.IsHumanPlayerShip || !_seenHere.Add(ship.UniqueID)) return;
        long t0 = Stats.Enter();
        try { Tele.Emit(Cat.Combat, "combat.spawn", ship, static (w, s) => { WriteShip(w, s); w.WriteBoolean("from_stasis", false); }); }
        finally { Stats.Exit(t0); }
    }

    /// <summary>Ships that spawn straight into stasis (far from the players) show here; the others show in OnShipAdded.</summary>
    void ScanStasis(SimRoot sim)
    {
        if (!Tele.On(Cat.Combat)) return;
        foreach (var s in sim.Stasis)
        {
            if (s is not (StasisShipSpawner or SimStasisManager.SerializedStasisShip) || !_seenHere.Add(s.ReservedObjectID)) continue;
            var tags = SimQueries.StasisTags(s);
            Tele.Emit(Cat.Combat, "combat.spawn", w =>
            {
                w.WriteString("ship", s.ReservedObjectID.ToString());
                w.WriteNumber("player", s.ObjectAllegiance);
                if (FactionRules.GetFaction(s.ObjectAllegiance) is { } f) w.WriteString("faction", f.ID.ToString());
                WriteTags(w, tags);
                w.WriteBoolean("from_stasis", true);
                w.WriteBoolean("hunter", tags?.Contains(HunterSpawner.HunterTag) == true);
            });
        }
    }

    internal static string Side(Ship ship)
    {
        if (ship.IsHumanPlayerShip) return "player";
        if (ship.IsJunk) return "derelict";
        if (ship.HasTag(HunterSpawner.HunterTag)) return "hunter";
        return HostileToPlayers(ship) ? "hostile" : "neutral";
    }

    static bool HostileToPlayers(Ship ship)
    {
        if (ship.Sim?.Mode is not { } mode || ship.Sim.Game is not { } game) return false;
        foreach (int human in game.HumanPlayersIndexes)
            if (mode.AreEnemies(ship.Metadata.PlayerIndex, human)) return true;
        return false;
    }

    internal static void WriteShip(Utf8JsonWriter w, Ship ship)
    {
        w.WriteString("ship", ship.UniqueID.ToString());
        w.WriteString("name", ship.Metadata.PlainShipName);
        w.WriteNumber("player", ship.Metadata.PlayerIndex);
        w.WriteString("side", Side(ship));
        if (FactionRules.GetFaction(ship.Metadata.PlayerIndex) is { } f) w.WriteString("faction", f.ID.ToString());
        WriteTags(w, ship.Tags);
        w.WriteBoolean("hunter", ship.HasTag(HunterSpawner.HunterTag));
        w.WriteNumber("value", ship.BaseBlueprintCost);
        w.WriteNumber("parts", ship.Parts.Count);
        if (ship.AI is { } ai) w.WriteString("ai", ai.Rules.ID.ToString());
        if (ship.Sim is { } sim && SimQueries.FleetCenter(sim) is { } c) Tele.Num(w, "dist", ship.DetWorldCenter.DistanceTo(c));
    }

    static void WriteTags(Utf8JsonWriter w, IEnumerable? tags)
    {
        if (tags is null) return;
        w.WriteStartArray("tags");
        foreach (var t in tags) w.WriteStringValue(t?.ToString());
        w.WriteEndArray();
    }

    // ---- Snapshot

    internal void Snapshot(SimRoot sim, string why)
    {
        if (!Tele.On(Cat.Snapshots)) return;
        Tele.Emit(Cat.Snapshots, "snap", w =>
        {
            var s = Run.State;
            w.WriteString("why", why);
            w.WriteString("result", s.Result.ToString());
            w.WriteNumber("stages", s.StagesStarted);
            Tele.Num(w, "notoriety", s.Notoriety);
            w.WriteNumber("peak_tier", s.PeakTier);
            w.WriteNumber("tech_value", TechPrice.Sell(Run));
            Tele.Num(w, "buyer_mult", s.BuyerPriceMultiplier);
            Tele.Num(w, "heat_mult", HeatModel.Multiplier(Run.T.Heat.HeatMultiplierByUnits, s.FleetTech));
            w.WriteNumber("fame", Run.Mode.PlayerFame);
            WriteMoney(w);
            w.WriteNumber("score", s.Score);
            w.WriteNumber("delivered", s.DeliveredUnits);
            float weight = ContractBook.OpenWeight(Run);
            w.WriteNumber("open_contracts", ContractBook.Open(Run).Count());
            Tele.Num(w, "trail_per_min", ContractBook.TrailPerMinute(Run, weight));
            w.WriteNumber("mission_log", Run.Mode.Missions.Count);
            w.WriteNumber("pending_informants", s.Contracts.Informants.Count);
            w.WriteNumber("waiting_hunter_groups", s.Hunters.Pending.Count);
            w.WriteNumber("events_seen", s.Events.Seen.Values.Sum());
            w.WriteNumber("chains", s.Chains.Instances.Count);
            w.WriteNumber("crew", Game.HumanPlayersIndexes.Sum(sim.Crew.CountCrewForPlayer));
            // Fleet: value, power and parts of each human ship.
            long fleetValue = 0, fleetBlueprint = 0;
            float fleetFirepower = 0;
            int fleetParts = 0, vaults = 0;
            w.WriteStartArray("ships");
            foreach (Ship ship in Vaults.HumanShips(sim).OrderBy(x => x.UniqueID))
            {
                int value = ship.BasePhysicalValue, blueprint = ship.BaseBlueprintCost, parts = ship.Parts.Count, shipVaults = Vaults.VaultParts(ship).Count();
                float fire = ship.Weapons.GetTotalFirePower();
                fleetValue += value; fleetBlueprint += blueprint; fleetParts += parts; fleetFirepower += fire; vaults += shipVaults;
                w.WriteStartObject();
                w.WriteString("ship", ship.UniqueID.ToString());
                w.WriteString("name", ship.Metadata.PlainShipName);
                w.WriteNumber("player", ship.Metadata.PlayerIndex);
                w.WriteNumber("value", value);
                w.WriteNumber("blueprint", blueprint);
                Tele.Num(w, "integrity", blueprint > 0 ? (double)value / blueprint : 1);
                w.WriteNumber("parts", parts);
                Tele.Num(w, "firepower", fire);
                w.WriteNumber("vaults", shipVaults);
                w.WriteNumber("crew", ship.Crew.Assigned.Count);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteNumber("fleet_value", fleetValue);
            w.WriteNumber("fleet_blueprint", fleetBlueprint);
            Tele.Num(w, "fleet_firepower", fleetFirepower);
            w.WriteNumber("fleet_parts", fleetParts);
            w.WriteNumber("vaults", vaults);
            // System: live ships by side.
            var sides = new Dictionary<string, int>();
            foreach (Ship ship in sim.Ships) { string side = Side(ship); sides[side] = sides.GetValueOrDefault(side) + 1; }
            w.WriteStartObject("live");
            foreach (var (k, v) in sides) w.WriteNumber(k, v);
            w.WriteEndObject();
            w.WriteNumber("stasis", sim.Stasis.Count);
            TelemetryPatches.WriteAiSums(w);
        });
    }
}

using System.Globalization;
using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Gui;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Halfling;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Dev;

/// <summary>
/// Runs the game without a person at the keyboard, for tests on the PC.
/// scripts/autotest.sh puts autotest.txt in the mod folder. One command per line, '#' starts a comment.
/// The file is renamed to autotest.done when the script starts, so it runs once.
/// Areas add commands to Commands in their Register method.
/// </summary>
public static class Autotest
{
    public delegate void Command(string arg);

    /// <summary>Command name to handler. Base commands are below; areas add more.</summary>
    public static readonly Dictionary<string, Command> Commands = new(StringComparer.OrdinalIgnoreCase);

    static Queue<(string Name, string Arg, int Line)>? _script;
    static double _waitUntil;
    static WaitCondition? _waitFor;
    static int _line;
    static EventHandler<Cosmoteer.Simulation.SimRoot>? _godmode;

    /// <summary>Test only: while godmode is on, the run cannot be lost (one big hit can still destroy a vault).</summary>
    public static bool GodMode => _godmode is not null;

    /// <summary>A condition that the script waits for. Then runs after the condition holds and may start the next wait.</summary>
    sealed record WaitCondition(Func<bool> Done, double Deadline, string What, Action? Then);

    /// <summary>True when this game instance runs an autotest script.</summary>
    public static bool IsActive => _script is not null;

    public static GameRoot? Game => App.Director.States.OfType<GameRoot>().FirstOrDefault();

    internal static void Init()
    {
        string path = Path.Combine(Mod.ModRoot, "autotest.txt");
        if (!File.Exists(path)) return;
        var lines = File.ReadAllLines(path);
        File.Move(path, Path.Combine(Mod.ModRoot, "autotest.done"), overwrite: true);
        _script = new();
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Split('#')[0].Trim();
            if (line.Length == 0) continue;
            int space = line.IndexOf(' ');
            _script.Enqueue(space < 0 ? (line, "", i + 1) : (line[..space], line[(space + 1)..].Trim(), i + 1));
        }
        AddBaseCommands();
        MpAutotest.AddCommands();
        // Test slots start with empty settings, so every tutorial page is unseen. Pages with AutoPause would pause the game
        // for good the first time their condition fires (for example the combat page when enemies appear).
        int unpaused = 0;
        foreach (var page in GameApp.Rules.Codex.CodexPages.Where(p => p.AutoPause)) { page.AutoPause = false; unpaused++; }
        Log.Info($"AUTOTEST loaded {_script.Count} commands; tutorial auto-pause off on {unpaused} pages");
        App.Director.FrameEnded += OnFrame;
    }

    static void OnFrame(object? sender, EventArgs e)
    {
        if (_script is null || App.Clock.Time.Seconds < _waitUntil) return;
        if (_waitFor is { } w)
        {
            try
            {
                if (!Holds(w.Done))
                {
                    if (App.Clock.Time.Seconds > w.Deadline) throw new TimeoutException($"timed out waiting for {w.What}; states: {StateNames()}");
                    return;
                }
                _waitFor = null;
                Log.Info($"AUTOTEST waited for {w.What}");
                w.Then?.Invoke();
            }
            catch (Exception ex) { Fail($"line {_line}: wait", ex); }
            return;
        }
        if (!App.Director.States.Any(s => s is TitleScreen or GameRoot)) return;
        if (_script.Count == 0) { App.Director.FrameEnded -= OnFrame; return; }
        var (name, arg, line) = _script.Dequeue();
        _line = line;
        try
        {
            if (!Commands.TryGetValue(name, out var cmd)) throw new InvalidOperationException($"unknown command '{name}'");
            Log.Info($"AUTOTEST line {line}: {name} {arg}");
            cmd(arg);
        }
        catch (Exception ex) { Fail($"line {line}: {name} {arg}", ex); }
    }

    /// <summary>Logs the failure and ends the script with quit.</summary>
    static void Fail(string what, Exception ex)
    {
        Log.Error($"AUTOTEST FAIL {what}", ex);
        _waitFor = null;
        _script?.Clear();
        _script?.Enqueue(("quit", "", _line));
    }

    /// <summary>A condition that throws (for example no run yet, during a resync) counts as not met.</summary>
    static bool Holds(Func<bool> condition)
    {
        try { return condition(); }
        catch (InvalidOperationException) { return false; }
    }

    static string StateNames() => string.Join(", ", App.Director.States.Select(s => s.GetType().Name));

    /// <summary>Pauses the script for real seconds.</summary>
    public static void Wait(double seconds) => _waitUntil = App.Clock.Time.Seconds + seconds;

    /// <summary>Pauses the script until the condition holds, then runs then. Fails the test after timeout real seconds.</summary>
    public static void WaitFor(Func<bool> done, double timeoutSeconds, string what, Action? then = null) =>
        _waitFor = new(done, App.Clock.Time.Seconds + timeoutSeconds, what, then);

    /// <summary>The player index of the sender while an "mp" command runs inside the sim. Null outside of it.</summary>
    static int? _simPlayer;

    /// <summary>The player that a test command acts for: the sender of an "mp" command inside the sim, else the local player.</summary>
    public static int ActingPlayerIndex => _simPlayer ?? (Game ?? throw new InvalidOperationException("no game")).LocalPlayerIndex;

    /// <summary>The first ship (lowest ID) that the acting player can command. Works for own ships and for a shared team.</summary>
    public static Ship? ActingShip(Cosmoteer.Simulation.SimRoot sim)
    {
        int player = ActingPlayerIndex;
        return sim.Ships.HumanPlayerShips.Where(s => s.Commands.CanExecuteCrewCommandsFrom(player)).OrderBy(s => s.UniqueID).FirstOrDefault();
    }

    /// <summary>
    /// Runs one script line inside the sim on every machine, for "mp" commands (see MpAutotest.AutotestInput).
    /// A failure fails the script on every machine the same way.
    /// </summary>
    internal static void RunInSim(string line, int playerIndex)
    {
        int space = line.IndexOf(' ');
        string name = space < 0 ? line : line[..space], arg = space < 0 ? "" : line[(space + 1)..].Trim();
        _simPlayer = playerIndex;
        try
        {
            if (!Commands.TryGetValue(name, out var cmd)) throw new InvalidOperationException($"unknown command '{name}'");
            Log.Info($"AUTOTEST sim tick {Game?.Sim.Tick}: {line} (player {playerIndex})");
            cmd(arg);
        }
        catch (Exception ex) { Fail($"mp {line}", ex); }
        finally { _simPlayer = null; }
    }

    /// <summary>Fails the test with a message. Use for checks.</summary>
    public static void Check(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException($"CHECK FAILED: {what}");
        Log.Info($"AUTOTEST PASS {what}");
    }

    public static Run RequireRun() => Run.Current ?? throw new InvalidOperationException("no Hot Cargo run is active");

    /// <summary>Sets one public bool, int or float field of a live tunables section. Arg: "field value" (bool: on|true, else off).</summary>
    public static void SetField(object target, string arg)
    {
        var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) throw new InvalidOperationException("needs: field value");
        var field = target.GetType().GetField(parts[0], System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{target.GetType().Name} has no field '{parts[0]}'");
        object value = field.FieldType switch
        {
            var t when t == typeof(bool) => parts[1] is "on" or "true",
            var t when t == typeof(int) => int.Parse(parts[1], CultureInfo.InvariantCulture),
            var t when t == typeof(float) => float.Parse(parts[1], CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"{parts[0]} is a {field.FieldType.Name}; only bool, int and float are supported"),
        };
        field.SetValue(target, value);
        Log.Info($"AUTOTEST {target.GetType().Name}.{parts[0]} = {value}");
    }

    /// <summary>Moves a ship and stops it. Test only: it changes sim state from a GUI frame, so single player only.</summary>
    public static void Teleport(Ship ship, Halfling.Geometry.Vector2 location)
    {
        ship.WorldCenter = location;
        ship.Physics.PhysicsWrapper.LinearVelocity = Halfling.Geometry.Vector2.Zero;
        // A forced move must update the ship lookup tree, or area queries (sun heat, nebula tracking) miss the ship until it moves.
        ship.Sim?.Ships.RefreshShipPosition(ship);
    }

    /// <summary>Radius that a stasis ship blocks. Stasis ships keep no size; this covers a trade station.</summary>
    const float StasisShipRadius = 800f;

    /// <summary>
    /// Test only. A point at least <paramref name="minDistance"/> from <paramref name="target"/> where a ship of radius
    /// <paramref name="shipRadius"/> overlaps no solid object: live AI ships and asteroids, and ships, asteroids and gates in stasis.
    /// A teleport into an unloaded area must not land on objects that load out of stasis around the ship, or they crush it.
    /// It tries 16 directions on rings that grow by 150 from minDistance, and throws when 20 rings have no free point.
    /// It reads only sim state, so the result is the same on every machine (co-op inputs can use it).
    /// </summary>
    public static Halfling.Geometry.Vector2 ClearSpotNear(Cosmoteer.Simulation.SimRoot sim, Halfling.Geometry.Vector2 target, float minDistance, float shipRadius)
    {
        var solids = new List<(Halfling.Geometry.Circle Area, string What)>();
        foreach (Ship s in sim.Ships)
            if (!s.IsHumanPlayerShip) solids.Add((s.DetWorldBoundingCircle, s.Metadata.PlainShipName));
        foreach (var stasis in sim.Stasis)
        {
            switch (stasis)
            {
                case Cosmoteer.Simulation.Stasis.StasisShipSpawner:
                    solids.Add((new(stasis.Location, StasisShipRadius), $"stasis ship [{string.Join(" ", SimQueries.StasisTags(stasis) ?? [])}]"));
                    break;
                case Cosmoteer.Simulation.Stasis.StasisDoodadSpawner { DoodadRules: Cosmoteer.Simulation.Doodads.ShipDoodadRules or Cosmoteer.Simulation.Doodads.GeneratedShipDoodadRules or Cosmoteer.Simulation.Doodads.FtlGateDoodadRules } d:
                    solids.Add((new(d.Location, d.DoodadRules.SpawnRadius * d.ScaleFactor), d.DoodadID.ToString()));
                    break;
            }
        }
        for (int ring = 0; ring < 20; ring++)
        {
            float r = minDistance + ring * 150f;
            for (int k = 0; k < 16; k++)
            {
                var p = target + Halfling.Geometry.Vector2.FromAngle(k * MathF.Tau / 16f) * r;
                var blockers = solids.Where(c => c.Area.Center.DistanceTo(p) < c.Area.Radius + shipRadius + 50f).Select(c => c.What).ToList();
                if (blockers.Count == 0)
                {
                    if (ring > 0 || k > 0) Log.Info($"AUTOTEST clear spot: moved from the first try to {p} ({r:0} from {target})");
                    return p;
                }
                if (ring == 0 && k == 0) Log.Info($"AUTOTEST clear spot: the first try {p} overlaps {string.Join(", ", blockers)}");
            }
        }
        throw new InvalidOperationException($"no clear spot within {minDistance + 19 * 150f:0} of {target}");
    }

    static void AddBaseCommands()
    {
        Commands["wait"] = arg => Wait(double.Parse(arg, CultureInfo.InvariantCulture));
        Commands["newgame"] = arg =>
        {
            // arg: [map size index, or "run" / nothing for the Hot Cargo run map] [starter ship index, default 0].
            var rules = GameApp.Rules.CareerMode;
            var args = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string map = args.Length > 0 ? args[0] : "run";
            int mapSize = map == "run" ? Journey.JourneyArea.RunMapSizeIndex() : int.Parse(map, CultureInfo.InvariantCulture);
            ShipFile ship = rules.StarterShips[args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 0].Ship;
            // The game's default levels, not the player's saved choices: test slots start with an empty settings file.
            int combat = Array.FindIndex(rules.CombatDifficultyLevels, l => l.IsDefault);
            int econ = Array.FindIndex(rules.EconDifficultyLevels, l => l.IsDefault);
            int crew = Array.FindIndex(rules.CrewScarcityLevels, l => l.IsDefault);
            if (combat < 0 || econ < 0 || crew < 0) throw new InvalidOperationException("no default difficulty level in career rules");
            var init = new CareerInit(null, mapSize, combat, econ, crew, false, false, false, false, enableIronmanMode: false, null, 12345UL,
                (ship.ShipName, Ship.LoadBytes(ship.FilePath)));
            App.Director.PushState(init.CreateGame(SPManager.Create));
            Wait(5);
        };
        Commands["save"] = arg =>
        {
            var game = Game ?? throw new InvalidOperationException("no game");
            game.SaveAs((string)(Paths.SavedGamesFolder / $"HotCargoTest_{arg}.savedgame"));
            Wait(2);
        };
        Commands["load"] = arg =>
        {
            if (Game is { } old) App.Director.PopState(old);
            string file = (string)(Paths.SavedGamesFolder / $"HotCargoTest_{arg}.savedgame");
            GameRoot? game = null;
            Task.Run(() => game = GameRoot.TryLoad(file, SPManager.Create, showErrorDialog: false, paused: false)).Wait();
            if (game is null) throw new InvalidOperationException($"load failed: {file}");
            game.SetMainThread(Thread.CurrentThread);
            App.Director.PushState(game);
            Wait(5);
        };
        Commands["pause"] = arg => (Game ?? throw new InvalidOperationException("no game")).NetManager.EnqueueInput(new MPSetPausedInput(arg != "off"));
        Commands["heat"] = arg => { var r = RequireRun(); r.AddHeat(float.Parse(arg, CultureInfo.InvariantCulture), r.Game.Sim); };
        Commands["stage"] = _ => { var r = RequireRun(); r.ForceNextStage(r.Game.Sim); };
        Commands["fame"] = arg => { var r = RequireRun(); r.Mode.PlayerFame += int.Parse(arg, CultureInfo.InvariantCulture); };
        Commands["log"] = arg =>
        {
            var r = RequireRun();
            var s = r.State;
            Log.Info($"AUTOTEST STATE {arg}: heat={s.Heat:0.00} stages={s.StagesStarted} sector={s.SectorIndex} tech={s.FleetTech} " +
                     $"result={s.Result} seconds={s.GameSeconds:0.0} tier={r.CurrentTier} fame={r.Mode.PlayerFame} node={r.Game.CurrentMapNode.Name}");
        };
        Commands["expect"] = arg =>
        {
            // expect <field> <op> <value>, for example: expect heat >= 30. The value can be another field: expect stages == stagecount
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) throw new InvalidOperationException("expect needs: field op value");
            double actual = Field(parts[0]);
            Check(Compare(actual, parts[1], parts[2]), $"{parts[0]} {parts[1]} {parts[2]} (actual {actual})");
        };
        Commands["waitfor"] = arg =>
        {
            // waitfor <field> <op> <value> [timeout seconds, default 60]: waits until the check holds. Use it to sync two MP instances.
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is not (3 or 4)) throw new InvalidOperationException("waitfor needs: field op value [timeout]");
            if (!Fields.ContainsKey(parts[0])) throw new InvalidOperationException($"unknown field '{parts[0]}'");
            Compare(0, parts[1], parts[2]);
            double timeout = parts.Length == 4 ? double.Parse(parts[3], CultureInfo.InvariantCulture) : 60;
            WaitFor(() => Compare(Field(parts[0]), parts[1], parts[2]), timeout, $"{parts[0]} {parts[1]} {parts[2]}");
        };
        Commands["godmode"] = arg =>
        {
            // Test only: keeps human ships at full part health each sim tick, so long AI tests are not cut short by a lost ship.
            // Parts that a single hit destroys still break. Vault damage still costs tech (it counts health drops).
            var game = Game ?? throw new InvalidOperationException("no game");
            if (arg == "off") { if (_godmode is not null) game.EventHub.Tick -= _godmode; _godmode = null; return; }
            _godmode ??= (_, sim) =>
            {
                foreach (var ship in sim.Ships.HumanPlayerShips)
                    foreach (var part in ship.Parts)
                        if (part.Health < part.Rules.MaxHealth) part.Health = part.Rules.MaxHealth;
            };
            game.EventHub.Tick += _godmode;
        };
        Commands["quit"] = _ =>
        {
            Log.Info("AUTOTEST DONE");
            App.Director.Exit();
        };
    }

    /// <summary>Numeric fields for "expect". Areas add fields here.</summary>
    public static readonly Dictionary<string, Func<double>> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["heat"] = () => RequireRun().State.Heat,
        ["stages"] = () => RequireRun().State.StagesStarted,
        ["stagecount"] = () => RequireRun().T.Heat.Stages.Length,
        ["sector"] = () => RequireRun().State.SectorIndex,
        ["tech"] = () => RequireRun().State.FleetTech,
        ["seconds"] = () => RequireRun().State.GameSeconds,
        ["lost"] = () => RequireRun().State.Result == RunResult.Lost ? 1 : 0,
        ["won"] = () => RequireRun().State.Result == RunResult.Won ? 1 : 0,
        ["fame"] = () => RequireRun().Mode.PlayerFame,
        ["tier"] = () => RequireRun().CurrentTier,
        ["active"] = () => Run.Current is null ? 0 : 1,
        ["paused"] = () => (Game ?? throw new InvalidOperationException("no game")).Sim.IsPaused.Confirmed ? 1 : 0,
    };

    static double Field(string name) =>
        Fields.TryGetValue(name, out var f) ? f() : throw new InvalidOperationException($"unknown field '{name}'");

    static bool Compare(double actual, string op, string value)
    {
        double expected = Fields.TryGetValue(value, out var field) ? field() : double.Parse(value, CultureInfo.InvariantCulture);
        return op switch
        {
            ">=" => actual >= expected, "<=" => actual <= expected, ">" => actual > expected,
            "<" => actual < expected, "==" => Math.Abs(actual - expected) < 1e-3,
            _ => throw new InvalidOperationException($"unknown op {op}"),
        };
    }
}

/// <summary>The startup hook runs before the game creates App.Director, so the autotest starts at the first title screen.</summary>
[HarmonyPatch(typeof(TitleScreen), MethodType.Constructor)]
static class AutotestTitleScreenPatch
{
    static bool _started;
    static void Postfix()
    {
        if (_started) return;
        _started = true;
        try { Autotest.Init(); } catch (Exception ex) { Log.Error("autotest init failed", ex); }
    }
}

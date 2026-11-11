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
        Log.Info($"AUTOTEST loaded {_script.Count} commands");
        App.Director.FrameEnded += OnFrame;
    }

    static void OnFrame(object? sender, EventArgs e)
    {
        if (_script is null || App.Clock.Time.Seconds < _waitUntil) return;
        if (!App.Director.States.Any(s => s is TitleScreen or GameRoot)) return;
        if (_script.Count == 0) { App.Director.FrameEnded -= OnFrame; return; }
        var (name, arg, line) = _script.Dequeue();
        try
        {
            if (!Commands.TryGetValue(name, out var cmd)) throw new InvalidOperationException($"unknown command '{name}'");
            Log.Info($"AUTOTEST line {line}: {name} {arg}");
            cmd(arg);
        }
        catch (Exception ex)
        {
            Log.Error($"AUTOTEST FAIL line {line}: {name} {arg}", ex);
            _script.Clear();
            _script.Enqueue(("quit", "", line));
        }
    }

    /// <summary>Pauses the script for real seconds.</summary>
    public static void Wait(double seconds) => _waitUntil = App.Clock.Time.Seconds + seconds;

    /// <summary>Fails the test with a message. Use for checks.</summary>
    public static void Check(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException($"CHECK FAILED: {what}");
        Log.Info($"AUTOTEST PASS {what}");
    }

    public static Run RequireRun() => Run.Current ?? throw new InvalidOperationException("no Hot Cargo run is active");

    static void AddBaseCommands()
    {
        Commands["wait"] = arg => Wait(double.Parse(arg, CultureInfo.InvariantCulture));
        Commands["newgame"] = arg =>
        {
            // arg: [map size index, or "last" for the last map size (the Hot Cargo short map is appended last)] [starter ship index, default 0].
            var rules = GameApp.Rules.CareerMode;
            var args = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string map = args.Length > 0 ? args[0] : "last";
            int mapSize = map == "last" ? rules.MapSizes.Length - 1 : int.Parse(map, CultureInfo.InvariantCulture);
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
        Commands["pause"] = arg => Game?.NetManager.EnqueueInput(new MPSetPausedInput(arg != "off"));
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
            // expect <field> <op> <value>, for example: expect heat >= 30
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) throw new InvalidOperationException("expect needs: field op value");
            double actual = Field(parts[0]);
            double expected = double.Parse(parts[2], CultureInfo.InvariantCulture);
            bool ok = parts[1] switch
            {
                ">=" => actual >= expected, "<=" => actual <= expected, ">" => actual > expected,
                "<" => actual < expected, "==" => Math.Abs(actual - expected) < 1e-3,
                _ => throw new InvalidOperationException($"unknown op {parts[1]}"),
            };
            Check(ok, $"{parts[0]} {parts[1]} {expected} (actual {actual})");
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
        ["sector"] = () => RequireRun().State.SectorIndex,
        ["tech"] = () => RequireRun().State.FleetTech,
        ["seconds"] = () => RequireRun().State.GameSeconds,
        ["lost"] = () => RequireRun().State.Result == RunResult.Lost ? 1 : 0,
        ["won"] = () => RequireRun().State.Result == RunResult.Won ? 1 : 0,
        ["fame"] = () => RequireRun().Mode.PlayerFame,
        ["tier"] = () => RequireRun().CurrentTier,
        ["active"] = () => Run.Current is null ? 0 : 1,
    };

    static double Field(string name) =>
        Fields.TryGetValue(name, out var f) ? f() : throw new InvalidOperationException($"unknown field '{name}'");
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

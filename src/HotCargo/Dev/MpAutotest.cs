using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Gui;
using Cosmoteer.Gui.Multiplayer;
using Cosmoteer.Modes;
using Cosmoteer.Modes.Career;
using Cosmoteer.Multiplayer;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Graphics;
using Halfling.IO;
using Halfling.Network;
using Halfling.Network.Rpc;
using Halfling.Serialization;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Dev;

/// <summary>
/// Co-op autotest sessions with two game instances: scripts/autotest-mp.sh runs a host slot and a client slot on the PC.
/// They connect over UDP on 127.0.0.1, through the same screens as a LAN game (lobby, game setup, launch).
/// The host's script holds all choreography; the client joins and runs "follow". Commands: mphost, mpjoin, follow, mpready,
/// mpstart, mplobby, mpload, mprestart, mpresync, "mp &lt;command&gt;" (in the sim on every machine, as the sender),
/// "as &lt;player&gt; &lt;command&gt;" (in the sim, as that player) and "on &lt;player&gt; &lt;command&gt;" (on that player's machine).
/// </summary>
public static class MpAutotest
{
    /// <summary>Desyncs that the game detected in this process (see DesyncPatch).</summary>
    public static int Desyncs { get; internal set; }

    /// <summary>Distinct multiplayer games seen (a resync, a load and mprestart replace the game). Each game counts ticks from 0,
    /// so the state lines carry it.</summary>
    internal static int _mpGames;
    static GameRoot? _lastGame;

    /// <summary>Port for the networking start of mphost. Null keeps the game's port (35862 or --port).</summary>
    internal static int? Port;

    /// <summary>5 s at 30 sim ticks a second: a short co-op step still gets a state comparison.</summary>
    const int StateLogTicks = 150;

    /// <summary>Set by "mp mpresync": the next out-of-sync call is a test resync, not a desync.</summary>
    internal static bool ExpectResync;
    static int _mark;

    /// <summary>Set when a command from the host ("on") failed on this client. The next "mpack" reports it and clears it.</summary>
    internal static bool OnCommandFailed;
    /// <summary>Commands from the host for this machine ("mpon"), filled in the sim and run by "follow".</summary>
    static readonly System.Collections.Concurrent.ConcurrentQueue<string> _inbox = new();
    /// <summary>The host ended the session ("mpend"): a following client quits.</summary>
    static bool _ended;
    /// <summary>"on" commands: the last number sent (host) and the last number acknowledged (sim, every machine), with the failed ones.</summary>
    static int _onSent, _acked;
    static readonly HashSet<int> _failedAcks = [];

    const double OnTimeoutSeconds = 120;
    /// <summary>A following client waits this long for the next command or the end. Longer than any session.</summary>
    const double FollowSeconds = 1800;
    /// <summary>A following client fails when it is in no game and no game setup for this long (the host is gone).</summary>
    const double LostHostSeconds = 30;
    const double JoinSeconds = 45;

    internal static void AddCommands()
    {
        Autotest.Commands["mphost"] = arg => Host(int.Parse(arg, CultureInfo.InvariantCulture));
        Autotest.Commands["mpjoin"] = Join;
        Autotest.Commands["mpready"] = Ready;
        Autotest.Commands["mpstart"] = arg => Start(arg.Length > 0 ? int.Parse(arg, CultureInfo.InvariantCulture) : 2);
        Autotest.Commands["mp"] = arg => RequireMpGame().NetManager.EnqueueInput(new AutotestInput(arg));
        // mplobby (host): ends the game for all players and returns everyone to the game setup screen, with the connections kept.
        Autotest.Commands["mplobby"] = _ =>
        {
            var host = RequireMpGame().NetManager as MPHostManager ?? throw new InvalidOperationException("mplobby is for the host");
            host.Abort();
            Autotest.WaitFor(() => Setup() is not null && Autotest.Game is null, 60, "the game setup screen");
        };
        // mpload <name> (host, in the game setup screen): the next mpstart loads the save HotCargoTest_<name>.
        Autotest.Commands["mpload"] = arg =>
        {
            var gss = Setup() ?? throw new InvalidOperationException("not in the game setup screen");
            var career = gss._curModeConfigGui as CareerMPModeConfigGui ?? throw new InvalidOperationException("game mode is not career");
            var path = new AbsolutePath((string)(Paths.SavedGamesFolder / $"HotCargoTest_{arg}.savedgame"));
            if (!File.Exists(path)) throw new InvalidOperationException($"no save {path}");
            // The same steps as CareerMPModeConfigGui.OnLoadGameClicked.
            career._loadGamePath = path;
            (career._setLoadGameNameRpc ?? throw new InvalidOperationException("career config has no load RPC"))(path.FilenameWithoutExtension);
            career.InvokeReadySettingChanged();
        };
        // mpresync (host): the resync that the game does after a desync (the host sends its game to the clients, every machine
        // reloads it). DesyncPatch does not count it. Then: waitfor mpgames >= N.
        Autotest.Commands["mpresync"] = _ =>
        {
            var game = RequireMpGame();
            if (!game.NetManager.IsHost) throw new InvalidOperationException("mpresync is for the host");
            // The host can get the client's out-of-sync call before its own sim reaches the input, so it expects the resync now.
            ExpectResync = true;
            game.NetManager.EnqueueInput(new AutotestInput("mpresync_client"));
        };
        Autotest.Commands["mpresync_client"] = _ =>
        {
            var mp = RequireMpGame().NetManager as BaseMPManager ?? throw new InvalidOperationException("no MP manager");
            if (mp.IsHost) return;
            // A client starts it, as a client does when its integrity hash differs from the host's.
            ExpectResync = true;
            mp._onOutOfSyncRpc(default);
        };
        // mp mpmark <n>: sets a test counter inside the sim on every machine. The other machine waits with "waitfor mpmark >= n".
        Autotest.Commands["mpmark"] = arg => _mark = int.Parse(arg, CultureInfo.InvariantCulture);
        Autotest.Fields["mpmark"] = () => _mark;

        // as <player> <command>: runs the command in the sim on every machine as that player index (0 the host, 1 the first client).
        // Inputs that the command sends run at once as that player (SimInputPatch). Use it for choices that only read sim state.
        Autotest.Commands["as"] = arg =>
        {
            var (player, line) = PlayerAndLine(arg, "as");
            RequireMpGame().NetManager.EnqueueInput(new AutotestInput(line, player));
        };
        // on <player> <command>: runs the command on that player's own machine and waits until it reports back. For what must
        // happen there: its UI, its view, and inputs that it sends over the network like a real click. A failure there fails the step.
        Autotest.Commands["on"] = arg =>
        {
            var (player, line) = PlayerAndLine(arg, "on");
            int number = ++_onSent;
            RequireMpGame().NetManager.EnqueueInput(new AutotestInput($"mpon {player} {number} {line}"));
            Autotest.WaitFor(() => _acked >= number, OnTimeoutSeconds, $"player {player} to run '{line}'", () =>
            {
                if (_failedAcks.Contains(number)) throw new InvalidOperationException($"CHECK FAILED: player {player}: '{line}' (the client log has the reason)");
            });
        };
        // Sim side of "on": the target machine queues the command and its acknowledgement for "follow".
        Autotest.Commands["mpon"] = arg =>
        {
            var parts = arg.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) throw new InvalidOperationException("mpon needs: player number command");
            if (RequireMpGame().LocalPlayerIndex != int.Parse(parts[0], CultureInfo.InvariantCulture)) return;
            _inbox.Enqueue(parts[2]);
            _inbox.Enqueue($"mpack {parts[1]}");
        };
        // mpack <number> (local): reports a finished "on" command to every machine, with its result.
        Autotest.Commands["mpack"] = arg =>
        {
            RequireMpGame().NetManager.EnqueueInput(new AutotestInput($"mpacked {arg} {(OnCommandFailed ? 0 : 1)}"));
            OnCommandFailed = false;
        };
        Autotest.Commands["mpacked"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int number = int.Parse(parts[0], CultureInfo.InvariantCulture);
            _acked = Math.Max(_acked, number);
            if (parts[1] == "0") _failedAcks.Add(number);
        };
        Autotest.Commands["mpstep"] = arg => Autotest.SimStep = arg;
        Autotest.Commands["mpend"] = _ => _ended = true;
        // follow (client): says ready in every game setup, runs the commands that the host sends ("on"), and quits when the host ends.
        Autotest.Commands["follow"] = _ =>
        {
            Autotest.Following = true;
            Follow();
        };
        // mprestart (host): back to the game setup with the connections kept, then a new run. For steps after a won or lost run.
        Autotest.Commands["mprestart"] = _ =>
        {
            var host = RequireMpGame().NetManager as MPHostManager ?? throw new InvalidOperationException("mprestart is for the host");
            host.Abort();
            Autotest.WaitFor(() => Setup() is not null && Autotest.Game is null, 60, "the game setup screen", () => Start(2));
        };

        // tunablesfile <rules text>: replaces the slot's tunables.rules, before mphost. Test slots only; deploy restores the repo file.
        Autotest.Commands["tunablesfile"] = arg => File.WriteAllText(TunablesFile.FilePath, arg);
        // mpchoose <serial|marked> <choice number>: sends an event choice for any page serial, like a click on a page that another player
        // already answered. "marked": the page that was pending at the last ev_mark. The sim must ignore a serial that is not the pending page.
        Autotest.Commands["mpchoose"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new InvalidOperationException("mpchoose needs: serial|marked choice");
            int serial = parts[0] == "marked" ? Events.EventsArea.MarkedSerial : int.Parse(parts[0], CultureInfo.InvariantCulture);
            if (serial <= 0) throw new InvalidOperationException("mpchoose marked needs an ev_mark while a page is pending");
            RequireMpGame().NetManager.EnqueueInput(Events.EventsChoiceDomain.Pick(serial, int.Parse(parts[1], CultureInfo.InvariantCulture) - 1));
        };

        Autotest.Fields["heatperminute"] = () => Autotest.RequireRun().T.Heat.HeatPerGameMinute;
        Autotest.Fields["desyncs"] = () => Desyncs;
        Autotest.Fields["mpgames"] = () => _mpGames;
        Autotest.Fields["ishost"] = () => RequireMpGame().NetManager.IsHost ? 1 : 0;
        Autotest.Fields["mpplayers"] = () => RequireMpGame().HumanPlayerCount;
        Autotest.Fields["mpconnected"] = () => RequireMpGame().NetManager is BaseMPManager mp ? mp._connections.ConnectionCount : 0;
        Autotest.Fields["mpsetup"] = () => Setup() is null ? 0 : 1;
        Autotest.Fields["localplayer"] = () => RequireMpGame().LocalPlayerIndex;

        App.Director.FrameEnded += (_, _) =>
        {
            var game = Autotest.Game;
            if (game is null || game == _lastGame || game.NetManager is not BaseMPManager) return;
            _lastGame = game;
            _mpGames++;
            Log.Info($"AUTOTEST MP game {_mpGames}: host={game.NetManager.IsHost} local player={game.LocalPlayerIndex} humans={game.HumanPlayerCount}");
        };
        Run.Hooks.Tick += LogState;
    }

    static GameRoot RequireMpGame() =>
        Autotest.Game is { NetManager.IsMultiplayer: true } g ? g : throw new InvalidOperationException("no multiplayer game");

    static GameSetupScreen? Setup() => App.Director.States.OfType<GameSetupScreen>().FirstOrDefault();

    static TitleScreen Title() => App.Director.States.OfType<TitleScreen>().FirstOrDefault() ?? throw new InvalidOperationException("no title screen");

    /// <summary>The game runs with a real MP manager (not the setup manager of the launch) and Hot Cargo is active in it.</summary>
    static bool InGame() => Autotest.Game is { NetManager: BaseMPManager } g && Run.Current?.Game == g;

    static GameSetupScreen.PlayerWidget Me(GameSetupScreen gss) =>
        gss._playersByID.TryGetValue(GameApp.Messenger.UniqueID, out var me) ? me : throw new InvalidOperationException("local player is not in the game setup");

    static MPModeConfigGui ModeConfig(GameSetupScreen gss) => gss._curModeConfigGui ?? throw new InvalidOperationException("game setup has no mode");

    static int ShipCount(GameSetupScreen.PlayerWidget p) => ((MPModeConfigGui.IPlayerInfo)p).ShipCount;

    static bool NeedsShip(GameSetupScreen gss, GameSetupScreen.PlayerWidget me) =>
        ModeConfig(gss).GetMaxShipsForPlayer(me) > 0 && ShipCount(me) == 0;

    /// <summary>mphost &lt;port&gt;: starts networking on the port, opens a LAN game setup as host, picks career with the Hot Cargo map and a fixed seed, and picks starter ship 0.</summary>
    static void Host(int port)
    {
        if (GameApp.s_messenger is not null) throw new InvalidOperationException("networking already started");
        Port = port;
        var net = GameApp.InitializeNetworking();
        // InitializeNetworking falls back to any free port when the port is in use.
        if (net.IPV4Port != port) throw new InvalidOperationException($"port {port} is in use; networking took port {net.IPV4Port}");
        var lobby = new LanLobbyScreen(Title().Background.Clone(showLogoShip: false));
        App.Director.PushState(lobby);
        lobby.OnHostClicked();
        var gss = Setup() ?? throw new InvalidOperationException("hosting did not open the game setup screen");
        // Career is the first and default mode. Setting _gameMode would not move the drop list, and a client that joins later
        // gets the drop list's mode, so the test only checks it.
        var career = gss._curModeConfigGui as CareerMPModeConfigGui
            ?? throw new InvalidOperationException($"the default MP mode is {gss._gameMode.Value}, not career");
        (career._mapSize ?? throw new InvalidOperationException("career config has no map size")).Value = GameApp.Rules.CareerMode.MapSizes[Journey.JourneyArea.RunMapSizeIndex()];
        (career._galaxySeed ?? throw new InvalidOperationException("career config has no seed")).Value = 12345UL;
        var me = Me(gss);
        AddStarterShip(gss, me);
        Autotest.WaitFor(() => ShipCount(me) > 0, 60, "the host's ship");
        Log.Info($"AUTOTEST hosting on port {port}");
    }

    /// <summary>
    /// mpjoin &lt;address:port&gt;: starts networking and joins the game like Direct Connect in the LAN lobby.
    /// A failed join (timeout, refusal dialog) logs the reason and tries once more.
    /// </summary>
    static void Join(string address)
    {
        if (GameApp.s_messenger is not null) throw new InvalidOperationException("networking already started");
        // Any free port, so the client never takes a port that a host test needs.
        Port = 0;
        var net = GameApp.InitializeNetworking();
        JoinOnce(address, net.ParseIPEndPoint(address, MPConfig.Port), retry: true);
    }

    static void JoinOnce(string address, System.Net.IPEndPoint endPoint, bool retry)
    {
        var lobby = new LanLobbyScreen(Title().Background.Clone(showLogoShip: false));
        App.Director.PushState(lobby);
        lobby.JoinGame(endPoint, sameServer: false);
        Autotest.WaitFor(() => Setup() is { } gss && gss._playersByID.ContainsKey(GameApp.Messenger.UniqueID), JoinSeconds, $"joining {address}",
            onTimeout: retry ? () =>
            {
                Log.Info($"AUTOTEST join failed; states: {Autotest.StateNames()}. Joining again.");
                App.Director.PopStatesUntil(s => s is TitleScreen, including: false);
                JoinOnce(address, endPoint, retry: false);
            } : null);
    }

    static (int Player, string Line) PlayerAndLine(string arg, string command)
    {
        var parts = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out int player))
            throw new InvalidOperationException($"{command} needs: <player index> <command>");
        if (!RequireMpGame().HumanPlayersByID.Values.Contains(player)) throw new InvalidOperationException($"no human player {player}");
        return (player, parts[1]);
    }

    /// <summary>The "step" command: the sim logs the step too, so a failed sim line counts for the step in which it ran.</summary>
    internal static void OnStepStarted(string step)
    {
        if (Autotest.Game is { NetManager: BaseMPManager mp }) mp.EnqueueInput(new AutotestInput($"mpstep {step}"));
    }

    /// <summary>The "end" command of the host: sends the client away, waits until it left, then quits. False outside a co-op game as host.</summary>
    internal static bool EndSession()
    {
        if (Autotest.Game is not { NetManager: BaseMPManager { IsHost: true } mp }) return false;
        mp.EnqueueInput(new AutotestInput("mpend"));
        Autotest.WaitFor(() => mp._connections.ConnectionCount == 0, 120, "the client to quit", () => Autotest.Inject(["quit"]),
            onTimeout: () => { Log.Info("AUTOTEST the client did not quit"); Autotest.Inject(["quit"]); });
        return true;
    }

    /// <summary>The "follow" wait of a client. It ends when a command from the host or the end arrives, or when the host is gone.</summary>
    static void Follow()
    {
        double nextClick = 0, lostSince = -1;
        Autotest.WaitFor(() =>
        {
            if (!_inbox.IsEmpty || _ended) return true;
            double now = App.Clock.Time.Seconds;
            if (Autotest.Game is { NetManager.IsMultiplayer: true }) { lostSince = -1; return false; }
            if (Setup() is { } gss)
            {
                lostSince = -1;
                // A host setting change (for example a loaded save) sets every player to not ready, so this clicks ready again.
                if (now >= nextClick && gss._playersByID.TryGetValue(GameApp.Messenger.UniqueID, out var me))
                {
                    nextClick = now + 3;
                    if (NeedsShip(gss, me)) AddStarterShip(gss, me);
                    else if (!me.IsReady) gss.OnReadyClicked(null, EventArgs.Empty);
                }
                return false;
            }
            if (lostSince < 0) lostSince = now;
            return now - lostSince > LostHostSeconds;
        }, FollowSeconds, "the host's next command", () =>
        {
            if (_ended) { Log.Info("AUTOTEST the host ended the session"); Autotest.Inject(["quit"]); return; }
            if (_inbox.IsEmpty) throw new InvalidOperationException($"lost the host; states: {Autotest.StateNames()}");
            var lines = new List<string>();
            while (_inbox.TryDequeue(out var line)) lines.Add(line);
            lines.Add("follow");
            Autotest.Inject(lines);
        });
    }

    /// <summary>
    /// mpready [shared] (client): picks starter ship 0 when the player must pick a ship, says ready, and waits for the host to start.
    /// With "shared", the client first joins team 1 of the host and shares the host's ships.
    /// </summary>
    static void Ready(string arg)
    {
        var gss = Setup() ?? throw new InvalidOperationException("not in the game setup screen");
        var me = Me(gss);
        if (arg == "shared" && me.Team != 0)
        {
            gss._requestJoinTeamRpc(0, RpcReceivers.Server);
            Autotest.WaitFor(() => me.Team == 0, 30, "joining the host's team", () => Ready(""));
            return;
        }
        if (NeedsShip(gss, me))
        {
            AddStarterShip(gss, me);
            Autotest.WaitFor(() => ShipCount(me) > 0, 60, "the client's ship", () => Ready(arg));
            return;
        }
        // A host setting change (for example a loaded save) sets every player to not ready, so the wait clicks ready again.
        double nextClick = 0;
        Autotest.WaitFor(() =>
        {
            if (InGame()) return true;
            if (Setup() is { } s && s._playersByID.TryGetValue(GameApp.Messenger.UniqueID, out var p) && !p.IsReady && App.Clock.Time.Seconds >= nextClick)
            {
                s.OnReadyClicked(null, EventArgs.Empty);
                nextClick = App.Clock.Time.Seconds + 3;
            }
            return false;
        }, 300, "the host to start the game");
    }

    /// <summary>mpstart [players] (host): waits until that many players are in the setup and all clients are ready, then starts.</summary>
    static void Start(int players)
    {
        var gss = Setup() ?? throw new InvalidOperationException("not in the game setup screen");
        Autotest.WaitFor(() => gss._playersByID.Count >= players && gss._playersByID.Values.All(p => p.IsHostPlayer || (p.CanReady && p.IsReady)),
            180, $"{players} ready players", () =>
            {
                if (NeedsShip(gss, Me(gss))) throw new InvalidOperationException("the host has no ship");
                gss.OnStartClicked(null, EventArgs.Empty);
                Autotest.WaitFor(InGame, 300, "the game start");
            });
    }

    /// <summary>Sends starter ship 0 for the player, with the same steps as the ship button of the game setup screen.</summary>
    static void AddStarterShip(GameSetupScreen gss, GameSetupScreen.PlayerWidget player)
    {
        string file = GameApp.Rules.CareerMode.StarterShips[0].Ship.FilePath;
        using Ship ship = Ship.TryLoad(file, showErrorDialog: false, 0, blueprintsOnly: true) ?? throw new InvalidOperationException($"cannot load {file}");
        using var shipStream = new MemoryStream();
        ship.SaveDesign(shipStream);
        using var icon = TextureData.LoadFrom(file);
        icon.Resize(64, 64, ResizeMode.Stretch);
        using var iconStream = new MemoryStream();
        icon.SaveTo(iconStream, ImageFileFormat.Png);
        gss._requestAddShipRpc(player.PlayerID, null, shipStream.ToArray(), iconStream.ToArray(), ship.Metadata.PlainShipName, RpcReceivers.Server);
    }

    /// <summary>Every saved member of RunState, by name. The state hash covers all of them, so a new feature field is covered too.</summary>
    static readonly (string Name, Type Type, Func<RunState, object?> Get)[] StateMembers = typeof(RunState)
        .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        .Where(m => m.IsDefined(typeof(SerializeAttribute), inherit: true))
        .Select(m => m switch
        {
            FieldInfo f => (f.Name, f.FieldType, (Func<RunState, object?>)f.GetValue),
            PropertyInfo p => (p.Name, p.PropertyType, (Func<RunState, object?>)p.GetValue),
            _ => throw new InvalidOperationException($"RunState member {m.Name} is a {m.MemberType}, not a field or property"),
        })
        .OrderBy(m => m.Name, StringComparer.Ordinal)
        .ToArray();

    static readonly HashSet<string> _hashErrorsLogged = [];

    /// <summary>The first 4 bytes of the SHA-256 of the value as the save writes it (object bits), as hex. "error" when it cannot serialize.</summary>
    static string Hash(string name, object? value, Type type)
    {
        try
        {
            var node = new Halfling.ObjectBits.OBNode();
            App.ObjectBitsSerializer.Write(node, value, type);
            using var stream = new MemoryStream();
            node.WriteTo(stream);
            return Convert.ToHexString(SHA256.HashData(stream.ToArray()), 0, 4);
        }
        catch (Exception ex)
        {
            if (_hashErrorsLogged.Add(name)) Log.Info($"MPHASH cannot serialize {name} ({type.Name}): {ex.Message}");
            return "error";
        }
    }

    /// <summary>
    /// Every StateLogTicks sim ticks in co-op autotests, logs a hash of the whole Hot Cargo run state and of key vanilla values
    /// (MPHASH), and one hash per RunState member (MPFIELDS). Lockstep machines log at the same ticks; scripts/coop-report.sh
    /// compares them and names the members that differ. It catches divergence that the game's integrity hash does not cover.
    /// </summary>
    static void LogState(Run run, SimRoot sim, double dt)
    {
        if (!Autotest.IsActive || !run.IsMultiplayer || sim.Tick % StateLogTicks != 0) return;
        var s = run.State;
        var players = new List<PlayerState>();
        run.Game.GetHumanPlayerStates(players, deterministic: true);
        var humans = sim.Ships.HumanPlayerShips.OrderBy(h => h.UniqueID).ToList();
        string vanilla = $"money=[{string.Join(",", players.Select(p => p.Money))}] fame={run.Mode.PlayerFame} ships={sim.Ships.Count} " +
                         $"human=[{string.Join(",", humans.Select(h => $"{h.Metadata.PlayerIndex}:{h.Parts.Count}"))}] rand={sim.Rand._a ^ sim.Rand._b:x}";
        var fields = StateMembers.Select(m => (m.Name, Hash: Hash(m.Name, m.Get(s), m.Type))).ToList();
        string all = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join(",", fields.Select(f => f.Hash)) + vanilla)), 0, 4);
        Log.Info($"MPHASH game={_mpGames} tick={sim.Tick} all={all} heat={s.Heat:0.000} tech={s.FleetTech} result={s.Result} {vanilla}");
        Log.Info($"MPFIELDS game={_mpGames} tick={sim.Tick} {string.Join(" ", fields.Select(f => $"{f.Name}={f.Hash}"))}");
    }

    /// <summary>Runs a test command inside the sim on every machine, for "mp" and "as". Allowed only in autotests.</summary>
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoAutotest")]
    internal sealed class AutotestInput : MPInput
    {
        [Serialize] public string Line { get; private set; }
        /// <summary>The player that the command acts for ("as"). -1: the sender.</summary>
        [Serialize(Optional = true)] public int AsPlayer { get; private set; } = -1;

        public AutotestInput(string line, int asPlayer = -1)
        {
            Line = line;
            AsPlayer = asPlayer;
        }

        public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false) =>
            Autotest.RunInSim(Line, AsPlayer >= 0 ? AsPlayer : playerIndex);
    }

    /// <summary>
    /// Inside the sim ("mp", "as"), every machine runs the same command. An input that the command sends would leave every machine
    /// once, so it runs at once as the acting player instead, after the same AllowMPInput check as a received input.
    /// </summary>
    [HarmonyPatch(typeof(BaseMPManager), nameof(BaseMPManager.EnqueueInput))]
    static class SimInputPatch
    {
        static bool Prefix(BaseMPManager __instance, MPInput input)
        {
            if (Autotest.SimPlayer is not { } player || input.IsEmpty) return true;
            var game = __instance.Game;
            var id = game.HumanPlayersByID.Where(p => p.Value == player).Select(p => (MessengerID?)p.Key).FirstOrDefault()
                ?? throw new InvalidOperationException($"no human player {player}");
            if (!game.Mode.AllowMPInput(input, player, id))
            {
                Log.Info($"AUTOTEST {input.GetType().Name} refused for player {player}");
                return false;
            }
            input.Execute(game, player, id);
            return false;
        }
    }

    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.AllowMPInput))]
    static class AllowAutotestInputPatch
    {
        static void Postfix(MPInput mpInput, ref bool __result)
        {
            if (mpInput is AutotestInput) __result = Autotest.IsActive;
        }
    }

    /// <summary>mphost picks the port before the game starts networking.</summary>
    [HarmonyPatch(typeof(MPConfig), nameof(MPConfig.Port), MethodType.Getter)]
    static class PortPatch
    {
        static void Postfix(ref int __result)
        {
            if (Port is { } p) __result = p;
        }
    }
}

/// <summary>
/// Logs each desync that the game detects, in normal play too: the game then resyncs from the host.
/// The log line holds the Hot Cargo state of this machine, to compare with the other player's log.
/// It is an ERROR line, so an autotest with a desync fails.
/// </summary>
[HarmonyPatch(typeof(BaseMPManager), nameof(BaseMPManager.OnOutOfSyncRpc))]
static class DesyncPatch
{
    static void Prefix(BaseMPManager __instance)
    {
        if (__instance._isOutOfSync) return;
        if (MpAutotest.ExpectResync)
        {
            MpAutotest.ExpectResync = false;
            Log.Info($"AUTOTEST test resync at input tick {__instance._curInputTick} (host={__instance.IsHost})");
            return;
        }
        MpAutotest.Desyncs++;
        var s = Run.Current?.State;
        Log.Error($"MP DESYNC at input tick {__instance._curInputTick}, game={MpAutotest._mpGames} sim tick={Autotest.Game?.Sim.Tick} (host={__instance.IsHost}); the game resyncs from the host. " +
                  $"Hot Cargo state: heat={s?.Heat:0.000} stages={s?.StagesStarted} sector={s?.SectorIndex} tech={s?.FleetTech} result={s?.Result}");
    }
}

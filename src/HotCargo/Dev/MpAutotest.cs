using System.Globalization;
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
/// Co-op autotests with two game instances: scripts/autotest-mp.sh runs a host slot and a client slot on the PC.
/// They connect over UDP on 127.0.0.1, through the same screens as a LAN game (lobby, game setup, launch).
/// Commands: mphost, mpjoin, mpready, mpstart, mplobby, mpload, mpresync, and "mp &lt;command&gt;", which runs a test command
/// inside the sim on every machine (so test commands that change the sim do not desync).
/// </summary>
public static class MpAutotest
{
    /// <summary>Desyncs that the game detected in this process (see DesyncPatch).</summary>
    public static int Desyncs { get; internal set; }

    /// <summary>Distinct multiplayer games seen (a resync and a load replace the game).</summary>
    static int _mpGames;
    static GameRoot? _lastGame;

    /// <summary>Port for the networking start of mphost. Null keeps the game's port (35862 or --port).</summary>
    internal static int? Port;

    const int StateLogTicks = 300;

    /// <summary>Set by "mp mpresync": the next out-of-sync call is a test resync, not a desync.</summary>
    internal static bool ExpectResync;
    static int _mark;

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

        // tunablesfile <rules text>: replaces the slot's tunables.rules, before mphost. Test slots only; deploy restores the repo file.
        Autotest.Commands["tunablesfile"] = arg => File.WriteAllText(TunablesFile.FilePath, arg);
        // mpchoose <serial> <choice number>: sends an event choice for any page serial, like a click on a page that another player
        // already answered. The sim must ignore a serial that is not the pending page.
        Autotest.Commands["mpchoose"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new InvalidOperationException("mpchoose needs: serial choice");
            RequireMpGame().NetManager.EnqueueInput(new Events.EventChoiceInput(
                int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture) - 1));
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

    /// <summary>mpjoin &lt;address:port&gt;: starts networking and joins the game like Direct Connect in the LAN lobby.</summary>
    static void Join(string address)
    {
        if (GameApp.s_messenger is not null) throw new InvalidOperationException("networking already started");
        // Any free port, so the client never takes a port that a host test needs.
        Port = 0;
        var net = GameApp.InitializeNetworking();
        var endPoint = net.ParseIPEndPoint(address, MPConfig.Port);
        var lobby = new LanLobbyScreen(Title().Background.Clone(showLogoShip: false));
        App.Director.PushState(lobby);
        lobby.JoinGame(endPoint, sameServer: false);
        Autotest.WaitFor(() => Setup() is { } gss && gss._playersByID.ContainsKey(GameApp.Messenger.UniqueID), 60, $"joining {address}");
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

    /// <summary>
    /// Logs a fingerprint of the Hot Cargo and game state every StateLogTicks sim ticks, in MP autotests only.
    /// Lockstep machines log at the same ticks, and scripts/autotest-mp.sh compares the lines of host and client.
    /// It catches divergence that the game's integrity hash does not cover.
    /// </summary>
    static void LogState(Run run, SimRoot sim, double dt)
    {
        if (!Autotest.IsActive || !run.IsMultiplayer || sim.Tick % StateLogTicks != 0) return;
        var s = run.State;
        var players = new List<PlayerState>();
        run.Game.GetHumanPlayerStates(players, deterministic: true);
        var humans = sim.Ships.HumanPlayerShips.OrderBy(h => h.UniqueID).ToList();
        Log.Info($"MPSTATE tick={sim.Tick} heat={s.Heat:0.000} stages={s.StagesStarted} sector={s.SectorIndex} tech={s.FleetTech} result={s.Result} " +
                 $"score={s.Score} delivered={s.DeliveredUnits} event={s.Events.Pending?.EventId ?? "-"}/{s.Events.Pending?.Serial ?? 0} " +
                 $"money=[{string.Join(",", players.Select(p => p.Money))}] fame={run.Mode.PlayerFame} ships={sim.Ships.Count} " +
                 $"human=[{string.Join(",", humans.Select(h => $"{h.Metadata.PlayerIndex}:{h.Parts.Count}"))}] rand={sim.Rand._a ^ sim.Rand._b:x}");
    }

    /// <summary>Runs a test command inside the sim on every machine, for the "mp" command. Allowed only in autotests.</summary>
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoAutotest")]
    internal sealed class AutotestInput : MPInput
    {
        [Serialize] public string Line { get; private set; }

        public AutotestInput(string line) => Line = line;

        public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false) => Autotest.RunInSim(Line, playerIndex);
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
        Log.Error($"MP DESYNC at input tick {__instance._curInputTick} (host={__instance.IsHost}); the game resyncs from the host. " +
                  $"Hot Cargo state: heat={s?.Heat:0.000} stages={s?.StagesStarted} sector={s?.SectorIndex} tech={s?.FleetTech} result={s?.Result}");
    }
}

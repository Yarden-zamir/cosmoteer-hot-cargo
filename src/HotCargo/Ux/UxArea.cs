using System.Globalization;
using System.Reflection;
using Cosmoteer.Game.Gui;
using Halfling;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Ui;

namespace HotCargo.Ux;

/// <summary>Entry point of the UX area. Mod.Boot calls Register once at startup, before the game starts.</summary>
public static class UxArea
{
    static bool _screenshotPatched;
    static Cosmoteer.Modes.Career.Comms.CommWindow? _comms;

    /// <summary>The comms window that "comms open" opened. Test only.</summary>
    internal static Cosmoteer.Modes.Career.Comms.CommWindow OpenComms =>
        _comms ?? throw new InvalidOperationException("no comms window is open; run comms open first");

    public static void Register()
    {
        Run.Hooks.NewRunStarted += run => RunEndUi.IntroPending = run;
        AddAutotestCommands();
        UxAutotest.Register();
    }

    /// <summary>App.Director does not exist yet when Register runs, so frame hooks start at the first title screen.</summary>
    [HarmonyPatch(typeof(Cosmoteer.Gui.TitleScreen), MethodType.Constructor)]
    static class FrameHooks
    {
        static bool _started;

        static void Postfix()
        {
            if (_started) return;
            _started = true;
            App.Director.FrameEnded += (_, _) =>
            {
                UxKit.Guard("ship alerts", ShipAlerts.OnFrame);
                if (Run.Current is { } run) UxKit.Guard("markers", () => Markers.Update(run));
                if (App.Director.States.OfType<Cosmoteer.Game.GameRoot>().FirstOrDefault() is not { } game) return;
                UxKit.Guard("damage pings", () => DamagePings.OnFrame(game));
                UxKit.Guard("low supplies", () => LowSupplies.OnFrame(game));
                UxKit.Guard("dialogue keys", () => DialogueKeys.OnFrame(game));
            };
        }
    }

    static void AddAutotestCommands()
    {
        // render on|off: draw while minimized, for screenshots (windowed test slots, scripts/screens.sh; headless slots have no graphics).
        // "on" also closes the vanilla Steam notice: test slots run with --nosteam, and the notice would cover every screenshot.
        Autotest.Commands["render"] = arg =>
        {
            if (!_screenshotPatched)
            {
                Screenshot.Register(new Harmony(Mod.HarmonyId + ".screenshot"));
                _screenshotPatched = true;
            }
            Screenshot.SetForceRender(arg != "off");
            if (arg == "off") return;
            string steamNotice = Cosmoteer.Localization.Strings.GetText("Misc/SteamNotInitialized");
            foreach (var dialog in Halfling.App.Director.States.OfType<Halfling.Gui.Dialogs.OneButtonDialog>().Where(d => d.MessageLabel.Text == steamNotice).ToList())
                Halfling.App.Director.PopState(dialog);
        };
        Autotest.Commands["screenshot"] = arg => { Screenshot.Capture(arg); Autotest.Wait(1.5); };
        // ux <Field> on|off|<number>: sets one UxTunables field for this run (before/after screenshots, thresholds in tests).
        Autotest.Commands["ux"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new InvalidOperationException("ux needs: Field on|off|number");
            var field = typeof(UxTunables).GetField(parts[0], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?? throw new InvalidOperationException($"unknown ux field '{parts[0]}'");
            object value = field.FieldType == typeof(bool) ? parts[1] switch
                {
                    "on" => true,
                    "off" => false,
                    _ => throw new InvalidOperationException($"ux field '{parts[0]}' needs on or off"),
                }
                : field.FieldType == typeof(float) ? float.Parse(parts[1], CultureInfo.InvariantCulture)
                : throw new InvalidOperationException($"ux field '{parts[0]}' has type {field.FieldType.Name}");
            field.SetValue(Autotest.RequireRun().T.Ux, value);
        };
        // damage F: test only, single player. Sets every second part of the first own ship to health fraction F.
        Autotest.Commands["damage"] = arg =>
        {
            var run = Autotest.RequireRun();
            if (run.IsMultiplayer) throw new InvalidOperationException("damage is single player only");
            float f = float.Parse(arg, CultureInfo.InvariantCulture);
            var ship = run.Game.Sim.Ships.LocalPlayerShips.FirstOrDefault() ?? throw new InvalidOperationException("no own ship");
            int i = 0;
            foreach (var part in ship.Parts.ToList())
                if (i++ % 2 == 0) part.Health = Math.Max(1, (int)(part.Rules.MaxHealth * f));
        };
        // win: test only. Ends the run as won with a sample score (the real path is a Buyer delivery).
        Autotest.Commands["win"] = _ =>
        {
            var run = Autotest.RequireRun();
            run.State.DeliveredUnits = 4;
            run.State.Score = 184_500;
            run.Win(Cosmoteer.Localization.Strings.GetText("HotCargoJourney/WinReason"));
        };
        Autotest.Commands["lose"] = arg => Autotest.RequireRun().Lose(arg.Length > 0 ? arg : "The last Tech Vault was destroyed.");
        // select: selects the first own ship, so the ships card shows its info row.
        Autotest.Commands["select"] = _ =>
        {
            var sim = Autotest.RequireRun().Game.Sim;
            sim.PlayerInput.SelectSingleShip(sim.Ships.LocalPlayerShips.FirstOrDefault() ?? throw new InvalidOperationException("no own ship"));
        };
        Autotest.Commands["closepanel"] = _ => CenterPanel.CloseAll();
        // comms open|contract|chain|close: opens a comms window from the first own ship to the nearest trading station
        // (contract: the station of the offer that contract_pick chose; chain: the test chain's station), or closes it.
        // comms_fit: 1 when at least one Hot Cargo comms page is open and every one is on the screen and shows its whole page or its scroll bar (spec 816).
        Autotest.Fields["comms_fit"] = () => CommsHail.PageFit() is var (open, fit) && open > 0 && fit == open ? 1 : 0;
        // comms_scroll <amount>: scrolls every open Hot Cargo comms page (spec 816), like the mouse wheel. Down is positive.
        Autotest.Commands["comms_scroll"] = arg =>
        {
            if (CommsHail.ScrollOpenPages(float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture)) == 0)
                throw new InvalidOperationException("no open comms window with a Hot Cargo page");
        };
        Autotest.Commands["comms"] = arg =>
        {
            var game = Autotest.RequireRun().Game;
            switch (arg)
            {
                case "open" or "contract" or "chain":
                    if (_comms is not null) throw new InvalidOperationException("a comms window is already open");
                    var callee = arg switch
                    {
                        "contract" => Contracts.ContractsArea.PickedStationForTests(),
                        "chain" => Chains.ChainsArea.TestStationShip(),
                        _ => Tech.TechAutotest.NearestStation(),
                    };
                    _comms = new Cosmoteer.Modes.Career.Comms.CommWindow(game, Tech.TechAutotest.PlayerShip(), callee);
                    game.Gui.FloatingWindows.AddChild(_comms);
                    break;
                case "close":
                    (_comms ?? throw new InvalidOperationException("no comms window is open")).Unparent();
                    _comms = null;
                    break;
                default: throw new InvalidOperationException("comms needs: open | contract | chain | close");
            }
        };
        Autotest.Commands["map"] = arg => { var r = Autotest.RequireRun(); r.Game.ShowMap = arg != "off"; };
        // camera <x> <y> <size>: test only. The sim camera stops following ships, centers on the world point and zooms to fit a
        // square of that size (for whole-system screenshots).
        Autotest.Commands["camera"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 3) throw new InvalidOperationException("camera needs: x y size");
            float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            var view = Autotest.RequireRun().Game.Sim.Camera.ViewController;
            view.ClearFocus(clearFramesOfReferenceAndRotation: true);
            view.FRLocation = new Halfling.Geometry.Vector2(F(p[0]), F(p[1]));
            view.ZoomToFit(new Halfling.Geometry.Vector2(F(p[2]), F(p[2])), instant: true);
        };

        // kill: test only, single player. Destroys every part of the first own ship.
        Autotest.Commands["kill"] = _ =>
        {
            var run = Autotest.RequireRun();
            if (run.IsMultiplayer) throw new InvalidOperationException("kill is single player only");
            var ship = run.Game.Sim.Ships.LocalPlayerShips.FirstOrDefault() ?? throw new InvalidOperationException("no own ship");
            foreach (var part in ship.Parts.ToList()) part.Health = 0;
        };
        Autotest.Fields["ux_stage_alerts"] = () => HeatPanel.AlertsShown;
        Autotest.Fields["ux_ship_alerts"] = () => ShipAlerts.Shown;
        Autotest.Commands["uxlog"] = _ =>
        {
            var sim = Autotest.RequireRun().Game.Sim;
            Log.Info($"ux: tracked={ShipAlerts.TrackedCount} local=[{string.Join(", ", sim.Ships.LocalPlayerShips.Select(s => $"{s.Metadata.PlainShipName} parts={s.Parts.Count} can={s.Commands.CanExecuteCommands} exists={s.ExistsInSim(sim)}"))}]");
        };
        Autotest.Fields["ux_panel"] = () => Autotest.RequireRun().T.Ux.HeatPanel ? 1 : 0;
        Autotest.Fields["ux_integrity"] = () =>
        {
            var ship = Autotest.RequireRun().Game.Sim.Ships.LocalPlayerShips.FirstOrDefault() ?? throw new InvalidOperationException("no own ship");
            return ShipIntegrity.Of(ship);
        };
    }
}

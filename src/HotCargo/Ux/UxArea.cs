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
        // render on|off: draw while minimized, for screenshots (main slot only; headless slots have no graphics).
        Autotest.Commands["render"] = arg =>
        {
            if (!_screenshotPatched)
            {
                Screenshot.Register(new Harmony(Mod.HarmonyId + ".screenshot"));
                _screenshotPatched = true;
            }
            Screenshot.SetForceRender(arg != "off");
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
        // comms open|close: opens a comms window from the first own ship to the nearest trading station, or closes it.
        Autotest.Commands["comms"] = arg =>
        {
            var game = Autotest.RequireRun().Game;
            switch (arg)
            {
                case "open":
                    if (_comms is not null) throw new InvalidOperationException("a comms window is already open");
                    _comms = new Cosmoteer.Modes.Career.Comms.CommWindow(game, Tech.TechAutotest.PlayerShip(), Tech.TechAutotest.NearestStation());
                    game.Gui.FloatingWindows.AddChild(_comms);
                    break;
                case "close":
                    (_comms ?? throw new InvalidOperationException("no comms window is open")).Unparent();
                    _comms = null;
                    break;
                default: throw new InvalidOperationException("comms needs: open | close");
            }
        };
        Autotest.Commands["map"] = arg => { var r = Autotest.RequireRun(); r.Game.ShowMap = arg != "off"; };

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

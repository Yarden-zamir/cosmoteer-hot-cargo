using System.Globalization;
using System.Reflection;
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

    public static void Register()
    {
        Run.Hooks.NewRunStarted += run => RunEndUi.IntroPending = run;
        AddAutotestCommands();
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
        // ux <Switch> on|off: sets one UxTunables switch for this run (before/after screenshots).
        Autotest.Commands["ux"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) throw new InvalidOperationException("ux needs: Switch on|off");
            var field = typeof(UxTunables).GetField(parts[0], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?? throw new InvalidOperationException($"unknown ux switch '{parts[0]}'");
            if (field.FieldType != typeof(bool)) throw new InvalidOperationException($"ux switch '{parts[0]}' is not a bool");
            field.SetValue(Autotest.RequireRun().T.Ux, parts[1] == "on");
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
        Autotest.Commands["win"] = arg => Autotest.RequireRun().Win(arg.Length > 0 ? arg : "You delivered 4 tech units to the Buyer.\nScore: 184,500");
        Autotest.Commands["lose"] = arg => Autotest.RequireRun().Lose(arg.Length > 0 ? arg : "The last Tech Vault was destroyed.");
        // select: selects the first own ship, so the ships card shows its info row.
        Autotest.Commands["select"] = _ =>
        {
            var sim = Autotest.RequireRun().Game.Sim;
            sim.PlayerInput.SelectSingleShip(sim.Ships.LocalPlayerShips.FirstOrDefault() ?? throw new InvalidOperationException("no own ship"));
        };
        Autotest.Commands["closepanel"] = _ => CenterPanel.CloseAll();
        Autotest.Commands["map"] = arg => { var r = Autotest.RequireRun(); r.Game.ShowMap = arg != "off"; };

        Autotest.Fields["ux_panel"] = () => Autotest.RequireRun().T.Ux.HeatPanel ? 1 : 0;
        Autotest.Fields["ux_integrity"] = () =>
        {
            var ship = Autotest.RequireRun().Game.Sim.Ships.LocalPlayerShips.FirstOrDefault() ?? throw new InvalidOperationException("no own ship");
            return ShipIntegrity.Of(ship);
        };
    }
}

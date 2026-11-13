using System.Globalization;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Hunters;
using HotCargo.SharedChoice;
using HotCargo.Ui;

namespace HotCargo.Pursuit;

/// <summary>Entry point of the Pursuit area (spec 900 to 949). Mod.Boot calls Register after the Hunters area, so its stage hooks see the spawned ships.</summary>
public static class PursuitArea
{
    public static void Register()
    {
        HunterSpawner.ScaleShips = Director.ScaleShips;
        Run.Hooks.StrikeWaveIntervalFactor = Director.IntervalFactor;
        Run.Hooks.HeatGainsBlocked = Outage.Active;
        Run.Hooks.HeatGainBlocked += Outage.OnGainBlocked;
        HunterOrders.Override = ship => Run.Current is { } run && run.PressureActive && (Scout.Override(run, ship) || Blockade.Override(run, ship));
        HunterOrders.StasisGoal = (sim, id) => Run.Current is { } run && run.PressureActive ? Scout.StasisGoal(run, id) ?? Blockade.StasisGoal(run, sim, id) : null;
        Run.Hooks.StageStarted += (run, sim, stage) => Errors.Guard("pursuit stage", () =>
        {
            Scout.OnStageStarted(run, sim, stage);
            Blockade.OnStageStarted(run, sim, stage);
        });
        Run.Hooks.Tick += (run, sim, _) => Errors.Guard("pursuit tick", () =>
        {
            Removals.Tick(run, sim);
            if (!run.PressureActive) return;
            Scout.Tick(run, sim);
            Blockade.Tick(run, sim);
            Outage.Tick(run, sim);
            Hacker.Tick(run, sim);
        });
        Run.Hooks.SystemEntered += OnSystemEntered;
        // First provider: the hacker owns his page's shared choices (one domain per page), and diplomacy then adds nothing there.
        CommsHail.Providers.Insert(0, Hacker.Instance);
        SharedChoices.Register(Hacker.Instance);
        AddAutotest();
    }

    /// <summary>Scouts, blockade ships and ships that jump out stay in the old system. The outage timer lasts across the jump.</summary>
    static void OnSystemEntered(Run run, bool firstVisit)
    {
        var s = run.State.Pursuit;
        s.Scouts.Clear();
        s.Blockaders.Clear();
        s.BlockadeRelay = null;
        s.Removals.Clear();
        Outage.OnSystemEntered(run, firstVisit);
        Hacker.OnSystemEntered(run, firstVisit);
    }

    static Run R() => Autotest.RequireRun();
    static SimRoot Sim() => R().Game.Sim;

    static Ship HackerShip() =>
        (R().State.Pursuit.Hacker is { } id ? Sim().Ships[id] : null) ?? throw new InvalidOperationException("no live hacker ship");

    static Ship Me() => Autotest.ActingShip(Sim()) ?? throw new InvalidOperationException("no player ship");

    static void AddAutotest()
    {
        // pu_set <PursuitTunables field> <value>: changes a live tunable (use "mp pu_set" in co-op). Test only.
        Autotest.Commands["pu_set"] = arg => Autotest.SetField(R().T.Pursuit, arg);
        // pu_gain <amount>: a heat gain like a mission or an event, which an outage blocks (the "heat" command ignores the block).
        Autotest.Commands["pu_gain"] = arg => R().AddHeat(float.Parse(arg, CultureInfo.InvariantCulture), Sim());
        // pu_scout: makes the first live hunter a reporting scout. pu_scoutnear <distance>: moves the scout this far from its relay.
        Autotest.Commands["pu_scout"] = _ =>
            Autotest.Check(HunterSpawner.Hunters(Sim()).OrderBy(h => h.UniqueID).FirstOrDefault() is { } h && Scout.Start(R(), Sim(), h.UniqueID), "a hunter became a scout");
        Autotest.Commands["pu_scoutnear"] = arg =>
        {
            var s = R().State.Pursuit.Scouts.FirstOrDefault() ?? throw new InvalidOperationException("no scout");
            var ship = Sim().Ships[s.Ship] ?? throw new InvalidOperationException("scout is not live");
            float d = float.Parse(arg, CultureInfo.InvariantCulture);
            var dir = Halfling.Geometry.Vector2.FromAngle(s.RelayAt.DirectionTo(ship.DetWorldCenter));
            Autotest.Teleport(ship, s.RelayAt + dir * d);
        };
        // pu_focusscout: centers the camera on the scout, zoomed out so its marker label shows (screenshots).
        Autotest.Commands["pu_focusscout"] = _ =>
        {
            var s = R().State.Pursuit.Scouts.FirstOrDefault() ?? throw new InvalidOperationException("no scout");
            var ship = Sim().Ships[s.Ship] ?? throw new InvalidOperationException("scout is not live");
            ship.FocusCamera(playSound: false);
            Sim().Camera.ViewController.ZoomToFit(new Halfling.Geometry.Vector2(2500f));
        };
        // pu_blockade: the live hunters park at the needed relay, through the pacer.
        Autotest.Commands["pu_blockade"] = _ =>
        {
            Autotest.Check(Pacer.MayStart(R(), PacedKinds.Blockade), "pacer allows a blockade");
            Autotest.Check(Blockade.Start(R(), Sim(), HunterSpawner.Hunters(Sim()).OrderBy(h => h.UniqueID).Select(h => h.UniqueID).ToList()), "blockade started");
        };
        // pu_relaynear <distance>: moves the acting ship this far from the blockade relay, on the line from the relay to the ship.
        Autotest.Commands["pu_relaynear"] = arg =>
        {
            var id = R().State.Pursuit.BlockadeRelay ?? throw new InvalidOperationException("no blockade");
            var relay = FtlGate.GetGates(Sim()).First(g => g.DestinationID == id).DetWorldCenter;
            var me = Me();
            var dir = Halfling.Geometry.Vector2.FromAngle(relay.DirectionTo(me.DetWorldCenter));
            Autotest.Teleport(me, relay + dir * float.Parse(arg, CultureInfo.InvariantCulture));
        };
        // pu_outage <seconds> [natural]: starts an outage now. natural counts in the pacer like a rolled outage.
        Autotest.Commands["pu_outage"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Outage.Start(R(), double.Parse(p[0], CultureInfo.InvariantCulture), natural: p.Length > 1 && p[1] == "natural");
        };
        // pu_outage_due <seconds>: the rolled natural outage of this system starts in this many seconds (through the pacer).
        Autotest.Commands["pu_outage_due"] = arg => R().State.Pursuit.OutageDueAt = R().State.GameSeconds + double.Parse(arg, CultureInfo.InvariantCulture);
        // pu_hacker: the hacker proxy comes now. pu_hacker_open: opens the comms window to it. pu_hacker_page: logs its page.
        Autotest.Commands["pu_hacker"] = _ => Autotest.Check(Hacker.Spawn(R(), Sim()), "hacker spawned");
        Autotest.Commands["pu_hacker_near"] = arg =>
            Autotest.Teleport(HackerShip(), Autotest.ClearSpotNear(Sim(), Me().DetWorldCenter, float.Parse(arg, CultureInfo.InvariantCulture), HackerShip().WorldBoundingRadius));
        Autotest.Commands["pu_hacker_open"] = _ => R().Game.Gui.FloatingWindows.AddChild(new CommWindow(R().Game, Me(), HackerShip()));
        Autotest.Commands["pu_hacker_page"] = _ =>
        {
            var page = HackerShip().AI?.GetDialogueExchange(Me()) as HailPage ?? throw new InvalidOperationException("the hacker has no hail page");
            _hackerButtons = page.Buttons.Count(b => b.SelfInputActive);
            Log.Info($"AUTOTEST HACKER PAGE: {page.PrimaryDialogue.Replace('\n', ' ')} | buttons: " +
                     string.Join(" | ", page.Buttons.Select(b => (b.SelfInputActive ? "" : "(off) ") + b.Text)));
        };
        // pu_hacker_pick pay|decline: the hacker page buttons, through the shared choice input like a click.
        Autotest.Commands["pu_hacker_pick"] = arg =>
        {
            int option = arg.Trim() switch { "pay" => Hacker.OptionPay, "decline" => Hacker.OptionDecline, _ => throw new InvalidOperationException("pu_hacker_pick needs: pay | decline") };
            R().Game.NetManager.EnqueueInput(new SharedChoiceInput(Hacker.DomainName, Hacker.Serial(HackerShip()), SharedChoiceAction.Choose, option));
        };
        // pu_started <kind>: records a big event start, as another area would.
        Autotest.Commands["pu_started"] = arg => Pacer.Started(R(), arg.Trim());
        // pu_killscout: destroys every part of the scouts (use "mp pu_killscout" in co-op).
        Autotest.Commands["pu_killscout"] = _ =>
        {
            foreach (var s in R().State.Pursuit.Scouts.ToList())
                if (Sim().Ships[s.Ship] is { } ship)
                    foreach (var part in ship.Parts.ToList()) part.Health = 0;
        };
        // pu_addmoney <credits>: gives every human player money (use "mp pu_addmoney" in co-op).
        Autotest.Commands["pu_addmoney"] = arg =>
        {
            foreach (int p in R().Game.HumanPlayersIndexes) R().Game.GetPlayerState(p, deterministic: true).AddMoney(int.Parse(arg, CultureInfo.InvariantCulture));
        };
        Autotest.Commands["pu_setmoney"] = arg =>
        {
            int money = int.Parse(arg, CultureInfo.InvariantCulture);
            foreach (int p in R().Game.HumanPlayersIndexes) { var ps = R().Game.GetPlayerState(p, deterministic: true); ps.AddMoney(money - ps.Money); }
        };
        Autotest.Commands["pu_money"] = _ => _moneyMark = R().Game.GetPlayerState(R().Game.LocalPlayerIndex, deterministic: true).Money;

        Autotest.Fields["pu_scouts"] = () => R().State.Pursuit.Scouts.Count;
        Autotest.Fields["pu_scoutdist"] = () => R().State.Pursuit.Scouts.FirstOrDefault() is { } s && Sim().Ships[s.Ship] is { } sh ? sh.DetWorldCenter.DistanceTo(s.RelayAt) : -1;
        Autotest.Fields["pu_blockaders"] = () => R().State.Pursuit.Blockaders.Count;
        Autotest.Fields["pu_blockade"] = () => R().State.Pursuit.BlockadeRelay is null ? 0 : 1;
        Autotest.Fields["pu_outage"] = () => Outage.SecondsLeft(R());
        Autotest.Fields["pu_outage_due"] = () => R().State.Pursuit.OutageDueAt;
        Autotest.Fields["pu_hacker"] = () => R().State.Pursuit.Hacker is null ? 0 : 1;
        Autotest.Fields["pu_hacker_done"] = () => R().State.Pursuit.HackerDone ? 1 : 0;
        Autotest.Fields["pu_hacker_buttons"] = () => _hackerButtons;
        Autotest.Fields["pu_hacker_price"] = () => Hacker.Price(R());
        Autotest.Fields["pu_moneyspent"] = () => _moneyMark - R().Game.GetPlayerState(R().Game.LocalPlayerIndex, deterministic: true).Money;
        Autotest.Fields["pu_ramp"] = () => Director.RampFactor(R());
        Autotest.Fields["pu_fleet"] = () => Director.FleetFactor(R(), Sim());
        Autotest.Fields["pu_interval"] = () => Director.IntervalFactor(R());
        Autotest.Fields["pu_events"] = () => R().State.Pursuit.Started.Count;
        foreach (string kind in new[] { PacedKinds.Blockade, PacedKinds.Outage, PacedKinds.Hacker, PacedKinds.RelayLockdown })
        {
            Autotest.Fields["pu_may_" + kind.ToLowerInvariant()] = () => Pacer.MayStart(R(), kind) ? 1 : 0;
            Autotest.Fields["pu_count_" + kind.ToLowerInvariant()] = () => Pacer.Count(R(), kind);
        }
    }

    static int _hackerButtons;
    static long _moneyMark;
}

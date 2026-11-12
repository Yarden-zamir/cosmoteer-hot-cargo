using System.Globalization;
using System.Text;
using Cosmoteer.GalaxyMap;
using Cosmoteer.Ships;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.MultiChoice;
using HotCargo.SharedChoice;
using HotCargo.Tech;

namespace HotCargo.Relays;

/// <summary>Autotest commands and fields of the Relays area. Commands that change the sim run under "mp" in co-op tests.</summary>
static class RelaysAutotest
{
    static Run R() => Autotest.RequireRun();

    static double _markMoney;

    internal static void Register()
    {
        // rl_set <RelaysTunables field> <value>: changes a live tunable for this run.
        Autotest.Commands["rl_set"] = arg => Autotest.SetField(R().T.Relays, arg);
        // rl_reroll: forgets the neighbour rolls of this system, so the next tick rolls them again with the tunables now.
        Autotest.Commands["rl_reroll"] = _ =>
        {
            var s = R().State.Relays;
            s.RolledFor = null;
            s.Rolled.Clear();
            s.MarkedNode = null;
        };
        // rl_mark closed|fee|pirates: marks the first unvisited neighbour with that kind, like a roll.
        Autotest.Commands["rl_mark"] = arg => RelaysArea.Mark(R(), Next(), Enum.Parse<LockKind>(arg, ignoreCase: true));
        // rl_here closed|fee|pirates: starts a lockdown of that kind in this system, as if the players just jumped in.
        Autotest.Commands["rl_here"] = arg =>
        {
            var run = R();
            var kind = Enum.Parse<LockKind>(arg, ignoreCase: true);
            if (kind == LockKind.Fee && run.Game.CurrentMapNode.ControllingFaction is null) throw new InvalidOperationException("this system has no controlling faction for a fee");
            run.State.Relays.Active = new Lockdown { Node = run.Game.CurrentMapNode.UniqueID, Kind = kind, Since = run.State.GameSeconds };
            Log.Info($"AUTOTEST relays: lockdown here: {kind}");
        };
        // rl_tostation: single player. Moves the player ship next to a station that collects the fee.
        Autotest.Commands["rl_tostation"] = _ =>
        {
            var run = R();
            var station = run.Game.Sim.Ships.AllShips.Where(s => RelaysArea.IsFeeStation(run, s)).OrderBy(s => s.UniqueID).FirstOrDefault()
                ?? throw new InvalidOperationException("no station of the controlling faction");
            MoveNear(station);
        };
        // rl_toguard: single player. Moves the player ship next to the first live guard.
        Autotest.Commands["rl_toguard"] = _ => MoveNear(FirstGuard());
        // rl_pay: the shared choice that pays the fee at the nearest fee station.
        Autotest.Commands["rl_pay"] = _ =>
        {
            var run = R();
            var station = run.Game.Sim.Ships.AllShips.Where(s => RelaysArea.IsFeeStation(run, s))
                .OrderBy(s => s.DetWorldCenter.DistanceSquaredTo(TechAutotest.PlayerShip().DetWorldCenter)).First();
            run.Game.NetManager.EnqueueInput(new SharedChoiceInput(RelayChoiceDomain.Name, RelayChoiceDomain.Serial(station), SharedChoiceAction.Choose, (int)RelayOption.PayFee));
        };
        // rl_pick money <credits|price|max> | res <amount|max>: selects a bribe option on the first guard's page (res: the first resource option).
        Autotest.Commands["rl_pick"] = arg =>
        {
            var run = R();
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var guard = FirstGuard();
            var options = RelayMultiDomain.OptionsFor(run, run.Game.Sim);
            int option = parts[0] == "money" ? RelayMultiDomain.MoneyOption : options.First(o => o.Id != RelayMultiDomain.MoneyOption).Id;
            int value = parts[1] switch
            {
                "max" => options.First(o => o.Id == option).Max,
                "price" => RelaysArea.BribePrice(run),
                _ => int.Parse(parts[1], CultureInfo.InvariantCulture),
            };
            run.Game.NetManager.EnqueueInput(new MultiChoiceInput(RelayMultiDomain.Name, RelayChoiceDomain.Serial(guard), option, true, value));
        };
        // rl_bribe | rl_refuse: the shared choices of the first guard's page.
        Autotest.Commands["rl_bribe"] = _ => Choose(RelayOption.Bribe);
        Autotest.Commands["rl_refuse"] = _ => Choose(RelayOption.Refuse);
        // rl_killguards: destroys every guard (the "fight" way out).
        Autotest.Commands["rl_killguards"] = _ =>
        {
            var run = R();
            foreach (var id in RelaysArea.ActiveHere(run)?.Guards ?? [])
                if (RelaysArea.Guard(run.Game.Sim, id) is { } g) run.Game.Sim.Ships.Remove(g, dispose: true, handleLostShip: false);
        };
        // rl_page: logs the text, rows and buttons of the first guard's page (or the fee station's page).
        Autotest.Commands["rl_page"] = _ =>
        {
            var run = R();
            var callee = RelaysArea.ActiveHere(run)?.Kind == LockKind.Pirates ? FirstGuard()
                : run.Game.Sim.Ships.AllShips.Where(s => RelaysArea.IsFeeStation(run, s)).OrderBy(s => s.UniqueID).First();
            var page = Ui.CommsHail.Page(TechAutotest.PlayerShip(), callee, null) ?? throw new InvalidOperationException("no relay page");
            Log.Info($"AUTOTEST relays page: {page.PrimaryDialogue} | rows: {string.Join("; ", page.Rows.Select(r => r.Text))} | buttons: {string.Join("; ", page.Buttons.Select(b => b.Text))}");
        };
        Autotest.Commands["rl_mark_money"] = _ => _markMoney = R().Game.LocalPlayerState.Money;
        // rl_tooltip: logs the map tag and tooltip line of the marked node.
        Autotest.Commands["rl_tooltip"] = _ =>
        {
            var node = MarkedNode();
            Log.Info($"AUTOTEST relays map: tag='{MapText(node)}' tip='{MapPatches.TooltipLine(R(), node)}'");
        };

        Autotest.Fields["rl_marked"] = () => R().State.Relays.MarkedNode is null ? 0 : (int)R().State.Relays.MarkedKind;
        Autotest.Fields["rl_started"] = () => R().State.Relays.Started;
        Autotest.Fields["rl_active"] = () => RelaysArea.ActiveHere(R()) is { } a ? (int)a.Kind : 0;
        Autotest.Fields["rl_blocks"] = () => RelaysArea.Blocks(R()) ? 1 : 0;
        Autotest.Fields["rl_guards"] = () => RelaysArea.ActiveHere(R())?.Guards.Count(id => RelaysArea.Guard(R().Game.Sim, id) is not null) ?? 0;
        Autotest.Fields["rl_bribefailed"] = () => RelaysArea.ActiveHere(R())?.BribeFailed == true ? 1 : 0;
        Autotest.Fields["rl_ready"] = () => R().Game.PlayersReadyToJump.Count;
        Autotest.Fields["rl_spent"] = () => _markMoney - R().Game.LocalPlayerState.Money;
        Autotest.Fields["rl_fee"] = () => RelaysArea.FeeCredits(R());
        Autotest.Fields["rl_price"] = () => RelaysArea.BribePrice(R());
        Autotest.Fields["rl_resources"] = () => RelaysArea.BribeResources(R(), R().Game.Sim).Sum(r => r.Held);
        // The map tag shows on the marked node: the vanilla node text that the galaxy map draws holds the tag of its kind.
        Autotest.Fields["rl_maptag"] = () => R().State.Relays.MarkedNode is null ? 0
            : MapText(MarkedNode()).Contains(RelaysArea.Text("Tag" + R().State.Relays.MarkedKind), StringComparison.Ordinal) ? 1 : 0;
        Autotest.Fields["rl_tipline"] = () => R().State.Relays.MarkedNode is null ? 0 : MapPatches.TooltipLine(R(), MarkedNode()).Length > 0 ? 1 : 0;
    }

    static MapNode Next()
    {
        var run = R();
        return run.Game.CurrentMapNode.Connections.Where(n => !run.State.VisitedNodes.Contains(n.UniqueID)).OrderBy(n => n.UniqueID).FirstOrDefault()
            ?? throw new InvalidOperationException("no unvisited neighbor system");
    }

    static MapNode MarkedNode() =>
        R().State.Relays.MarkedNode is { } id ? R().Game.Map.Nodes[id] ?? throw new InvalidOperationException("marked node is not on the map") : throw new InvalidOperationException("no marked node");

    static string MapText(MapNode node)
    {
        var sb = new StringBuilder();
        R().Mode.GetMapNodeText(node, sb);
        return sb.ToString();
    }

    static Ship FirstGuard()
    {
        var run = R();
        var a = RelaysArea.ActiveHere(run) ?? throw new InvalidOperationException("no lockdown here");
        return a.Guards.Select(id => RelaysArea.Guard(run.Game.Sim, id)).FirstOrDefault(g => g is not null) ?? throw new InvalidOperationException("no live guard");
    }

    static void MoveNear(Ship target)
    {
        var ship = TechAutotest.PlayerShip();
        Autotest.Teleport(ship, Autotest.ClearSpotNear(R().Game.Sim, target.DetWorldCenter, target.WorldBoundingRadius + 600f, ship.WorldBoundingRadius));
        Log.Info($"AUTOTEST relays: {ship.Name} next to {target.Name}");
    }

    static void Choose(RelayOption option) =>
        R().Game.NetManager.EnqueueInput(new SharedChoiceInput(RelayChoiceDomain.Name, RelayChoiceDomain.Serial(FirstGuard()), SharedChoiceAction.Choose, (int)option));
}

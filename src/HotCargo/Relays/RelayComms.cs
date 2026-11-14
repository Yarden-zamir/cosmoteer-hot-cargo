using Cosmoteer.Game;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling.Random;
using HotCargo.Core;
using HotCargo.MultiChoice;
using HotCargo.SharedChoice;
using HotCargo.Tech;
using HotCargo.Ui;
using HotCargo.Ux;

namespace HotCargo.Relays;

/// <summary>Shared choice options of relay pages (spec 956, 958, 959).</summary>
public enum RelayOption { PayFee = 1, Bribe = 2, Refuse = 3 }

/// <summary>
/// Spec 956, 958: the fee page at the faction's stations and the bribe page at the pirate guards, in ship comms.
/// Relays registers before Diplomacy, so a guard's page belongs to the relays domain (one shared domain per page).
/// </summary>
internal sealed class RelayHail : IHailProvider
{
    public static readonly RelayHail Instance = new();

    public bool Answers(Ship caller, Ship callee)
    {
        if (Run.Current is not { } run || RelaysArea.ActiveHere(run) is not { Open: false } a) return false;
        return a.Kind switch
        {
            LockKind.Fee => RelaysArea.IsFeeStation(run, callee),
            LockKind.Pirates => !a.BribeFailed && a.Guards.Contains(callee.UniqueID),
            _ => false,
        };
    }

    public void Build(Ship caller, Ship callee, HailPage page)
    {
        var run = Run.Current ?? throw new InvalidOperationException("no run");
        var sim = caller.Sim ?? throw new InvalidOperationException("caller has no sim");
        if (page.Shared is { } owner && owner.Domain != RelayChoiceDomain.Name) return;
        var a = RelaysArea.ActiveHere(run) ?? throw new InvalidOperationException("relay page without a lockdown");
        int serial = RelayChoiceDomain.Serial(callee);
        int player = run.Game.LocalPlayerIndex;
        page.Shared = (RelayChoiceDomain.Name, serial);
        if (a.Kind == LockKind.Fee)
        {
            int fee = RelaysArea.FeeCredits(run);
            page.AddText(RelaysArea.Format("FeeAskFmt", callee.Metadata.XmlShipName, RelaysArea.Cost(run, fee)));
            bool ok = Diplomacy.Situation.CanPay(run, player, fee);
            page.AddChoice(RelaysArea.Format("FeePayFmt", RelaysArea.Cost(run, fee)), (int)RelayOption.PayFee, ok, ok ? null : RelaysArea.Text("NoMoney"), close: true);
            return;
        }
        // The bribe replaces the vanilla pirate surrender (its lower price would undercut the bribe), so the page has only relay options.
        page.PrimaryDialogue = "";
        page.Buttons.Clear();
        page.Multi = (RelayMultiDomain.Name, serial);
        int price = RelaysArea.BribePrice(run);
        page.AddText(RelaysArea.Format("BribeAskFmt", callee.Metadata.XmlShipName, RelaysArea.Cost(run, price)));
        var picks = MultiChoices.Draft(run, RelayMultiDomain.Name, serial);
        foreach (var option in RelayMultiDomain.OptionsFor(run, sim))
        {
            var pick = picks.FirstOrDefault(p => p.Option == option.Id);
            var spec = new SliderSpec("relaybribe" + option.Id, "", option.Min, option.Max, option.Step, option.Min, v => Bribes.ValueText(run, sim, option.Id, v));
            int value = pick?.Value ?? CommsHail.SliderValue(callee, spec);
            page.AddRow(new HailRow("relaybribe" + option.Id, Bribes.RowText(run, sim, option.Id), option.Id, spec, Active: true, Toggle: true, Selected: pick is not null, Value: value));
        }
        long offer = Bribes.Value(run, sim, picks);
        float chance = RelaysModel.BribeChance(offer, price, run.T.Relays.BribeMinShare);
        page.AddText(RelaysArea.Format("BribeOfferFmt", RelaysArea.Cost(run, (int)Math.Min(int.MaxValue, offer)), (int)MathF.Round(chance * 100f)));
        bool allowed = Bribes.Allowed(run, sim, player, picks, out string why);
        page.AddChoice(RelaysArea.Text("BribeSubmit"), (int)RelayOption.Bribe, allowed, allowed ? null : why);
        page.AddChoice(RelaysArea.Text("BribeRefuse"), (int)RelayOption.Refuse, close: true);
    }
}

/// <summary>The relay pages as a shared choice domain. The serial is the hailed ship's ID.</summary>
internal sealed class RelayChoiceDomain : ISharedChoiceDomain
{
    public const string Name = "relays";
    public static readonly RelayChoiceDomain Instance = new();

    string ISharedChoiceDomain.Name => Name;

    public static int Serial(Ship ship) => unchecked((int)(uint)ship.UniqueID);

    internal static Ship? Callee(SimRoot sim, int serial)
    {
        var ship = sim.Ships[new ObjectID(unchecked((uint)serial))];
        return ship is not null && ship.ExistsInSim(sim) ? ship : null;
    }

    /// <summary>Open while the callee still gets a relay page from some human ship in comms range.</summary>
    public int OptionCount(Run run, int serial)
    {
        if (run.Game.Sim is not { } sim || Callee(sim, serial) is not { } callee) { Log.Info($"relays: page {serial} has no callee"); return 0; }
        var career = (CareerSimModeManager)sim.Mode;
        if (sim.Ships.HumanPlayerShips.Any(h => career.CanShipCallShip(h, callee) && RelayHail.Instance.Answers(h, callee))) return 4;
        Log.Info($"relays: page {serial} closed: " + string.Join("; ", sim.Ships.HumanPlayerShips.Select(h =>
            $"{h.Metadata.PlainShipName} d={h.DetWorldCenter.DistanceTo(callee.DetWorldCenter):0} call={career.CanShipCallShip(h, callee)} answers={RelayHail.Instance.Answers(h, callee)}")));
        return 0;
    }

    public bool Apply(Run run, SimRoot sim, int playerIndex, int serial, int option)
    {
        if (RelaysArea.ActiveHere(run) is not { Open: false } a || Callee(sim, serial) is not { } callee) return false;
        var career = (CareerSimModeManager)sim.Mode;
        if (!sim.Ships.HumanPlayerShips.Any(h => h.Commands.CanExecuteCrewCommandsFrom(playerIndex) && career.CanShipCallShip(h, callee) && RelayHail.Instance.Answers(h, callee)))
        {
            Log.Info($"relays: player {playerIndex} has no ship that talks with {callee.Metadata.PlainShipName}");
            return false;
        }
        switch ((RelayOption)option)
        {
            case RelayOption.PayFee when a.Kind == LockKind.Fee:
                int fee = RelaysArea.FeeCredits(run);
                if (!Diplomacy.Situation.CanPay(run, playerIndex, fee)) return false;
                run.Game.GetPlayerState(playerIndex, deterministic: true).AddMoney(-fee);
                RelaysArea.Open(run, a, RelaysArea.Format("FeePaidFmt", RelaysArea.Cost(run, fee)));
                return true;
            case RelayOption.Bribe when a.Kind == LockKind.Pirates:
                return Bribes.Submit(run, sim, playerIndex, serial, a, callee);
            case RelayOption.Refuse when a.Kind == LockKind.Pirates:
                a.BribeFailed = true;
                MultiChoices.Clear(run, RelayMultiDomain.Name, serial);
                Say(run, RelaysArea.Text("BribeRefused"));
                return true;
            default:
                return false;
        }
    }

    internal static void Say(Run run, string line) =>
        Run.OnNextFrame(() => UxKit.Guard("relay line", () => { run.Game.Gui.ShowAreaName(line); UxKit.ChatLine(run.Game, line); }));
}

/// <summary>Spec 958: the bribe page as a multiselect domain. Option 0 is money; other options are resource kinds (index in the resource list + 1).</summary>
internal sealed class RelayMultiDomain : IMultiChoiceDomain
{
    public const string Name = "relays";
    public static readonly RelayMultiDomain Instance = new();
    public const int MoneyOption = 0;

    string IMultiChoiceDomain.Name => Name;

    public IReadOnlyList<MultiOption>? Options(Run run, int serial)
    {
        if (run.Game.Sim is not { } sim || RelaysArea.ActiveHere(run) is not { Kind: LockKind.Pirates, Open: false, BribeFailed: false } a) return null;
        return a.Guards.Contains(new ObjectID(unchecked((uint)serial))) ? OptionsFor(run, sim) : null;
    }

    /// <summary>Money up to twice the price in steps of a twentieth, then each resource kind up to what the fleet holds.</summary>
    internal static List<MultiOption> OptionsFor(Run run, SimRoot sim)
    {
        int price = RelaysArea.BribePrice(run);
        int step = Math.Max(1, price / 20);
        var list = new List<MultiOption> { new(MoneyOption, step, step * 40, step) };
        foreach (var (option, _, held) in RelaysArea.BribeResources(run, sim))
            list.Add(new MultiOption(option, Math.Min(held, Math.Max(1, held / 20)), held, Math.Max(1, held / 20)));
        return list;
    }
}

/// <summary>Spec 958, 959: value, check and submit of a bribe draft.</summary>
internal static class Bribes
{
    static Cosmoteer.Resources.ResourceRules Resource(int option) => Cosmoteer.GameApp.Rules.Resources[option - 1];

    internal static long Value(Run run, SimRoot sim, int option, int value) =>
        option == RelayMultiDomain.MoneyOption ? value : RelaysModel.ResourceValue(value, Resource(option).BuyPrice, run.T.Relays.BribeResourceRate);

    internal static long Value(Run run, SimRoot sim, IReadOnlyList<Pick> picks) => picks.Sum(p => Value(run, sim, p.Option, p.Value));

    internal static string RowText(Run run, SimRoot sim, int option) =>
        option == RelayMultiDomain.MoneyOption ? RelaysArea.Text("BribeMoney") : RelaysArea.Format("BribeResourceFmt", Resource(option).NameKey.Text);

    internal static string ValueText(Run run, SimRoot sim, int option, int value) =>
        option == RelayMultiDomain.MoneyOption ? RelaysArea.Cost(run, value) : $"{value} ({RelaysArea.Cost(run, (int)Math.Min(int.MaxValue, Value(run, sim, option, value)))})";

    internal static int Money(IReadOnlyList<Pick> picks) => picks.Where(p => p.Option == RelayMultiDomain.MoneyOption).Sum(p => p.Value);

    internal static bool Allowed(Run run, SimRoot sim, int player, IReadOnlyList<Pick> picks, out string why)
    {
        why = "";
        if (picks.Count == 0) { why = RelaysArea.Text("NoPick"); return false; }
        if (!Diplomacy.Situation.CanPay(run, player, Money(picks))) { why = RelaysArea.Text("NoMoney"); return false; }
        return true;
    }

    /// <summary>Rolls the chance of the draft. Success: the chooser pays, the fleet hands over the resources, the guards stand down. Failure: no second offer.</summary>
    internal static bool Submit(Run run, SimRoot sim, int player, int serial, Lockdown a, Ship callee)
    {
        var picks = MultiChoices.Draft(run, RelayMultiDomain.Name, serial);
        if (!Allowed(run, sim, player, picks, out string why)) { Log.Info($"relays: bribe refused by the sim: {why}"); return false; }
        long offer = Value(run, sim, picks);
        float chance = RelaysModel.BribeChance(offer, RelaysArea.BribePrice(run), run.T.Relays.BribeMinShare);
        float roll = new FastRand(((ulong)(uint)serial << 32) | (uint)player, (ulong)(uint)sim.Tick).Single();
        Log.Info($"relays: bribe offer {offer} chance {chance:0.00} roll {roll:0.00}");
        MultiChoices.Clear(run, RelayMultiDomain.Name, serial);
        if (roll >= chance)
        {
            a.BribeFailed = true;
            RelayChoiceDomain.Say(run, RelaysArea.Text("BribeNo"));
            return true;
        }
        int money = Money(picks);
        if (money > 0) run.Game.GetPlayerState(player, deterministic: true).AddMoney(-money);
        var ships = Vaults.HumanShips(sim).OrderBy(sh => sh.Commands.CanExecuteCrewCommandsFrom(player) ? 0 : 1).ThenBy(sh => sh.UniqueID).ToList();
        foreach (var p in picks.Where(p => p.Option != RelayMultiDomain.MoneyOption))
        {
            int left = p.Value;
            foreach (var ship in ships)
            {
                if (left == 0) break;
                left -= ship.Resources.RemoveResources(Resource(p.Option).ID, left);
            }
        }
        if (ships.Count == 0) throw new InvalidOperationException("bribe without a human ship");
        a.Bribed = true;
        RelaysArea.TruceGuards(sim, a);
        RelaysArea.Open(run, a, RelaysArea.Text("BribeYes"));
        return true;
    }
}

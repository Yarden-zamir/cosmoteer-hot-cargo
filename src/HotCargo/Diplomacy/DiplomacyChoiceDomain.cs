using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using HotCargo.Core;
using HotCargo.SharedChoice;

namespace HotCargo.Diplomacy;

/// <summary>
/// Hail choices as a shared choice domain (spec 587, 600 and later), so co-op players see hovers and can vote.
/// The page serial is the hailed ship's ID. An option packs the offer and the slider amount: offer × <see cref="Span"/> + amount,
/// so a vote is for "hand over 3", not for "hand over" alone. The caller is the chooser's nearest ship that can talk with the ship.
/// </summary>
internal sealed class DiplomacyChoiceDomain : ISharedChoiceDomain
{
    public const string Name = "diplomacy";
    public static readonly DiplomacyChoiceDomain Instance = new();
    /// <summary>Amounts are tech units, slider steps or minutes, so they stay far below this.</summary>
    public const int Span = 1000;
    static readonly int Offers = Enum.GetValues<Offer>().Length;

    string ISharedChoiceDomain.Name => Name;

    public static int Serial(Ship ship) => unchecked((int)(uint)ship.UniqueID);

    public static int Option(Offer offer, int amount) =>
        amount is >= 0 and < Span ? (int)offer * Span + amount : throw new ArgumentOutOfRangeException(nameof(amount), amount, "amount outside the option span");

    public static (Offer Offer, int Amount) Decode(int option) => ((Offer)(option / Span), option % Span);

    static Ship? Callee(Run run, int serial)
    {
        if (run.Game.Sim is not { } sim) return null;
        var ship = sim.Ships[new Cosmoteer.Game.ObjectID(unchecked((uint)serial))];
        return ship is not null && ship.ExistsInSim(sim) ? ship : null;
    }

    /// <summary>The chooser's ship that talks with the callee: in comms range, nearest, lowest ID on a tie.</summary>
    static Ship? Caller(SimRoot sim, int player, Ship callee) =>
        sim.Ships.HumanPlayerShips
            .Where(s => s.Commands.CanExecuteCrewCommandsFrom(player) && Situation.Career(sim).CanShipCallShip(s, callee))
            .OrderBy(s => s.DetWorldCenter.DistanceSquaredTo(callee.DetWorldCenter)).ThenBy(s => s.UniqueID)
            .FirstOrDefault();

    /// <summary>Open while some human ship can talk with the ship and gets Hot Cargo options.</summary>
    public int OptionCount(Run run, int serial)
    {
        if (run.State.Diplomacy.Punish?.Serial == serial) return Offers * Span;
        if (Callee(run, serial) is not { } callee || callee.Sim is not { } sim) return 0;
        var career = Situation.Career(sim);
        bool open = sim.Ships.HumanPlayerShips.Any(h => career.CanShipCallShip(h, callee) && DiplomacyHail.Instance.Answers(h, callee));
        return open ? Offers * Span : 0;
    }

    public bool Apply(Run run, SimRoot sim, int playerIndex, int serial, int option)
    {
        if (run.State.Diplomacy.Punish is { } punish && punish.Serial == serial)
            return Deals.Punish(run, sim, playerIndex, Decode(option).Offer);
        if (Callee(run, serial) is not { } callee) return false;
        if (Caller(sim, playerIndex, callee) is not { } caller) { Log.Info($"diplomacy: player {playerIndex} has no ship in comms range of {callee.Metadata.PlainShipName}"); return false; }
        var (offer, amount) = Decode(option);
        return Deals.Choose(run, sim, playerIndex, caller, callee, offer, amount);
    }

    /// <summary>The input that a test command sends for a pick.</summary>
    public static SharedChoiceInput Pick(Ship callee, Offer offer, int amount) => new(Name, Serial(callee), SharedChoiceAction.Choose, Option(offer, amount));
}

/// <summary>The punishment page as a multiselect domain (spec 594): its options while the punishment waits, else closed.</summary>
internal sealed class DiplomacyMultiDomain : MultiChoice.IMultiChoiceDomain
{
    public const string Name = "diplomacy";
    public static readonly DiplomacyMultiDomain Instance = new();

    string MultiChoice.IMultiChoiceDomain.Name => Name;

    public IReadOnlyList<MultiChoice.MultiOption>? Options(Run run, int serial) =>
        run.State.Diplomacy.Punish is { } p && p.Serial == serial && run.Game.Sim is { } sim ? Quotes.PunishOptions(run, sim) : null;
}

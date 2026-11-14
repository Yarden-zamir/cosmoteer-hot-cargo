using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.ObjectText;
using Halfling.Random;
using Halfling.Serialization;
using HotCargo.Core;
using HotCargo.SharedChoice;
using HotCargo.Ui;
using HotCargo.StartJobs;

namespace HotCargo.Pursuit;

/// <summary>
/// Spec 930 to 936: the hacker. A small proxy ship comes, hails the players and offers an outage for money. He leaves
/// HackerStaySeconds after his hail. The choice goes through the shared choice domain "pursuit", so co-op players see it and vote.
/// </summary>
public sealed class Hacker : IHailProvider, ISharedChoiceDomain
{
    public static readonly Hacker Instance = new();
    public static readonly ID<SimObjectSpawner> Tag = new("hc_hacker");
    public const string DomainName = "pursuit";
    public const int OptionPay = 0, OptionDecline = 1;
    /// <summary>Game seconds between the answer and the jump-out.</summary>
    const double LeaveAfterAnswer = 8;

    Hacker() { }

    static SimulationGenerator? _proxy;

    /// <summary>The proxy ship spawn from mod/pursuit/spawns.rules. Read on first use; a change needs a game restart.</summary>
    static SimulationGenerator Proxy => _proxy ??= App.ObjectTextSerializer.ReadFromPath<SimulationGenerator>(
            new OTFile(Path.Combine(Mod.ModRoot, "pursuit", "spawns.rules")), "HackerProxy")
        ?? throw new InvalidDataException("HackerProxy missing in pursuit/spawns.rules");

    /// <summary>Spec 931: the price at the hunter tier.</summary>
    public static int Price(Run run) =>
        PursuitModel.HackerPrice(GameData.TierValueMaximum(Hunters.HunterSpawner.BaseTier(run)), run.T.Pursuit.HackerPriceShare, run.T.Pursuit.HackerPriceRoundTo);

    /// <summary>Spec 930: rolls the hacker of a new system. Any system switch drops a hacker that did not come, and the old proxy stays behind.</summary>
    internal static void OnSystemEntered(Run run, bool firstVisit)
    {
        var s = run.State.Pursuit;
        var t = run.T.Pursuit;
        s.HackerDueAt = -1;
        s.Hacker = null;
        if (!firstVisit || !t.Enabled || run.State.SectorIndex < t.HackerMinSector || run.State.SectorIndex >= run.SectorCount()) return;
        if (!Pacer.MayStart(run, PacedKinds.Hacker)) return;
        var rand = Outage.SectorRand(run, 0x4841_434B_4552UL);
        if (rand.Single() >= t.HackerChance) return;
        s.HackerDueAt = run.State.GameSeconds + t.HackerDelayMin + rand.Single() * (t.HackerDelayMax - t.HackerDelayMin);
        Log.Info($"pursuit: hacker rolled for {s.HackerDueAt:0}s");
    }

    internal static void Tick(Run run, SimRoot sim)
    {
        var s = run.State.Pursuit;
        double now = run.State.GameSeconds;
        if (s.HackerDueAt >= 0 && now >= s.HackerDueAt)
        {
            s.HackerDueAt = -1;
            var verdict = Pacer.Verdict(run, PacedKinds.Hacker);
            if (verdict == PaceVerdict.Ok && run.PressureActive) Spawn(run, sim);
            else Log.Info($"pursuit: hacker skipped ({verdict})");
        }
        if (s.Hacker is not { } id) return;
        var ship = sim.Ships[id];
        if (ship is null || !ship.ExistsInSim(sim) || ship.IsJunk)
        {
            if (sim.Stasis.IsObjectIDInStasis(id)) return;
            Log.Info("pursuit: hacker ship is gone");
            s.Hacker = null;
            return;
        }
        if (!s.HackerHailed && !s.HackerDone && TryHail(sim, ship))
        {
            s.HackerHailed = true;
            s.HackerLeaveAt = now + run.T.Pursuit.HackerStaySeconds;
        }
        if (now >= s.HackerLeaveAt) Leave(run, ship);
    }

    /// <summary>Spec 930: spawns the proxy ship near the fleet. Test commands call it too.</summary>
    internal static bool Spawn(Run run, SimRoot sim)
    {
        var s = run.State.Pursuit;
        if (SimQueries.FleetCenter(sim) is not { } at) { Log.Info("pursuit: hacker skipped, no human ships here"); return false; }
        var before = SimQueries.TaggedShips(sim, Tag).Select(x => x.Id).ToHashSet();
        Proxy.Generate(sim, new FastRand(sim.Rand.UInt64(), 0x5052_4F58UL), loadInstantly: true, rootLoc: at, addExistingSpawnTags: true);
        var spawned = SimQueries.TaggedShips(sim, Tag).Select(x => x.Id).Where(x => !before.Contains(x)).ToList();
        if (spawned.Count != 1) throw new InvalidDataException($"HackerProxy made {spawned.Count} ships tagged hc_hacker, not 1");
        s.Hacker = spawned[0];
        s.HackerHailed = false;
        s.HackerDone = false;
        s.HackerLeaveAt = run.State.GameSeconds + run.T.Pursuit.HackerStaySeconds;
        Pacer.Started(run, PacedKinds.Hacker);
        Log.Info($"pursuit: hacker proxy {s.Hacker} spawned, price {Price(run)}");
        return true;
    }

    /// <summary>Like a vanilla AI hail (CareerSimModeManager.AIHailPlayer): only a human ship that can call back gets it.</summary>
    static bool TryHail(SimRoot sim, Ship hacker)
    {
        var mode = (CareerSimModeManager)sim.Mode;
        var target = sim.Ships.HumanPlayerSignificantShips
            .Where(h => mode.CanShipCallShip(h, hacker) && mode.WillShipAnswerCall(h, hacker))
            .OrderBy(h => h.DetWorldCenter.DistanceSquaredTo(hacker.DetWorldCenter)).ThenBy(h => h.UniqueID)
            .FirstOrDefault();
        if (target is null) return false;
        mode.AIHailPlayer(hacker, target);
        Log.Info($"pursuit: hacker hails {target.Metadata.PlainShipName}");
        return true;
    }

    static void Leave(Run run, Ship ship)
    {
        Log.Info("pursuit: hacker leaves");
        run.State.Pursuit.Hacker = null;
        Removals.JumpOut(run, ship);
        PursuitText.Chat(run, PursuitText.Text("HackerGone"));
    }

    static bool IsOpen(Run run, Ship callee) => run.State.Pursuit.Hacker == callee.UniqueID && !run.State.Pursuit.HackerDone && run.PressureActive;

    // ---- Comms (spec 933)

    public bool Answers(Ship caller, Ship callee) => Run.Current is { } run && run.Game.Sim == caller.Sim && IsOpen(run, callee);

    public void Build(Ship caller, Ship callee, HailPage page)
    {
        if (Run.Current is not { } run || page.Shared is not null) return;
        int price = Price(run);
        string minutes = PursuitModel.Clock(run.T.Pursuit.HackerOutageSeconds);
        double left = Math.Max(0, run.State.Pursuit.HackerLeaveAt - run.State.GameSeconds);
        page.Shared = (DomainName, Serial(callee));
        page.AddText(PursuitText.Format("HackerOfferFmt", minutes, price.ToString("N0"), PursuitModel.Clock(left)));
        bool canPay = Diplomacy.Situation.CanPay(run, run.Game.LocalPlayerIndex, price);
        page.AddChoice(PursuitText.Format("HackerPayFmt", price.ToString("N0"), minutes), OptionPay, active: canPay,
            tooltip: canPay ? null : PursuitText.Text("HackerNoMoney"));
        page.AddChoice(PursuitText.Text("HackerDecline"), OptionDecline, close: true);
    }

    public static int Serial(Ship ship) => unchecked((int)(uint)ship.UniqueID);

    // ---- Shared choice (spec 934)

    string ISharedChoiceDomain.Name => DomainName;

    public int OptionCount(Run run, int serial) =>
        run.State.Pursuit.Hacker is { } id && Serial(id) == serial && !run.State.Pursuit.HackerDone && run.PressureActive ? 2 : 0;

    static int Serial(ObjectID id) => unchecked((int)(uint)id);

    public bool Apply(Run run, SimRoot sim, int playerIndex, int serial, int option)
    {
        var s = run.State.Pursuit;
        if (option == OptionPay)
        {
            int price = Price(run);
            if (!Diplomacy.Situation.CanPay(run, playerIndex, price)) { Log.Info($"pursuit: player {playerIndex} cannot pay the hacker {price}"); return false; }
            run.Game.GetPlayerState(playerIndex, deterministic: true).AddMoney(-price);
            Outage.Start(run, run.T.Pursuit.HackerOutageSeconds, natural: false);
            PursuitText.Chat(run, PursuitText.Format("HackerPaidFmt", price.ToString("N0")));
        }
        else PursuitText.Chat(run, PursuitText.Text("HackerDeclined"));
        Log.Info($"pursuit: hacker answer {option} by player {playerIndex}");
        s.HackerDone = true;
        s.HackerLeaveAt = Math.Min(s.HackerLeaveAt, run.State.GameSeconds + LeaveAfterAnswer);
        CommsHail.Unlock(DomainName, serial);
        return true;
    }
}

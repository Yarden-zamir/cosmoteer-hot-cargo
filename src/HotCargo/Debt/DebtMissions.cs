using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Gui;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Cosmoteer.Simulation;
using Halfling.Random;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Missions;

namespace HotCargo.Debt;

/// <summary>Spec 1420 and 1422: missions that pay the debt instead of money, and the Collect a Debt job.</summary>
static class DebtMissions
{
    public const string CollectMetatype = "HotCargoDebtCollect";
    static readonly ID<MissionMetatype> CollectId = new(CollectMetatype);
    static readonly ID<Cosmoteer.Generators.Simulation.SimObjectSpawner> TradeStationTag = new("trade_station");
    const ulong PayoffSalt = 0x4445_4254_5041_59UL;

    /// <summary>
    /// Spec 1420: the debt that a mission's money pays, or null when it pays money. The roll is a pure function of the mission ID;
    /// the Collect a Debt job always pays the debt.
    /// </summary>
    public static Debt? PaidDebt(Run run, Mission? mission)
    {
        if (mission?.Faction is not { } faction || run.State.Result != RunResult.Running || run.State.Debt.Of(faction.ToString()) is not { } d) return null;
        bool pays = mission.MetatypeID.Equals(CollectId)
                    || new FastRand((uint)mission.MissionID, PayoffSalt).Single() < run.T.Debt.MissionPayoffChance;
        return pays ? d : null;
    }

    static Run? RunOf(GameRoot game) => Run.Current is { } run && run.Game == game ? run : null;

    /// <summary>Spec 1420: the payout takes the money off the debt; money above the balance pays as vanilla money.</summary>
    [HarmonyPatch(typeof(MoneyReward), nameof(MoneyReward.GiveReward))]
    static class Give
    {
        static bool Prefix(MoneyReward __instance, GameRoot game)
        {
            try
            {
                if (RunOf(game) is not { } run || PaidDebt(run, __instance._mission) is not { } d) return true;
                var players = new List<PlayerState>();
                game.GetHumanPlayerStates(players, deterministic: true);
                if (players.Count == 0) throw new InvalidOperationException("a mission pays with no human players");
                long total = (long)__instance.GetAmountPerPlayer(game) * players.Count;
                var (off, money) = DebtModel.PayoffSplit(total, d.Balance);
                DebtDeals.Reduce(run, d, off);
                foreach (var p in players) p.AddMoney((int)(money / players.Count));
                DebtArea.Chat(run, DebtArea.Format("PayoffFmt", Cosmoteer.Extensions.ToXmlCostString((int)off), Lenders.Name(d.Faction)));
                Log.Info($"debt: mission {__instance._mission.MissionID} paid {off} off the {d.Faction} debt and {money} as money");
                return false;
            }
            catch (Exception ex)
            {
                Errors.Report("debt payoff", ex.Message, ex);
                return true;
            }
        }
    }

    /// <summary>Spec 1420: the money line reads "Debt −N to the lender" while the mission pays the debt.</summary>
    [HarmonyPatch(typeof(MoneyReward), nameof(MoneyReward.GetRewardText))]
    static class Text
    {
        static void Postfix(MoneyReward __instance, GameRoot game, ref string? __result)
        {
            if (__result is null || RunOf(game) is not { } run) return;
            try
            {
                if (PaidDebt(run, __instance._mission) is not { } d) return;
                __result = WidgetRules.Instance.MoneyTextColor.FormatWithXml(
                    DebtArea.Format("PayoffLineFmt", __instance.GetScaledMoney(game).ToString("N0"), Lenders.Name(d.Faction)));
            }
            catch (Exception ex) { Errors.Report("debt payoff line", ex.Message, ex); }
        }
    }

    /// <summary>
    /// Spec 1422: once in each system, a trade station of a lender offers Collect a Debt. Checked once a second of sim ticks, in a
    /// stable order (debts in list order, stations by ID).
    /// </summary>
    public static void Tick(Run run, SimRoot sim)
    {
        var s = run.State.Debt;
        if (!run.T.Debt.CollectEnabled || s.Debts.Count == 0 || sim.Tick % 60 != 0 || run.State.Result != RunResult.Running) return;
        var node = sim.NodeId();
        if (s.CollectSystems.Contains(node)) return;
        foreach (var d in s.Debts)
        {
            var station = Cosmoteer.Ships.Special.SpaceStation.GetStations(sim, onlyOperating: true).Select(st => st.PhysicalShip).OfType<Cosmoteer.Ships.Ship>()
                .Where(sh => sh.HasTag(TradeStationTag) && Lenders.FactionOf(sh)?.ID.ToString() == d.Faction).OrderBy(sh => sh.UniqueID).FirstOrDefault();
            if (station is null) continue;
            s.CollectSystems.Add(node);
            SpawnCollect(run, sim, d, station);
            return;
        }
    }

    static Cosmoteer.Generators.Simulation.SimulationGenerator? _generator;

    static string FilePath => Path.Combine(Mod.ModRoot, "debt", "collect.rules");

    /// <summary>The job from debt/collect.rules in the mod folder. It is not part of the game rules tree.</summary>
    static Cosmoteer.Generators.Simulation.SimulationGenerator Generator => _generator ??=
        Halfling.App.ObjectTextSerializer.ReadFromPath<Cosmoteer.Generators.Simulation.SimulationGenerator>(new Halfling.ObjectText.OTFile(FilePath), "Collect")
        ?? throw new InvalidDataException($"Collect missing in {FilePath}");

    /// <summary>Spawns the job at the station like StashArea.Spawn, then sets its client and scales its money (spec 1422).</summary>
    static void SpawnCollect(Run run, SimRoot sim, Debt d, Cosmoteer.Ships.Ship station)
    {
        var before = MissionArea.Offers(sim).Where(m => m.MetatypeID.Equals(CollectId)).Select(m => m.MissionID).ToHashSet();
        var mode = (CareerSimModeManager)sim.Mode;
        var parentTree = new List<Cosmoteer.Generators.Simulation.SimSpawner.SpawnedObject> { new(station.DetWorldCenter) };
        uint batch = 0, fleet = 0;
        foreach (var g in Generator.Spawners.OfType<Cosmoteer.Generators.Simulation.SimObjectSpawner>())
            g.Spawn(sim, sim.Rand, parentTree, 0, ref batch, ref fleet, loadInstantly: true, [], MissionArea.ExistingSpawnTags(sim), mode.GetSpawnFlags(), 0f);
        var spawned = MissionArea.Offers(sim).Where(m => m.MetatypeID.Equals(CollectId) && !before.Contains(m.MissionID)).ToList();
        if (spawned.Count != 1) throw new InvalidDataException($"debt/collect.rules made {spawned.Count} Collect a Debt offers, not 1");
        var mission = spawned[0];
        mission.Faction = d.FactionId;
        foreach (var r in mission.Rewards)
            if (r is MoneyReward m) m.Money = MissionModel.ScaleReward(m.Money, run.T.Debt.CollectRewardFactor, 500);
        Log.Info($"debt: {station.Metadata.PlainShipName} offers Collect a Debt for {d.Faction}");
    }
}

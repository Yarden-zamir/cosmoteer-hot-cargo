using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.ObjectText;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Tech;

namespace HotCargo.Journey;

/// <summary>
/// The Buyer station in the last sector of the run (spec requirements 1, 6 and 7).
/// It is a normal sim object, so it saves with its system.
/// </summary>
public static class Buyer
{
    public static readonly ID<SimObjectSpawner> Tag = new("hc_buyer");

    internal static readonly ID<MissionMetatype> MissionType = new("HotCargoBuyer");

    static SimulationGenerator? _generator;

    /// <summary>
    /// Read from mod/journey/buyer.rules on first use, after the game rules are loaded.
    /// Limit: the file is read outside the game rules tree, so other mods that change the vanilla files it references
    /// are seen, but it cannot use game root references (&amp;/...). Revisit if the Buyer needs tier tables.
    /// </summary>
    static SimulationGenerator Generator => _generator ??= LoadGenerator();

    static SimulationGenerator LoadGenerator()
    {
        string path = Path.Combine(Mod.ModRoot, "journey", "buyer.rules");
        var file = new OTFile(path);
        return App.ObjectTextSerializer.ReadFromPath<SimulationGenerator>(file, "Buyer")
            ?? throw new InvalidDataException($"Buyer missing in {path}");
    }

    internal static void OnSystemEntered(Run run, bool firstVisit)
    {
        // Limit: the Buyer appears only on the first visit at exactly RunSectorCount. If the tunable drops below the
        // current sector index mid-run, no Buyer appears in this run. Revisit if designers change it mid-run.
        if (!firstVisit || run.State.SectorIndex != run.T.Journey.RunSectorCount) return;
        Spawn(run);
    }

    internal static void OnNewRun(Run run)
    {
        if (run.T.Journey.RunSectorCount <= 1) Spawn(run);
    }

    /// <summary>Spawns the Buyer into the current system. Deterministic: uses the sim random source only.</summary>
    public static void Spawn(Run run)
    {
        var sim = run.Game.Sim;
        var careerSim = (CareerSimModeManager)sim.Mode;
        int before = Count(sim);
        Generator.Generate(sim, sim.Rand, loadInstantly: true, rootLoc: default, areaRadius: 0f,
            addExistingSpawnTags: true, extraTagLocs: null, spawnFlags: careerSim.GetSpawnFlags());
        int after = Count(sim);
        if (after <= before) throw new InvalidOperationException("the Buyer generator spawned no hc_buyer station (no free location?)");
        Log.Info($"Buyer spawned in {run.Game.CurrentMapNode.Name} (sector {run.State.SectorIndex})");
        ShowWhereAsync(run);
    }

    /// <summary>GUI only: a toast after the jump animation ends, and the Buyer mission tracked on the HUD.</summary>
    static async void ShowWhereAsync(Run run)
    {
        // The jump shows the system name for about 4 s after the switch. Wait so the two texts do not overlap.
        await App.Clock.UntilElapsed(6f);
        if (Run.Current != run) return;
        run.Game.Gui.ShowAreaName(Strings.GetText("HotCargoJourney/BuyerHere"));
        var missions = run.Mode.Missions;
        var mission = missions.FirstOrDefault(m => m.MetatypeID == MissionType);
        if (mission is not null && !missions.IsTracked(mission)
            && missions.TrackedMissions.Count < GameApp.Rules.CareerMode.Gui.Missions.MaxTrackedMissions)
            missions.TrackMission(mission);
    }

    /// <summary>Spec 7: BuyerPriceFactor × the run's Buyer price multiplier (an advance from the Buyer's agent event lowers it).</summary>
    public static float PriceFactor(Run run) => run.T.Journey.BuyerPriceFactor * run.State.BuyerPriceMultiplier;

    public static bool IsBuyer(Ship ship) => ship.HasTag(Tag);

    /// <summary>Buyer stations in the sim, loaded or in stasis.</summary>
    public static int Count(SimRoot sim) => SimQueries.TaggedShips(sim, Tag).Count;

    /// <summary>
    /// Spec 6 and 7: the first tech delivery to the Buyer wins the run. Later deliveries raise DeliveredUnits and the score.
    /// Called from the sim tick.
    /// </summary>
    internal static void OnDelivered(Run run, int units)
    {
        var s = run.State;
        if (s.Result == RunResult.Lost) return;
        s.DeliveredUnits += units;
        var players = new List<PlayerState>();
        run.Game.GetHumanPlayerStates(players, deterministic: true);
        s.Score = Score(players.Sum(p => (long)p.Money), s.DeliveredUnits, UnitPrice(run));
        Log.Info($"Buyer delivery: {units} units, {s.DeliveredUnits} in total, score {s.Score}");
        run.Win(Strings.GetText("HotCargoJourney/WinReason"));
    }

    /// <summary>
    /// Spec 7, as written: money + delivered units × the Buyer price of one unit.
    /// Open decision: the money already includes the Buyer payment, so delivered units count twice.
    /// To count them once, return money. To score only the tech, return deliveredUnits × unitPrice.
    /// </summary>
    internal static long Score(long money, int deliveredUnits, long unitPrice) => money + deliveredUnits * unitPrice;

    /// <summary>What the Buyer pays for one unit now: the tech value × PriceFactor, as PricePatch computes it.</summary>
    internal static long UnitPrice(Run run) => (long)(TechPrice.Sell(run) * PriceFactor(run));

    /// <summary>
    /// Spec 7: a sale of tech to the Buyer pays tech value × PriceFactor (BuyerPriceFactor × the run multiplier).
    /// Runs after the Tech area's general tech price postfix (lower priority), so it multiplies the final tech price.
    /// The Buyer is the only trading ship with the Neutral player index (see buyer.rules).
    /// Limit: a mod that adds other Neutral trade stations gets the Buyer price too. Revisit if such a mod is in use.
    /// </summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.GetResourceSellRefund))]
    static class PricePatch
    {
        [HarmonyPriority(Priority.Low)]
        static void Postfix(CareerSimModeManager __instance, ID<ResourceRules> resourceType, int? destPlayerIndex, ref int __result)
        {
            if (destPlayerIndex != PlayerIndexes.Neutral || resourceType != Vaults.TechId) return;
            if (Run.Current is not { } run || run.Game.Sim != __instance.Sim) return;
            __result = (int)(__result * PriceFactor(run));
        }
    }

    /// <summary>Detects a completed sale: the crew delivered the units and the players got the money. Runs in the sim tick.</summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.OnResourceTradeDelivery))]
    static class DeliveryPatch
    {
        static void Postfix(CareerSimModeManager __instance, TradeType tradeType, ID<ResourceRules> resourceType, int amount, Ship? sinkShip)
        {
            if (tradeType != TradeType.Sell || amount <= 0 || resourceType != Vaults.TechId || sinkShip is null || !IsBuyer(sinkShip)) return;
            if (Run.Current is not { } run || run.Game.Sim != __instance.Sim) return;
            OnDelivered(run, amount);
        }
    }
}

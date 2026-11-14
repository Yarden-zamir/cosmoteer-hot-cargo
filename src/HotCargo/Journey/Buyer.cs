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
using HotCargo.StartJobs;

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
        // At or after RunSectorCount, so that a value lowered mid-run below the current sector still gives a Buyer in the next new system.
        if (!firstVisit || run.State.BuyerSpawned || run.State.SectorIndex < run.SectorCount()) return;
        Spawn(run);
    }

    internal static void OnNewRun(Run run)
    {
        if (run.SectorCount() <= 1) Spawn(run);
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
        run.State.BuyerSpawned = true;
        Log.Info($"Buyer spawned in {run.Game.CurrentMapNode.Name} (sector {run.State.SectorIndex})");
        // A co-op launch runs this on a worker thread (Buyer in the first sector), so the GUI part starts on the next frame.
        Run.OnNextFrame(() => ShowWhereAsync(run));
    }

    /// <summary>GUI only: a toast after the jump animation ends, and the Buyer mission tracked on the HUD.</summary>
    static async void ShowWhereAsync(Run run)
    {
        // The jump shows the system name for about 4 s after the switch. Wait so the two texts do not overlap.
        await App.Clock.UntilElapsed(6f);
        if (Run.Current != run) return;
        Ux.UxKit.CenterText(run.Game, Strings.GetText("HotCargoJourney/BuyerHere"));
        var missions = run.Mode.Missions;
        var mission = missions.FirstOrDefault(m => m.MetatypeID == MissionType);
        if (mission is not null && !missions.IsTracked(mission)
            && missions.TrackedMissions.Count < GameApp.Rules.CareerMode.Gui.Missions.MaxTrackedMissions)
            missions.TrackMission(mission);
    }

    /// <summary>Spec 7, 1201: BuyerPriceFactor × the run's Buyer price multiplier (an advance from the Buyer's agent event lowers it) × the start job's factor.</summary>
    public static float PriceFactor(Run run) => run.T.Journey.BuyerPriceFactor * run.State.BuyerPriceMultiplier * StartJobs.StartJobs.BuyerPrice(run);

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
        int held = Run.Hooks.CountFleetTech(run.Game.Sim);
        s.Score = Run.Hooks.ScoreAdjust(run, Score(players.Sum(p => (long)p.Money), held, UnitPrice(run)));
        Log.Info($"Buyer delivery: {units} units, {s.DeliveredUnits} in total, {held} still held, score {s.Score}");
        run.Win(Strings.GetText("HotCargoJourney/WinReason"));
    }

    /// <summary>
    /// Spec 7: money + units still held × the Buyer price of one unit. Delivered units are already in the money (at the Buyer price),
    /// so each unit counts once, and an unsold unit is worth what the Buyer would pay for it.
    /// </summary>
    internal static long Score(long money, int heldUnits, long unitPrice) => money + heldUnits * unitPrice;

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

using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Ships.Special;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Random;
using HotCargo.Core;
using HotCargo.Hunters;

namespace HotCargo.Contracts;

/// <summary>
/// The contract economy (spec 100 to 108): terms of offers, fees, flat heat, the trail, rivals and informants.
/// Every state change runs in the sim tick or in the game's accept input, so it is deterministic in multiplayer.
/// The UI only reads (Preview, Open, TrailPerMinute).
/// </summary>
public static class ContractBook
{
    const double ScanIntervalSeconds = 1.0;

    /// <summary>Offers in the current system in a stable order: stations by ID, then the system list, then the galaxy list.</summary>
    public static List<(Mission Mission, MissionList List)> Offers(SimRoot sim)
    {
        var result = new List<(Mission, MissionList)>();
        foreach (MetaSpaceStation station in SpaceStation.GetStations(sim, onlyOperating: false).OrderBy(s => s.StationID))
            if (station.GetProvidedMissions() is { } list)
                foreach (Mission m in list) result.Add((m, list));
        var simList = ((CareerSimModeManager)sim.Mode).SimGlobalAvailableMissions;
        foreach (Mission m in simList) result.Add((m, simList));
        var gameList = ((CareerGameModeManager)sim.RequireGame().Mode).GameGlobalAvailableMissions;
        foreach (Mission m in gameList) result.Add((m, gameList));
        return result;
    }

    public static ContractTerms? Find(Run run, Mission m) => run.State.Contracts.Terms.GetValueOrDefault(m.MissionID);

    /// <summary>Spec 100: missions of the categories in Contracts.ExcludedCategories are not contracts (no terms, no trail).</summary>
    public static bool IsContract(Run run, Mission m) => !run.T.Contracts.ExcludedCategories.Contains(m.Metatype.CategoryID.ToString());

    /// <summary>The saved terms, or the terms that the next scan will save. Null when the mission is not a contract. Changes no state.</summary>
    public static ContractTerms? Preview(Run run, Mission m) => Find(run, m) ?? (IsContract(run, m) ? Roll(run, m) : null);

    /// <summary>
    /// Rolls the terms of a mission. Rules terms (HotCargoContract) win, then Contracts.Metatypes, then a roll seeded by the mission ID.
    /// The same mission always rolls the same terms, on every client.
    /// </summary>
    static ContractTerms Roll(Run run, Mission m)
    {
        var t = run.T.Contracts;
        var rules = m.Rewards.OfType<ContractTermsCarrier>().FirstOrDefault();
        string metatype = m.MetatypeID.ToString();
        var meta = t.Metatypes.FirstOrDefault(x => x.Metatype == metatype);
        var r = new FastRand(0x4843_434F_4E54_5241UL ^ (uint)m.MissionID.GetHashCode());
        // Fixed draw order, so a tunables change moves values but not which offers are paid.
        float feeRoll = r.Single(), feeUnits = r.Single(new Range<float>(t.FeeMin, t.FeeMax));
        float informantRoll = r.Single(), informantChance = r.Single(new Range<float>(t.InformantChanceMin, t.InformantChanceMax));

        // Spec 682: the difficulty fee factor scales every fee, the rules fees too. The bonus follows the fee.
        float units = (rules?.AcceptFee ?? (feeRoll < t.FeeChance ? feeUnits : 0f)) * RunModes.Difficulty.Pressure(run).ContractFees;
        int tier = m.GetTierRange(m.Game.Sim)?.Max ?? run.CurrentTier;
        float moneyFactor = run.Mode.EconDifficulty.MoneyRewardFactor * run.T.Journey.RewardScale;
        int fee = ContractsModel.Fee(units, GameData.StandardMissionMoney(tier), moneyFactor, t.MoneyRoundTo);
        return new ContractTerms
        {
            Fee = fee,
            Bonus = ContractsModel.Bonus(fee, t.PaidBonusFactor, t.MoneyRoundTo),
            FlatHeat = rules?.FlatHeat ?? meta?.FlatHeat ?? 0f,
            TrailWeight = rules?.TrailWeight ?? meta?.TrailWeight ?? 1f,
            RivalGroup = t.RivalsEnabled ? rules?.RivalGroup ?? "" : "",
            InformantChance = Math.Clamp(rules?.InformantChance ?? (informantRoll < t.InformantShare ? informantChance : 0f), 0f, 1f),
        };
    }

    /// <summary>Saves the terms of a mission and adds its completion bonus. Sim logic only.</summary>
    static ContractTerms? Ensure(Run run, Mission m)
    {
        if (Find(run, m) is { } known) return known;
        if (!IsContract(run, m)) return null;
        var terms = Roll(run, m);
        run.State.Contracts.Terms[m.MissionID] = terms;
        if (terms.Bonus > 0 && !m.Rewards.OfType<ContractBonusReward>().Any()) m.AddReward(new ContractBonusReward(terms.Bonus));
        return terms;
    }

    /// <summary>Accepted contracts that are still open: in the mission log, in progress, with terms.</summary>
    public static IEnumerable<(Mission Mission, ContractTerms Terms)> Open(Run run)
    {
        foreach (Mission m in run.Mode.Missions)
            if (m.Completion == CompletionState.InProgress && Find(run, m) is { } t) yield return (m, t);
    }

    public static float OpenWeight(Run run) => Open(run).Sum(o => o.Terms.TrailWeight);

    public static float TrailPerMinute(Run run, float openWeight) =>
        ContractsModel.TrailPerMinute(openWeight, run.T.Contracts.TrailFreeContracts, run.T.Contracts.TrailHeatPerContractMinute);

    /// <summary>The other offer in this system that accepting m dismisses, or null.</summary>
    public static Mission? RivalOf(Run run, SimRoot sim, Mission m)
    {
        string group = run.T.Contracts.RivalsEnabled ? Preview(run, m)?.RivalGroup ?? "" : "";
        if (group.Length == 0) return null;
        return Offers(sim).Select(o => o.Mission).FirstOrDefault(o => o != m && Find(run, o)?.RivalGroup == group);
    }

    public static string ClientName(Mission m) =>
        m.Faction is { } f ? GameApp.Rules.GetFactionRules(f).NameKey.Text : Strings.GetText("HotCargoContracts/UnknownClient");

    internal static void OnTick(Run run, SimRoot sim, double dt)
    {
        var state = run.State.Contracts;
        state.SecondsSinceScan += dt;
        if (state.SecondsSinceScan >= ScanIntervalSeconds)
        {
            state.SecondsSinceScan = 0;
            Scan(run, sim);
        }
        if (!run.PressureActive) return;
        float trail = TrailPerMinute(run, OpenWeight(run));
        if (trail > 0)
        {
            run.AddHeat(trail * (float)(dt / 60.0), sim);
            TrailHeatAdded += trail * dt / 60.0;
        }
        TickInformants(run, sim);
    }

    /// <summary>Saves terms of new offers, pairs rivals (spec 103) and dismisses offers that rival an open contract.</summary>
    static void Scan(Run run, SimRoot sim)
    {
        var offers = Offers(sim);
        var openGroups = Open(run).Select(o => o.Terms.RivalGroup).Where(g => g.Length > 0).ToHashSet();
        var seen = new List<Mission>();
        foreach (var (m, list) in offers)
        {
            bool isNew = Find(run, m) is null;
            if (Ensure(run, m) is not { } terms) continue;
            if (isNew && terms.RivalGroup.Length == 0 && run.T.Contracts.RivalsEnabled) PairRival(run, m, terms, seen);
            if (run.T.Contracts.RivalsEnabled && terms.RivalGroup.Length > 0 && openGroups.Contains(terms.RivalGroup))
            {
                list.RemoveMission(m);
                Log.Info($"contracts: dismissed offer {m.MetatypeID} {m.MissionID}, it rivals an open contract");
                continue;
            }
            seen.Add(m);
        }
    }

    /// <summary>
    /// Pairs a new offer with an earlier offer of another client: another faction, or another station.
    /// Offers whose targets are each other's faction always pair. Other pairs form with chance RivalPairChance, rolled from the new offer's ID.
    /// </summary>
    static void PairRival(Run run, Mission m, ContractTerms terms, List<Mission> earlier)
    {
        var sim = m.Game.Sim;
        var mine = new List<Cosmoteer.Data.ID<Cosmoteer.Factions.FactionRules>>();
        m.GetEnemyFactions(sim, mine);
        bool Free(Mission o) =>
            Find(run, o) is { RivalGroup.Length: 0 } && (o.Faction != m.Faction || o.SpawnedAt?.ShipID != m.SpawnedAt?.ShipID);
        bool Conflict(Mission o)
        {
            if (o.Faction is not { } of || m.Faction is not { } mf || of == mf) return false;
            var theirs = new List<Cosmoteer.Data.ID<Cosmoteer.Factions.FactionRules>>();
            o.GetEnemyFactions(sim, theirs);
            return mine.Contains(of) || theirs.Contains(mf);
        }
        Mission? rival = earlier.FirstOrDefault(o => Free(o) && Conflict(o));
        if (rival is null && new FastRand(0x5249_5641_4CUL ^ (uint)m.MissionID.GetHashCode()).Single() < run.T.Contracts.RivalPairChance)
            rival = earlier.FirstOrDefault(Free);
        if (rival is null) return;
        string group = $"pair-{m.MissionID}";
        terms.RivalGroup = group;
        Find(run, rival)!.RivalGroup = group;
        Log.Info($"contracts: rivals {m.MetatypeID} {m.MissionID} and {rival.MetatypeID} {rival.MissionID}");
    }

    /// <summary>Spec 102: the player who accepts pays the fee. False when that player has too little money.</summary>
    internal static bool CanPay(Run run, Mission m, int playerIndex) =>
        (Preview(run, m)?.Fee ?? 0) <= run.Game.GetPlayerState(playerIndex, deterministic: true).Money;

    /// <summary>Runs inside the game's accept input, after the mission moved to the log. Sim logic.</summary>
    internal static void OnAccepted(Run run, SimRoot sim, Mission m, int playerIndex)
    {
        if (Ensure(run, m) is not { } terms) return;
        var contracts = run.State.Contracts;
        if (terms.Fee > 0) run.Game.GetPlayerState(playerIndex, deterministic: true).AddMoney(-terms.Fee);
        // The payer collects the bonus (spec 102). An offer that the scan never saw gets its bonus reward here.
        if (terms.Bonus > 0)
        {
            var bonus = m.Rewards.OfType<ContractBonusReward>().FirstOrDefault();
            if (bonus is null) m.AddReward(bonus = new ContractBonusReward(terms.Bonus));
            bonus.PayerIndex = playerIndex;
        }
        if (terms.FlatHeat != 0) run.AddHeat(terms.FlatHeat, sim);
        var dismissed = new List<string>();
        // RivalsEnabled off also stops the rival groups that offers saved before the switch.
        if (run.T.Contracts.RivalsEnabled && terms.RivalGroup.Length > 0)
            foreach (var (o, list) in Offers(sim))
                if (o != m && Find(run, o)?.RivalGroup == terms.RivalGroup)
                {
                    list.RemoveMission(o);
                    dismissed.Add(o.Name.Text);
                }
        bool informant = terms.InformantChance > 0 && sim.Rand.Single() < terms.InformantChance;
        if (informant)
        {
            var t = run.T.Contracts;
            contracts.Informants[m.MissionID] = run.State.GameSeconds + sim.Rand.Single(new Range<float>(t.InformantDelayMin, t.InformantDelayMax));
        }
        Log.Info($"contracts: accepted {m.MetatypeID} {m.MissionID} by player {playerIndex}: fee={terms.Fee} bonus={terms.Bonus} flatHeat={terms.FlatHeat} " +
                 $"trail={terms.TrailWeight} rival='{terms.RivalGroup}' dismissed={dismissed.Count} informant={terms.InformantChance:0.00} sellout={informant}");
        Onboarding.RunLog.ContractAccepted(run, m.Name.Text, terms.Fee, dismissed.Count);
        // UI only: a line in the chat log for each dismissed rival, so the players see why an offer vanished.
        foreach (string name in dismissed)
            Ux.UxKit.ChatLine(run.Game, Strings.FormatText("HotCargoContracts/RivalDismissedChatFmt", StringTools.EscapeForXml(name), StringTools.EscapeForXml(m.Name.Text)));
    }

    /// <summary>Spec 104: a client sells the players out when its time comes, while the contract is still open.</summary>
    static void TickInformants(Run run, SimRoot sim)
    {
        var pending = run.State.Contracts.Informants;
        if (pending.Count == 0) return;
        foreach (var (id, at) in pending.OrderBy(kv => kv.Key).ToList())
        {
            var m = run.Mode.Missions.FirstOrDefault(x => x.MissionID.Equals(id));
            if (m is null || m.Completion != CompletionState.InProgress) { pending.Remove(id); continue; }
            if (run.State.GameSeconds < at) continue;
            pending.Remove(id);
            SellOut(run, sim, m);
        }
    }

    /// <summary>The sell-out: heat, a hunter wave, and a warning that names the client and the contract.</summary>
    internal static void SellOut(Run run, SimRoot sim, Mission m)
    {
        var t = run.T.Contracts;
        run.State.Contracts.SoldOut.Add(m.MissionID);
        run.AddHeat(t.InformantHeat, sim);
        if (t.InformantShips > 0)
            Errors.Guard("informant wave", () => HunterSpawner.SpawnWave(run, sim, t.InformantShips, run.CurrentTier, "Informant", firstOfStage: false));
        string client = StringTools.EscapeForXml(ClientName(m)), contract = StringTools.EscapeForXml(m.Name.Text);
        Log.Info($"contracts: informant sold out {m.MetatypeID} {m.MissionID} ({ClientName(m)})");
        Onboarding.RunLog.SoldOut(run, ClientName(m), m.Name.Text, t.InformantHeat);
        InformantWarnings++;
        // UI only, after the wave alert so that this text replaces the generic one.
        Ux.UxKit.CenterText(run.Game, Strings.FormatText("HotCargoContracts/InformantBannerFmt", client, contract));
        Ux.UxKit.ChatLine(run.Game, Strings.FormatText("HotCargoContracts/InformantChatFmt", client, contract, t.InformantHeat));
    }

    /// <summary>Heat that the trail added since game start. For autotests only; not sim state.</summary>
    internal static double TrailHeatAdded;

    /// <summary>Informant warnings shown since game start. For autotests only; not sim state.</summary>
    internal static int InformantWarnings;
}

using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Resources;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Serialization;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Events;
using HotCargo.Journey;
using HotCargo.Tech;
using HotCargo.Ux;

namespace HotCargo.Onboarding;

/// <summary>Kind of a run log line. The career log tab colors the line by kind.</summary>
public enum RunLogKind { Sector, Stage, Sale, Delivery, Contract, Chain, Event, Loss, Won, Lost, Job, Debt }

/// <summary>One line of the run log (spec 730). The text is XML for a game label, written in the language of the machine that logged it.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class RunLogEntry
{
    [Serialize] public double Seconds;
    [Serialize] public int Sector;
    [Serialize] public RunLogKind Kind;
    [Serialize] public string Text = "";
}

/// <summary>
/// Spec 730 to 734: Hot Cargo events in the career log. Areas call Add from sim logic, so every co-op machine logs the same lines.
/// The log is not in the integrity hash: it only shows what already happened.
/// </summary>
public static class RunLog
{
    /// <summary>Limit: the oldest lines drop above this count. A run is short, so it holds a whole run. Revisit if runs get much longer.</summary>
    const int MaxEntries = 400;

    /// <summary>Adds a line. Call only from sim logic (sim tick, Run.Hooks, MP inputs).</summary>
    public static void Add(Run run, RunLogKind kind, string xml)
    {
        var log = run.State.Log;
        log.Add(new RunLogEntry { Seconds = run.State.GameSeconds, Sector = run.State.SectorIndex, Kind = kind, Text = xml });
        if (log.Count > MaxEntries) log.RemoveRange(0, log.Count - MaxEntries);
        Log.Info($"run log [{kind}] {xml}");
    }

    internal static string Text(string key) => Strings.GetText("HotCargoRunLog/" + key);

    internal static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoRunLog/" + key, args);

    static string Escape(string text) => StringTools.EscapeForXml(text);

    internal static void Register()
    {
        Run.Hooks.StageStarted += (run, _, stage) =>
        {
            string name = run.T.Heat.Stages[stage].Name;
            Add(run, RunLogKind.Stage, Format("StageFmt", Escape(name), UxKit.StageText(name)));
        };
        Run.Hooks.StrikeWave += (run, _) => Add(run, RunLogKind.Stage, Text("StrikeWave"));
        Run.Hooks.SystemEntered += (run, firstVisit) =>
        {
            if (!firstVisit) return;
            var sector = run.Mode.GetMapNodeData(run.Game.CurrentMapNode).SectorType;
            Add(run, RunLogKind.Sector, Format("SectorFmt", run.State.SectorIndex, run.T.Journey.RunSectorCount,
                Escape(run.Game.CurrentMapNode.Name), sector.NameKey.Text));
        };
        Run.Hooks.NewRunStarted += run => Add(run, RunLogKind.Sector, Format("StartFmt", run.T.Tech.StartingTechUnits, run.T.Journey.RunSectorCount));
        Run.Hooks.RunEnded += run =>
        {
            if (run.State.Result == RunResult.Won) Add(run, RunLogKind.Won, Format("WonFmt", run.State.Score.ToString("N0")));
            else Add(run, RunLogKind.Lost, Format("LostFmt", Escape(run.State.EndReason)));
        };
        EventRunner.OutcomeChosen += OnOutcome;
    }

    /// <summary>An event choice. A choice that leads to a follow-up page logs nothing; the last page of the event logs the result.</summary>
    static void OnOutcome(Run run, SimRoot sim, int serial, int player, OutcomeRules outcome)
    {
        if (run.State.Events.LastResult is not { } r || r.Serial != serial) return;
        string lines = string.Join("  ", r.Lines);
        Add(run, RunLogKind.Event, lines.Length > 0 ? Format("EventFmt", Escape(r.Title), lines) : Format("EventPlainFmt", Escape(r.Title)));
    }

    /// <summary>
    /// Spec 731: a contract accept, from the contract economy (ContractBook.OnAccepted). Mission names are XML already
    /// (a chain stage name carries a color tag), so they go in as they are.
    /// </summary>
    public static void ContractAccepted(Run run, string contractXml, int fee, int dismissedRivals)
    {
        string text = fee > 0 ? Format("ContractFeeFmt", contractXml, fee.ToString("N0")) : Format("ContractFmt", contractXml);
        if (dismissedRivals > 0) text += " " + Format("RivalsFmt", dismissedRivals);
        Add(run, RunLogKind.Contract, text);
    }

    /// <summary>Spec 731: an informant sold the players out (ContractBook.SellOut).</summary>
    public static void SoldOut(Run run, string client, string contractXml, float heat) =>
        Add(run, RunLogKind.Contract, Format("SoldOutFmt", Escape(client), contractXml, heat));

    /// <summary>Spec 731: a chain stage result (ChainRunner.Resolve). Story lines are plain rules text.</summary>
    public static void ChainResult(Run run, string chainTitle, string story, IEnumerable<string> resultLines, bool ended)
    {
        string text = story.Length > 0 ? Format("ChainFmt", Escape(chainTitle), Escape(story)) : Format("ChainPlainFmt", Escape(chainTitle));
        string lines = string.Join("  ", resultLines);
        if (lines.Length > 0) text += "  " + lines;
        if (ended) text += " " + Text("ChainEnded");
        Add(run, RunLogKind.Chain, text);
    }

    /// <summary>Spec 731: a destroyed vault: its tech dropped as crates, or burned (Tech VaultDropPatch).</summary>
    public static void VaultDestroyed(Run run, int units, bool burned) => Add(run, RunLogKind.Loss, Format(burned ? "VaultBurnedFmt" : "VaultDroppedFmt", units));

    /// <summary>
    /// Spec 731: tech sales and Buyer deliveries, when the crew deliver and the station pays. Runs in the sim tick.
    /// Runs before the Journey area's delivery postfix, so a winning delivery logs before the win.
    /// </summary>
    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.OnResourceTradeDelivery))]
    static class TradePatch
    {
        [HarmonyPriority(Priority.High)]
        static void Postfix(CareerSimModeManager __instance, TradeType tradeType, ID<ResourceRules> resourceType, int amount, Ship? sinkShip, Ship? sourceShip)
        {
            if (amount <= 0 || resourceType != Vaults.TechId) return;
            if (Run.Current is not { } run || run.Game.Sim != __instance.Sim || run.State.Result == RunResult.Lost) return;
            Errors.Guard("run log trade", () =>
            {
                if (tradeType == TradeType.Sell && sinkShip is not null && Buyer.IsBuyer(sinkShip))
                    Add(run, RunLogKind.Delivery, Format("DeliveryFmt", amount, Buyer.UnitPrice(run).ToString("N0")));
                else if (tradeType == TradeType.Sell)
                    Add(run, RunLogKind.Sale, Format("SaleFmt", amount, TechPrice.Sell(run).ToString("N0"), Escape(sinkShip?.Metadata.PlainShipName ?? "")));
                else if (tradeType == TradeType.Buy)
                    Add(run, RunLogKind.Sale, Format("BuyBackFmt", amount, TechPrice.Buy(run).ToString("N0")));
            });
        }
    }
}

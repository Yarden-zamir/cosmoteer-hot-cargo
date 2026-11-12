using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Modes.Career;
using Cosmoteer.Simulation;
using Halfling.Network;
using Halfling.Serialization;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.MultiChoice;

/// <summary>One option of a multiselect page: its ID and the range of its value. An option without a slider has Min = Max.</summary>
public readonly record struct MultiOption(int Id, int Min, int Max, int Step)
{
    public bool HasSlider => Max > Min;
}

/// <summary>
/// A kind of multiselect page (spec 590 and later). Register it once at boot with <see cref="MultiChoices.Register"/>, on every
/// machine. <see cref="Options"/> reads sim state only, so every machine checks a draft change the same way.
/// The page's submit and other buttons are shared choices (SharedChoice): the domain's Apply reads the draft with <see cref="MultiChoices.Draft"/>.
/// </summary>
public interface IMultiChoiceDomain
{
    /// <summary>Unique key. Inputs carry it.</summary>
    string Name { get; }
    /// <summary>The options of the open page with this serial, or null when that page is not open (any more).</summary>
    IReadOnlyList<MultiOption>? Options(Run run, int serial);
}

/// <summary>
/// Sim side of multiselect pages: the domains, the draft inputs and the cleanup of closed pages. A selection is a shared draft:
/// every change is an MP input, so every player sees the same draft, and the submit decides through the shared choice system.
/// </summary>
public static class MultiChoices
{
    static readonly Dictionary<string, IMultiChoiceDomain> Domains = new();

    public static void Register(IMultiChoiceDomain domain) => Domains.Add(domain.Name, domain);

    public static IMultiChoiceDomain Domain(string name) =>
        Domains.TryGetValue(name, out var d) ? d : throw new InvalidOperationException($"no multiselect domain '{name}'");

    /// <summary>The draft of a page, with each value kept inside the option range that holds now. Empty when nobody selected anything.</summary>
    public static IReadOnlyList<Pick> Draft(Run run, string domain, int serial)
    {
        var options = Domain(domain).Options(run, serial);
        var draft = run.State.MultiChoice.Find(domain, serial);
        if (options is null || draft is null) return [];
        var picks = new List<Pick>();
        foreach (var p in draft.Picks)
            if (options.FirstOrDefault(o => o.Id == p.Option) is { Step: > 0 } o)
                picks.Add(new Pick { Option = p.Option, Value = MultiChoiceModel.Snap(p.Value, o.Min, o.Max, o.Step) });
        return picks;
    }

    /// <summary>Clears the draft of a page, for example after its submit.</summary>
    public static void Clear(Run run, string domain, int serial) =>
        run.State.MultiChoice.Drafts.RemoveAll(d => d.Domain == domain && d.Serial == serial);

    internal static void Execute(Run run, int playerIndex, MultiChoiceInput input)
    {
        var options = Domain(input.Domain).Options(run, input.Serial);
        if (options is null) { Log.Info($"multiselect ignored: {input.Domain} {input.Serial} is not open"); return; }
        if (options.FirstOrDefault(o => o.Id == input.Option) is not { Step: > 0 } option)
        {
            Log.Info($"multiselect ignored: {input.Domain} {input.Serial} has no option {input.Option}");
            return;
        }
        var state = run.State.MultiChoice;
        var draft = state.Find(input.Domain, input.Serial);
        if (draft is null) state.Drafts.Add(draft = new Draft { Domain = input.Domain, Serial = input.Serial });
        draft.Picks.RemoveAll(p => p.Option == input.Option);
        if (input.Selected)
            draft.Picks.Add(new Pick { Option = input.Option, Value = MultiChoiceModel.Snap(input.Value, option.Min, option.Max, option.Step) });
        draft.Picks.Sort((a, b) => a.Option.CompareTo(b.Option));
        Log.Info($"multiselect: player {playerIndex} {(input.Selected ? "selects" : "clears")} option {input.Option} = {input.Value} on {input.Domain} {input.Serial}");
    }

    /// <summary>Sim tick: drops the drafts of closed pages.</summary>
    internal static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("multiselect tick", () =>
    {
        var drafts = run.State.MultiChoice.Drafts;
        if (drafts.Count > 0) drafts.RemoveAll(d => Domain(d.Domain).Options(run, d.Serial) is null);
    });
}

/// <summary>A change of a multiselect draft: select an option with a value, or clear it. Runs on every machine at the same tick.</summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoMultiChoice")]
public sealed class MultiChoiceInput : MPInput
{
    [Serialize] public string Domain { get; private set; }
    [Serialize] public int Serial { get; private set; }
    [Serialize] public int Option { get; private set; }
    [Serialize] public bool Selected { get; private set; }
    [Serialize] public int Value { get; private set; }

    public MultiChoiceInput(string domain, int serial, int option, bool selected, int value)
    {
        Domain = domain;
        Serial = serial;
        Option = option;
        Selected = selected;
        Value = value;
    }

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false)
    {
        if (Run.Current is not { } run || run.Game != game) return;
        Errors.Guard("multiselect input", () => MultiChoices.Execute(run, playerIndex, this));
    }
}

[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.AllowMPInput))]
static class AllowMultiChoicePatch
{
    static void Postfix(MPInput mpInput, ref bool __result)
    {
        if (mpInput is MultiChoiceInput) __result = true;
    }
}

/// <summary>Entry point of the MultiChoice area. Mod.Boot calls Register; areas register their domains in their own Register.</summary>
public static class MultiChoiceArea
{
    public static void Register() => Run.Hooks.Tick += MultiChoices.OnTick;
}

using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Halfling.Gui.Components.Text;
using Halfling.Serialization;
using HotCargo.Events;

namespace HotCargo.Chains;

/// <summary>
/// Rules type "HotCargoChainStage": the chain mark on a stage mission. It gives nothing. It fills the string arguments of the
/// HotCargoChain metatype (comms name and description) and adds the gold "story chain" line to the reward list.
/// ChainRunner adds it to every stage mission when the mission spawns. The texts are copies, so the save keeps them with the mission.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoChainStage")]
[TrackMultipleReferences]
internal sealed class ChainStageReward : Reward
{
    [Serialize] public string ChainTitle { get; private set; } = "";
    [Serialize] public string StageTitle { get; private set; } = "";
    [Serialize] public string Brief { get; private set; } = "";
    [Serialize] public string Story { get; private set; } = "";
    /// <summary>1 for the first stage.</summary>
    [Serialize] public int Part { get; private set; }

    public ChainStageReward(string chainTitle, string stageTitle, string brief, string story, int part)
    {
        ChainTitle = chainTitle;
        StageTitle = stageTitle;
        Brief = brief;
        Story = story;
        Part = part;
    }

    ChainStageReward() { }

    /// <summary>"[Chain]" in the event heading color: the mark that sets chain stages apart from other offers.</summary>
    public static string Tag => EventText.Heading.FormatWithXml(Strings.GetText("HotCargoChains/Tag"));

    public override void GetStringArgs(Dictionary<string, object?> args, GameRoot game)
    {
        args["chain_tag"] = Tag;
        args["chain_name"] = ChainTitle;
        args["chain_stage"] = StageTitle;
        args["chain_brief"] = Brief;
        args["chain_story"] = Story.Length > 0 ? $"<gray>{Strings.FormatText("HotCargoChains/SoFarFmt", Story)}</gray>\n\n" : "";
    }

    public override void GiveReward(GameRoot game) { }

    public override string? GetRewardText(float lerpT, GameRoot game, Dictionary<int, int>? implicitFame = null) => null;

    public override int GetRewardSortValue(SortValueType valueType, GameRoot game, int implicitFame = 0) => 0;

    public override void GetDisplayLines(List<(ITextProvider Text, ITextProvider? Tooltip)> lines, GameRoot game, Dictionary<int, int>? implicitFame = null) =>
        lines.Add((new SimpleTextProvider(EventText.Heading.FormatWithXml(Strings.FormatText("HotCargoChains/PartFmt", Part)), null),
            new SimpleTextProvider(Story.Length > 0 ? Strings.FormatText("HotCargoChains/SoFarFmt", Story) : Strings.GetText("HotCargoChains/FirstPart"), null)));

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        iHash.AddHash(Part);
    }

    public override int GetTypeIntegrityHash() => 0x4843_4348; // "HCCH"
}

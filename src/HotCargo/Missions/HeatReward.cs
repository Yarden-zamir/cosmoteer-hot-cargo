using Cosmoteer.Data;
using Cosmoteer.Factions;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Generators.Simulation;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Modes.Career.Missions.Rewards;
using Halfling;
using Halfling.Geometry;
using Halfling.Gui.Components.Text;
using Halfling.PortableHashing;
using Halfling.Random;
using Halfling.Serialization;
using HotCargo.Core;

namespace HotCargo.Missions;

/// <summary>
/// Rules type "HotCargoEscortCover": a Convoy Duty reward that changes heat by MissionTunables.EscortHeatChange (spec 36).
/// The amount is read when the offer spawns, so the offer shows the same number that the reward gives.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoEscortCover")]
internal sealed class HeatReward : Reward
{
    [ReflectiveSerialization(Explicit = true)]
    [SerialDerivedType(TypeName = "HotCargoEscortCover")]
    [TrackMultipleReferences]
    [ReflectivePortableHashing(Explicit = true)]
    public sealed class Spawner : RewardSpawner
    {
        public override Reward? CreateReward(Mission mission, Rand rand, SpawnedObjectCollection objects, Vector2 loc, Range<int>? tierRange, ID<FactionRules>? faction)
        {
            float heat = MissionArea.TunablesFor(mission.Game).EscortHeatChange;
            return heat == 0f ? null : new HeatReward(heat);
        }
    }

    [Serialize] public float Heat { get; private set; }

    public HeatReward(float heat) => Heat = heat;

    /// <summary>Runs in the sim when the mission completes, so it is deterministic.</summary>
    public override void GiveReward(GameRoot game)
    {
        if (Run.Current is not { } run || run.Game != game || game.Sim is not { } sim || !run.PressureActive) return;
        run.AddHeat(Heat, sim);
        Log.Info($"escort cover: heat {Heat:+0;-0} -> {run.State.Heat:0.0}");
    }

    string Text() => (Heat < 0 ? WidgetRules.Instance.GoodTextColor : WidgetRules.Instance.BadTextColor)
        .FormatWithXml(Strings.KeyStringFmt("HotCargoMissions/EscortCoverFmt", Heat.ToString("+0;-0")).Text);

    public override string? GetRewardText(float lerpT, GameRoot game, Dictionary<int, int>? implicitFame = null) => Text();

    public override int GetRewardSortValue(SortValueType valueType, GameRoot game, int implicitFame = 0) => int.MinValue;

    public override void GetDisplayLines(List<(ITextProvider Text, ITextProvider? Tooltip)> lines, GameRoot game, Dictionary<int, int>? implicitFame = null) =>
        lines.Add((new SimpleTextProvider(Text(), null), Strings.KeyString("HotCargoMissions/EscortCoverTip")));

    public override void GetIntegrityHash(IntegrityHash iHash)
    {
        base.GetIntegrityHash(iHash);
        iHash.AddHash((int)(Heat * 100));
    }

    public override int GetTypeIntegrityHash() => 0x4843_4843; // "HCHC"
}

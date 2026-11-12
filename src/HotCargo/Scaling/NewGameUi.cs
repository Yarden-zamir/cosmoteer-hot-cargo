using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Label = Halfling.Gui.Label;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Modes;
using Cosmoteer.Modes.Career;
using Halfling;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.IO;
using Halfling.Network;
using Halfling.Network.Rpc;
using Halfling.Serialization;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Scaling;

/// <summary>
/// A career init with the enemy scaling setting (spec 330). The multiplayer launch sends the host's init to every client,
/// so every machine generates the first system with the host's value. The run state takes the value at run start, and
/// <see cref="ToPlain"/> then puts a vanilla init back in the game, so saves stay loadable without the mod.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoCareer")]
internal sealed class HotCargoCareerInit : CareerInit
{
    [Serialize] public float EnemyScale;

    public HotCargoCareerInit(List<MPPlayerInit>? mpPlayers, int mapSizeIndex, int combatDifficultyIndex, int econDifficultyIndex,
        int crewScarcityIndex, bool disableCrewConstruction, bool disableExploration, bool disableSalvaging, bool disableNebulas,
        bool enableIronmanMode, AbsolutePath? ironmanSaveName, ulong randSeed, (string Name, byte[] Data)? starterShip, float enemyScale)
        : base(mpPlayers, mapSizeIndex, combatDifficultyIndex, econDifficultyIndex, crewScarcityIndex, disableCrewConstruction,
            disableExploration, disableSalvaging, disableNebulas, enableIronmanMode, ironmanSaveName, randSeed, starterShip)
    {
        ScalingModel.Snap(enemyScale);
        EnemyScale = enemyScale;
    }

    /// <summary>A vanilla CareerInit with every field of this one, the ones that a game update adds too.</summary>
    public CareerInit ToPlain()
    {
        var plain = (CareerInit)RuntimeHelpers.GetUninitializedObject(typeof(CareerInit));
        for (Type? t = typeof(CareerInit); t is not null && t != typeof(object); t = t.BaseType)
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                f.SetValue(plain, f.GetValue(this));
        return plain;
    }
}

/// <summary>
/// The "Enemy ships per extra player" slider on the new career screens (spec 330). Single player and the multiplayer host share
/// one saved value. Only the host's value counts in multiplayer; clients see it in a slider they cannot move.
/// </summary>
[HarmonyPatch]
static class NewGameUi
{
    internal const string PreferenceKey = "HotCargo.EnemyScaleTenths";
    const string SyncName = "HotCargoEnemyScale";
    /// <summary>The multiplayer setting rows sort by number: econ difficulty is 210, map size 300.</summary>
    const int MpSortOrder = 215;

    /// <summary>The last value that the player chose, as tenths so the preference stays an exact int like the vanilla ones.</summary>
    internal static float SavedSetting() =>
        App.Preferences.ContainsKey(PreferenceKey)
            ? Math.Clamp(App.Preferences.Get<int>(PreferenceKey) / 10f, ScalingModel.MinSetting, ScalingModel.MaxSetting)
            : ScalingModel.DefaultSetting;

    static int Tenths(float setting) => (int)Math.Round(setting * 10f);

    /// <summary>Both new career screens create their init here, through NewInitPatch.</summary>
    internal static CareerInit NewInit(List<MPPlayerInit>? mpPlayers, int mapSizeIndex, int combatDifficultyIndex, int econDifficultyIndex,
        int crewScarcityIndex, bool disableCrewConstruction, bool disableExploration, bool disableSalvaging, bool disableNebulas,
        bool enableIronmanMode, AbsolutePath? ironmanSaveName, ulong randSeed, (string Name, byte[] Data)? starterShip) =>
        Activation.IsModEnabled()
            ? new HotCargoCareerInit(mpPlayers, mapSizeIndex, combatDifficultyIndex, econDifficultyIndex, crewScarcityIndex, disableCrewConstruction,
                disableExploration, disableSalvaging, disableNebulas, enableIronmanMode, ironmanSaveName, randSeed, starterShip, SavedSetting())
            : new CareerInit(mpPlayers, mapSizeIndex, combatDifficultyIndex, econDifficultyIndex, crewScarcityIndex, disableCrewConstruction,
                disableExploration, disableSalvaging, disableNebulas, enableIronmanMode, ironmanSaveName, randSeed, starterShip);

    /// <summary>A setting row like the game's own: the label, the slider and its value.</summary>
    static (LayoutBox Row, ScrollBar Slider) SliderRow(float labelPercent)
    {
        var tip = Strings.KeyString("HotCargoScaling/Tip");
        var row = new LayoutBox { Height = 32f };
        row.NineSlice.Flags = NineSliceFlags.None;
        row.Children.LayoutAlgorithm = LayoutAlgorithms.StretchLeftToRight;
        var label = new Label { PercentileWidth = labelPercent, TextProvider = Strings.KeyString("HotCargoScaling/Label") };
        label.ToolTips.TextProvider = tip;
        row.AddChild(label);
        const float valuePercent = 12f;
        var slider = new ScrollBar(WidgetRules.Instance.HorizontalSlider)
        {
            PercentileWidth = 100f - labelPercent - valuePercent,
            MinValue = ScalingModel.MinSetting,
            MaxValue = ScalingModel.MaxSetting,
            Interval = ScalingModel.SettingStep,
            IncrementAmount = ScalingModel.SettingStep,
            PageSize = 0.5f,
        };
        slider.ToolTips.TextProvider = tip;
        row.AddChild(slider);
        var value = new Label { PercentileWidth = valuePercent };
        value.TextRenderer.HAlignment = HAlignment.Center;
        value.ToolTips.TextProvider = tip;
        row.AddChild(value);
        slider.ValueChanged += (_, _) => value.Text = Strings.FormatText("HotCargoScaling/ValueFmt", slider.Value);
        slider.Value = SavedSetting();
        value.Text = Strings.FormatText("HotCargoScaling/ValueFmt", slider.Value);
        return (row, slider);
    }

    /// <summary>The single-player row goes under the difficulty lists, above the Advanced button.</summary>
    [HarmonyPostfix, HarmonyPatch(typeof(CareerSPModeConfigGui), MethodType.Constructor)]
    static void SinglePlayer(CareerSPModeConfigGui __instance) => Errors.Guard("enemy scaling slider", () =>
    {
        if (!Activation.IsModEnabled()) return;
        var (row, slider) = SliderRow(45f);
        slider.ValueChanged += (_, _) => App.Preferences.Set(PreferenceKey, Tenths(slider.Value));
        // The Advanced button is the first button. A game update without it only moves the row to the end.
        int advanced = __instance.Children.ToList().FindIndex(w => w is Button);
        if (advanced < 0) __instance.AddChild(row);
        else __instance.Children.Insert(advanced, row);
    });

    [HarmonyPostfix, HarmonyPatch(typeof(CareerMPModeConfigGui), MethodType.Constructor, typeof(MPModeConfigGui.IGameInfo))]
    static void Multiplayer(CareerMPModeConfigGui __instance, MPModeConfigGui.IGameInfo gameInfo) => Errors.Guard("enemy scaling slider", () =>
    {
        if (gameInfo.IsPrototype || !Activation.IsModEnabled()) return;
        var (row, slider) = SliderRow(50f);
        __instance.AddInSortedOrder(__instance, row, MpSortOrder);
        RpcActionInvoker<int> send = gameInfo.RpcManager.RegisterRpc<int>(
            tenths => slider.Value = tenths / 10f, SyncName, TransmitMode.Reliable, 0, RpcAccessibility.Server);
        __instance._onDisposing += () => gameInfo.RpcManager.UnregisterRpc(send);
        if (!gameInfo.IsHost)
        {
            slider.SelfInputActive = false;
            return;
        }
        send(Tenths(slider.Value), RpcReceivers.Clients);
        __instance._onSendingInitGameState += player => send(Tenths(slider.Value), RpcReceivers.Single(player));
        slider.ValueChanged += (_, _) =>
        {
            App.Preferences.Set(PreferenceKey, Tenths(slider.Value));
            send(Tenths(slider.Value), RpcReceivers.Others);
            // Like a vanilla setting change: players say ready again, so nobody starts on a value they did not see.
            __instance.InvokeReadySettingChanged();
        };
    });
}

/// <summary>Both new career screens build their CareerInit through NewGameUi.NewInit, so it carries the slider value.</summary>
[HarmonyPatch]
static class NewInitPatch
{
    static IEnumerable<MethodBase> TargetMethods() =>
    [
        AccessTools.Method(typeof(CareerSPModeConfigGui), nameof(CareerSPModeConfigGui.CreateGame)),
        AccessTools.Method(typeof(CareerMPModeConfigGui), nameof(CareerMPModeConfigGui.CreateMPGameInit)),
    ];

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var ctor = AccessTools.GetDeclaredConstructors(typeof(CareerInit)).Single();
        var factory = AccessTools.Method(typeof(NewGameUi), nameof(NewGameUi.NewInit));
        int replaced = 0;
        var result = instructions.Select(ci =>
        {
            if (ci.opcode != OpCodes.Newobj || !Equals(ci.operand, ctor)) return ci;
            replaced++;
            return new CodeInstruction(OpCodes.Call, factory).MoveLabelsFrom(ci);
        }).ToList();
        if (replaced != 1)
            Errors.Report($"enemy scaling {original.DeclaringType?.Name}", $"found {replaced} CareerInit constructions, expected 1; new careers use no enemy scaling");
        return result;
    }
}

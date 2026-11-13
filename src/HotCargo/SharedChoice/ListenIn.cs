using System.Runtime.CompilerServices;
using Cosmoteer;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Gui;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Comms;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.SharedChoice;

/// <summary>
/// Co-op listen-in (spec 1131 to 1138). UI only: no sim state, no MP input except the votes of the shared choice panel.
/// The hails come from the vanilla sim set CareerSimModeManager.OpenComms, which MPOpenCommsInput and MPCloseCommsInput keep the
/// same on every machine. A listen window is a vanilla CommWindow with the speaker's ship as caller, in listen mode: only the
/// dialogue tab, no open, close or pause inputs, and it closes ListenLingerSeconds after the speaker's channel closes.
/// Limit: players that share ships share a player index, so they cannot listen to each other. Revisit if shared ships get common.
/// </summary>
public static class ListenIn
{
    sealed class Listen(Ship caller, Ship callee, string speaker)
    {
        public readonly Ship Caller = caller;
        public readonly Ship Callee = callee;
        public readonly string Speaker = speaker;
        public double? ClosedAt;
    }

    static readonly ConditionalWeakTable<CommWindow, Listen> Windows = new();
    /// <summary>Set while a listen window builds its tabs: the ship AI offers no tab but the dialogue (UI thread only).</summary>
    [ThreadStatic] static bool _dialogueOnly;

    static LayoutBox? _bar;
    static Cosmoteer.Game.GameRoot? _barGame;
    static List<OpenComm<Ship, ObjectID>> _shown = [];
    const float BarWidth = 440f, ButtonHeight = 40f, Gap = 8f;

    /// <summary>The speaker's name when the window is a listen window, else null.</summary>
    public static string? SpeakerOf(CommWindow? window) => window is not null && Windows.TryGetValue(window, out var l) ? l.Speaker : null;

    /// <summary>The names of the players with this player index, joined. Players that share ships share an index.</summary>
    static string PlayerName(Run run, int playerIndex)
    {
        var names = run.Game.HumanPlayersByID.Where(kv => kv.Value == playerIndex).Select(kv => kv.Key).Order()
            .Select(id => SharedChoices.PlayerName(run, id)).ToList();
        return names.Count > 0 ? string.Join(", ", names) : SharedChoices.Text("SomePlayer");
    }

    /// <summary>The hails that the local player can listen to now, in a stable order. Empty in single player.</summary>
    internal static List<OpenComm<Ship, ObjectID>> Hails(Run run)
    {
        if (!run.IsMultiplayer || run.Game.Sim is not { Mode: CareerSimModeManager mode }) return [];
        var comms = mode.OpenComms.SelectMany(kv => kv.Value.Select(caller => new OpenComm<Ship, ObjectID>(
            caller, kv.Key, caller.UniqueID, kv.Key.UniqueID, caller.Metadata.PlayerIndex,
            caller.IsHumanPlayerShip && kv.Key.AI is { } ai && ai.WillProvideDialogue(caller))));
        return ListenInModel.Hails(comms, run.Game.LocalPlayerIndex);
    }

    static IEnumerable<(CommWindow Window, Listen Listen)> Open() =>
        Windows.Where(kv => kv.Key.Parent is not null).Select(kv => (kv.Key, kv.Value));

    static CommWindow? WindowOf(Ship caller, Ship callee) =>
        Open().FirstOrDefault(o => o.Listen.Caller == caller && o.Listen.Callee == callee).Window;

    /// <summary>The number of listen buttons that show now. For autotests.</summary>
    internal static int ShownCount => _bar is { SelfActive: true } ? _shown.Count : 0;

    /// <summary>The open listen windows. For autotests.</summary>
    internal static IEnumerable<CommWindow> OpenWindows() => Open().Select(o => o.Window);

    /// <summary>Opens the listen window of the n-th shown hail, or closes it when it is open (the button toggles). Also for autotests.</summary>
    internal static void Toggle(Run run, int index)
    {
        if (index < 0 || index >= _shown.Count) throw new InvalidOperationException($"no listen button {index + 1}; {_shown.Count} shown");
        var hail = _shown[index];
        if (WindowOf(hail.Caller, hail.Callee) is { } open) { open.Unparent(); return; }
        string speaker = PlayerName(run, hail.CallerPlayer);
        var window = new CommWindow(run.Game, hail.Caller, hail.Callee);
        Windows.Add(window, new Listen(hail.Caller, hail.Callee, speaker));
        window.TitleText = SharedChoices.Format("ListenTitleFmt", speaker, hail.Callee.Metadata.PlainShipName);
        run.Game.Gui.FloatingWindows.AddChild(window);
        Log.Info($"listen-in: player {run.Game.LocalPlayerIndex} listens to {speaker} and {hail.Callee.Metadata.PlainShipName}");
    }

    /// <summary>Each UI frame: the list of listen buttons above the comms button.</summary>
    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.Update))]
    static class FramePatch
    {
        static void Postfix(CareerGameModeManager __instance)
        {
            if (Run.Current is not { } run || run.Mode != __instance) return;
            Errors.Guard("listen-in list", () => UpdateBar(run, __instance));
        }
    }

    static void UpdateBar(Run run, CareerGameModeManager mode)
    {
        var game = run.Game;
        if (_bar is null || _barGame != game)
        {
            _bar = new LayoutBox { AnchorRect = AnchorPresets.BottomLeft, Width = BarWidth, InputBlockMode = InputBlockMode.Auto };
            _bar.NineSlice.Flags = NineSliceFlags.None;
            _bar.Children.LayoutAlgorithm = LayoutAlgorithms.StretchTopToBottom;
            _bar.Children.WidgetPadding = new Vector2(Gap);
            game.Gui.RootWidget.AddChild(_bar);
            _barGame = game;
            _shown = [];
        }
        var hails = Hails(run);
        if (hails.Count != _shown.Count)
            Log.Info($"listen-in: player {game.LocalPlayerIndex} sees {hails.Count} hail(s): " +
                     string.Join(", ", hails.Select(h => $"player {h.CallerPlayer} with {h.Callee.Metadata.PlainShipName}")));
        _shown = hails;
        _bar.SelfActive = _shown.Count > 0 && game.MapDisplayFactor < 1f;
        if (!_bar.SelfActive) return;
        while (_bar.Children.Count < _shown.Count)
        {
            int index = _bar.Children.Count;
            var b = new ImageButton();
            b.CopySettingsFrom(WidgetRules.Instance.FlatButton);
            b.Height = ButtonHeight;
            foreach (var r in new[] { b.StateNormalTextRenderer, b.StateHighlightedTextRenderer, b.StatePressedTextRenderer, b.StateDisabledTextRenderer })
            {
                r.FontSize = 16;
                r.HAlignment = HAlignment.Left;
                r.OversizeMode = OversizeMode.Ellipses;
                r.Padding = new Borders(10f, 0f, 10f, 0f);
            }
            b.Clicked += (_, _) => Errors.Guard("listen-in click", () => Toggle(run, index));
            _bar.Children.Add(b);
        }
        while (_bar.Children.Count > _shown.Count) _bar.Children.RemoveAt(_bar.Children.Count - 1);
        for (int i = 0; i < _shown.Count; i++)
        {
            var h = _shown[i];
            bool open = WindowOf(h.Caller, h.Callee) is not null;
            ((ImageButton)_bar.Children[i]).Text = SharedChoices.Format(open ? "ListenStopFmt" : "ListenInFmt", PlayerName(run, h.CallerPlayer), h.Callee.Metadata.PlainShipName);
        }
        _bar.Height = _shown.Count * (ButtonHeight + Gap);
        // Above the comms button, or above the comms box while it is open. Without the button (no ship selected): above the tool row.
        var root = game.Gui.RootWidget.ScreenRect;
        var below = mode.CommsToolbox is { SelfActive: true } box ? box.ScreenRect : mode._commsBtn is { SelfActive: true } btn ? btn.ScreenRect : (Rect?)null;
        _bar.X = below is { } r1 ? r1.X - root.X : 10f;
        _bar.Y = below is { } r2 ? r2.Y - root.Bottom - Gap - _bar.Height : -80f - _bar.Height;
    }

    [HarmonyPatch(typeof(CommWindow), nameof(CommWindow.OnParented))]
    static class ParentedPatch
    {
        /// <summary>Listen mode: the vanilla placement without the open comms input and without the open window record.</summary>
        static bool Prefix(CommWindow __instance)
        {
            if (!Windows.TryGetValue(__instance, out _)) return true;
            var w = __instance;
            w.ScreenRect = Rect.CenteredOn(w.Parent.RectProvider.ScreenRect.Size / 2f, w.Size);
            w.BoundsProvider = w.Parent.RectProvider;
            w.Game.MapShown += w.OnCloseClicked;
            return false;
        }
    }

    [HarmonyPatch(typeof(CommWindow), nameof(CommWindow.OnUnparenting))]
    static class UnparentingPatch
    {
        /// <summary>Listen mode: no close comms input. That input would close the speaker's channel.</summary>
        static bool Prefix(CommWindow __instance)
        {
            if (!Windows.TryGetValue(__instance, out _)) return true;
            __instance.BoundsProvider = null;
            __instance.Game.MapShown -= __instance.OnCloseClicked;
            return false;
        }
    }

    [HarmonyPatch(typeof(CommWindow), nameof(CommWindow.OnUpdatingUIState))]
    static class FrameWindowPatch
    {
        static bool Prefix(CommWindow __instance)
        {
            if (!Windows.TryGetValue(__instance, out var l)) return true;
            Errors.Guard("listen-in window", () => UpdateWindow(__instance, l));
            return false;
        }
    }

    /// <summary>Listen mode frame: follows the speaker's channel, and builds only the dialogue tab.</summary>
    static void UpdateWindow(CommWindow w, Listen l)
    {
        if (Run.Current is not { } run || !l.Caller.ExistsInSim(w.Sim) || !l.Callee.ExistsInSim(w.Sim)) { w.SelfActive = false; return; }
        bool speakerOpen = w.SimMode.OpenComms.TryGetValue(l.Callee, out var callers) && callers.Contains(l.Caller);
        if (!speakerOpen && l.ClosedAt is null)
        {
            l.ClosedAt = App.Clock.Time.Seconds;
            w.TitleText = SharedChoices.Format("ListenClosedFmt", l.Speaker);
        }
        if (speakerOpen) l.ClosedAt = null;
        if (ListenInModel.CloseNow(l.ClosedAt, App.Clock.Time.Seconds, run.T.SharedChoice.ListenLingerSeconds)) { w.SelfActive = false; return; }
        _dialogueOnly = true;
        try { if (!w.UpdateTabs()) w.SelfActive = false; }
        finally { _dialogueOnly = false; }
    }

    /// <summary>Runs one frame of every open listen window and its dialogue tab. For autotests (headless games may skip widget frames).</summary>
    internal static void Frame()
    {
        foreach (var (w, l) in Open().ToList())
        {
            UpdateWindow(w, l);
            foreach (var tab in Ui.CommsHail.TabsOf(w)) tab.OnUpdatingUIState(null, EventArgs.Empty);
        }
    }

    [HarmonyPatch]
    static class OtherTabsPatch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
        [
            AccessTools.Method(typeof(ShipAI), nameof(ShipAI.WillTradeWith)),
            AccessTools.Method(typeof(ShipAI), nameof(ShipAI.WillProvideCrew)),
            AccessTools.Method(typeof(ShipAI), nameof(ShipAI.WillProvideMissions)),
            AccessTools.Method(typeof(ShipAI), nameof(ShipAI.WillProvideTechs)),
        ];

        static void Postfix(ref bool __result)
        {
            if (_dialogueOnly) __result = false;
        }
    }

    [HarmonyPatch(typeof(CommDialogueTab), MethodType.Constructor, typeof(ScrollBox), typeof(Cosmoteer.Game.GameRoot), typeof(Ship), typeof(Ship), typeof(CommWindow))]
    static class NoPausePatch
    {
        static bool _restore;

        /// <summary>A listener never pauses the game: the vanilla tab pauses on a dialogue with an enemy (a local setting).</summary>
        static void Prefix(CommWindow window)
        {
            _restore = false;
            if (!Windows.TryGetValue(window, out _) || !Settings.PauseOnOpenDialogueWithEnemy) return;
            Settings.PauseOnOpenDialogueWithEnemy = false;
            _restore = true;
        }

        static void Finalizer()
        {
            if (_restore) Settings.PauseOnOpenDialogueWithEnemy = true;
            _restore = false;
        }
    }
}

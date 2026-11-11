using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Localization;
using Halfling;
using Halfling.Events;
using Halfling.Gui.Components.Text;
using HotCargo.Core;

namespace HotCargo.Ux;

/// <summary>Small shared helpers for the UX area: switches, strings, the chat log.</summary>
public static class UxKit
{
    static readonly UxTunables Defaults = new();
    static bool _modEnabled;
    static double _nextEnabledCheck = double.MinValue;

    /// <summary>
    /// The UX switches in effect, or null when Hot Cargo is not enabled (spec 63: the game stays vanilla).
    /// Limit: outside a Hot Cargo run (title screen, creative mode) the switches are the defaults, not tunables.rules.
    /// Revisit if a player needs to turn a vanilla change off outside career mode.
    /// </summary>
    public static UxTunables? Switches
    {
        get
        {
            if (Run.Current is { } run) return run.T.Ux;
            double now = App.Clock.Time.Seconds;
            if (now >= _nextEnabledCheck)
            {
                _modEnabled = Activation.IsModEnabled();
                _nextEnabledCheck = now + 5;
            }
            return _modEnabled ? Defaults : null;
        }
    }

    public static bool On(Func<UxTunables, bool> pick) => Switches is { } s && pick(s);

    /// <summary>Text from mod/ux/strings.en.rules (section HotCargoUx).</summary>
    public static string Text(string key) => Strings.GetText("HotCargoUx/" + key);

    public static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoUx/" + key, args);

    /// <summary>Adds a line to the chat log (single player and multiplayer). Local only, so it is safe in multiplayer.</summary>
    public static void ChatLine(GameRoot game, string xml)
    {
        if (game.NetManager is IChatBoxProvider p) p.ChatBox.DisplayRawMessage(xml);
    }

    /// <summary>Runs a UI patch body. A failure logs once, shows in the overlay, and the vanilla UI stays.</summary>
    public static void Guard(string what, Action body) => Errors.Guard("ux " + what, body);

    /// <summary>Per-stage text "HotCargoUx/Stage{Name}". A stage that the strings do not know gets a generic line.</summary>
    public static string StageText(string stageName) =>
        Strings.TryGetText("HotCargoUx/Stage" + stageName, out string? text) ? text : Text("StageGeneric");

    /// <summary>Percent text in the game's good, limited or bad text color.</summary>
    public static string PercentXml(float fraction)
    {
        string tag = fraction >= 0.75f ? "good" : fraction >= 0.4f ? "limited_color" : "bad";
        return $"<{tag}>{fraction * 100f:0}%</{tag}>";
    }
}

/// <summary>A tooltip text that is computed each time the game reads it.</summary>
public sealed class LiveText(Func<string> text) : ITextProvider
{
    public string Text => text();

    // The value is computed on read, so there is never a change to announce.
    public event EventHandler<ValueChangedEventArgs<string>>? TextChanged { add { } remove { } }
}

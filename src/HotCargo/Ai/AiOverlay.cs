using Cosmoteer.Localization;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Cosmoteer.Simulation.Overlays;
using Halfling;
using Halfling.Graphics;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Tactics;

namespace HotCargo.Ai;

/// <summary>Text of the AI tells, from mod/tactics/strings.en.rules (section HotCargoAi).</summary>
static class AiText
{
    public static string Text(string key) => Strings.GetText("HotCargoAi/" + key);

    public static string Format(string key, params object?[] args) => Strings.FormatText("HotCargoAi/" + key, args);

    /// <summary>
    /// A log line about the ship, when the local player has it on radar. GUI only: it changes no sim state,
    /// so it is safe in the sim tick on every client.
    /// </summary>
    public static void Chat(Ship ship, string key)
    {
        if (ship.Sim?.Game is not { } game || !ship.IsWithinRadarOfPlayer(game.LocalPlayerIndex)) return;
        string text = StringTools.EscapeForXml(Format(key, ship.Metadata.PlainShipName));
        Ux.UxKit.ChatLine(game, $"<limited_color>{text}</limited_color>");
    }
}

/// <summary>
/// Spec 197 and 368: the player-facing threats. A calling ship gets a red ring that grows to a fixed outer ring as the call
/// completes, and its marker shows the call progress. A fleeing corporate hunter's marker shows its jump countdown. Strategy
/// (personality, traits, roles, cover, regroup) shows nothing. Reads AI module state only, so it is safe in multiplayer.
/// </summary>
static class AiOverlay
{
    static SimRoot? _sim;

    /// <summary>Hooks the overlay of the current sim. Called each frame; it re-hooks after a system jump or a load.</summary>
    internal static void Update(Run run)
    {
        var sim = run.Game.Sim;
        if (sim == _sim) return;
        if (_sim is not null) _sim.Overlays.DrawingCustomOverlays -= OnDrawOverlays;
        _sim = sim;
        sim.Overlays.DrawingCustomOverlays += OnDrawOverlays;
    }

    static void OnDrawOverlays(object? sender, EventArgs e) => Errors.Guard("ai overlay", () =>
    {
        if (Run.Current is not { } run || run.Game.Sim != _sim) return;
        var t = run.T.Tactics;
        var circle = _sim.Rules.SimGui.FocusShipCircle;
        float scale = _sim.Camera.UniformScale;
        float pulse = 0.65f + 0.35f * MathF.Sin((float)App.Clock.Time.Seconds * 6f);
        foreach (var ship in _sim.Ships)
        {
            if (ship.AI?.GetStrategyModule<CallStrategy>() is not { Calling: true } call) continue;
            float r = ship.WorldBoundingRadius;
            circle.Draw(ship.WorldCenter, r * 2.2f, scale, new Color(1f, 0.3f, 0.2f, 0.35f));
            circle.Draw(ship.WorldCenter, r * (1.2f + call.Progress(ship, t)), scale * 1.5f, new Color(1f, 0.3f, 0.2f, pulse));
        }
    });

    /// <summary>Label for the ship marker, or null when no threat shows.</summary>
    static string? Label(Ship ship, TacticsTunables t)
    {
        if (ship.AI is not { } ai) return null;
        if (ai.GetStrategyModule<CallStrategy>() is { Calling: true } call)
            return $"<bad>{AiText.Format("LabelCallingFmt", (int)(call.Progress(ship, t) * 100f))}</bad>";
        if (ai.GetStrategyModule<FleeStrategy>()?.SecondsToJump(ship, t) is { } s)
            return $"<limited_color>{AiText.Format("LabelJumpFmt", (int)Math.Ceiling(s))}</limited_color>";
        return null;
    }

    [HarmonyPatch(typeof(ShipIndicatorSource), nameof(ShipIndicatorSource.GetNonMouseoverText))]
    static class MarkerLabel
    {
        static void Postfix(ShipIndicatorSource __instance, ref string? __result)
        {
            if (Run.Current is not { } run) return;
            try
            {
                if (Label(__instance.Ship, run.T.Tactics) is { } label) __result = __result is null ? label : label + "\n" + __result;
            }
            catch (Exception ex) { Errors.Report("ai label", ex.Message, ex); }
        }
    }
}

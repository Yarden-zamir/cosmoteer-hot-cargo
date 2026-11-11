using Halfling.Serialization;

namespace HotCargo.Ux;

/// <summary>
/// On/off switches for every UI change, so that a bad change can be turned off live (UX.md).
/// Hot Cargo switches act only in a Hot Cargo run. Vanilla switches act in every game while the mod is enabled.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class UxTunables
{
    // ---- Hot Cargo UI
    /// <summary>Heat panel with the stage bar, top left. Off: the plain debug text block.</summary>
    [Serialize(Optional = true)] public bool HeatPanel = true;
    /// <summary>The heat bar pulses when the next stage is this close.</summary>
    [Serialize(Optional = true)] public float StageWarnHeat = 5f;
    /// <summary>Stage strip under the heat panel and a chat log line when a stage starts.</summary>
    [Serialize(Optional = true)] public bool StageAlerts = true;
    /// <summary>Seconds that the stage strip stays after a stage starts.</summary>
    [Serialize(Optional = true)] public float StageAlertSeconds = 10f;
    /// <summary>Short premise and rules panel at the start of a new run.</summary>
    [Serialize(Optional = true)] public bool RunIntro = true;
    /// <summary>Win panel and the Hot Cargo lines on the game-over screen. Off: plain dialogs.</summary>
    [Serialize(Optional = true)] public bool RunEndPanel = true;
    /// <summary>"BUYER" tag on the Buyer station indicator, and the Buyer hint in the galaxy map tooltip.</summary>
    [Serialize(Optional = true)] public bool BuyerMarker = true;
    /// <summary>Pulsing gold tint on Tech Vault parts of your ships.</summary>
    [Serialize(Optional = true)] public bool VaultMarker = true;

    // ---- Vanilla UI
    /// <summary>Hull integrity: ShipsCard box, ship hover text, and off-screen blips of damaged own ships.</summary>
    [Serialize(Optional = true)] public bool ShipIntegrity = true;
    /// <summary>Banner, sound and chat log line when one of your ships is destroyed, becomes derelict or loses control.</summary>
    [Serialize(Optional = true)] public bool ShipLossAlerts = true;
    /// <summary>Fixes the mission HUD distance for other systems (vanilla divides light years by 1000) and adds the jump count.</summary>
    [Serialize(Optional = true)] public bool MissionDistance = true;
    /// <summary>Galaxy map node tooltip: route jumps, visited state, missions there, Hot Cargo heat and sector.</summary>
    [Serialize(Optional = true)] public bool MapNodeDetails = true;
    /// <summary>Fame tooltip: title, next title, crew limit.</summary>
    [Serialize(Optional = true)] public bool FameTooltip = true;
    /// <summary>Game-over screen: a backdrop panel, run statistics and a "waiting for the host" line for clients.</summary>
    [Serialize(Optional = true)] public bool GameOverStats = true;
    /// <summary>A comms window with a station or ship pauses the game. Closing it unpauses, unless you paused by hand.</summary>
    [Serialize(Optional = true)] public bool PauseOnComms = true;
    /// <summary>The same in multiplayer. Off by default, because one player's comms window pauses every player.</summary>
    [Serialize(Optional = true)] public bool PauseOnCommsMultiplayer;
}

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
    /// <summary>Trade tab: what a tech sale does to the heat rate and the final score, and a note at the Buyer.</summary>
    [Serialize(Optional = true)] public bool TechSaleHint = true;

    // ---- Vanilla UI
    /// <summary>Hull integrity: ShipsCard box, ship hover text, and off-screen blips of damaged own ships.</summary>
    [Serialize(Optional = true)] public bool ShipIntegrity = true;
    /// <summary>Banner, sound and chat log line when one of your ships is destroyed, becomes derelict or loses control.</summary>
    [Serialize(Optional = true)] public bool ShipLossAlerts = true;
    /// <summary>Pings the blip of your ship and marks it "under fire" when it loses part health. Polled on the GUI frame.</summary>
    [Serialize(Optional = true)] public bool DamagePings = true;
    /// <summary>Seconds that a blip stays marked "under fire" after the last damage.</summary>
    [Serialize(Optional = true)] public float DamagePingSeconds = 4f;
    /// <summary>A thin hull bar under each ship icon in the ships card.</summary>
    [Serialize(Optional = true)] public bool ShipIconHealth = true;
    /// <summary>Several selected parts: count, average health bar and damaged count in the parts card.</summary>
    [Serialize(Optional = true)] public bool MultiPartInfo = true;
    /// <summary>Low supply warnings for resources that parts burn in operation (UX.md V7): colors, a flash and an alert.</summary>
    [Serialize(Optional = true)] public bool LowSupplyWarnings = true;
    /// <summary>Below this fraction of typed capacity, a supply shows orange. Above it again, a supply can alert again.</summary>
    [Serialize(Optional = true)] public float LowSupplyFraction = 0.25f;
    /// <summary>Below this fraction of typed capacity, a supply shows red and alerts once.</summary>
    [Serialize(Optional = true)] public float CriticalSupplyFraction = 0.1f;
    /// <summary>Service badges (trade, crew, techs, missions, rewards) in the comms list and in the station hover text.</summary>
    [Serialize(Optional = true)] public bool StationBadges = true;
    /// <summary>The galaxy map no longer closes an open comms window. Read when a comms window opens.</summary>
    [Serialize(Optional = true)] public bool CommsStayOnMap = true;
    /// <summary>Trade tab: the price factor of your reputation and the delivery missions of the station.</summary>
    [Serialize(Optional = true)] public bool TradeHints = true;
    /// <summary>Wanted label: which factions, the time left, and "seen" when the timer waits.</summary>
    [Serialize(Optional = true)] public bool WantedTimer = true;
    /// <summary>Keys 1 to 9 pick dialogue options in comms and in Hot Cargo event windows.</summary>
    [Serialize(Optional = true)] public bool DialogueKeys = true;
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

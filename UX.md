# Hot Cargo UX pass

UI and UX findings for vanilla Cosmoteer 0.30.5a and the Hot Cargo mod, the changes, and their status.
Each change has an on/off switch in `Ux` in `tunables.rules` (class `src/HotCargo/Ux/UxTunables.cs`). All switches default to on.
A switch change takes effect on the next frame in single player (live reload).

All changes are GUI-only. No change writes simulation state, so co-op multiplayer stays in lockstep.
A failing UI patch logs one error, shows it in the overlay, and leaves the vanilla UI in place (`UxKit.Guard`).

## How to read the table

- **Impact**: how much a player gains (high, medium, low).
- **Cost**: implementation size and risk (S, M, L).
- **Status**: **done** (in this branch), **later** (not done, with the reason).

## Hot Cargo UI

| ID | Problem | Change | How | Switch | Impact | Cost | Status |
|---|---|---|---|---|---|---|---|
| H1 | The overlay is a plain block of debug text in the top right. It shows numbers but not how close the next stage is. | Heat panel at the top of the left HUD column: sector, a heat bar with a mark for each stage, the current stage, the next stage, the heat rate, the tech count and the price. The bar color goes from cool to hot. It pulses when the next stage is `StageWarnHeat` away. A gray debug line shows only with debug keys in single player. F8 toggles it. | `Ui/HeatPanel.cs`, added to `GameGui.TopLeftHudBox` from `Overlay.Attach` (the existing `CreateModeSpecificGui` postfix). Blurred panel style from `WidgetRules.AddBlur`. | `HeatPanel` | high | M | done |
| H2 | A stage start is easy to miss in combat. | A red strip in the heat panel names the stage and its threat for `StageAlertSeconds`. The bar flashes. A chat log line keeps a record. | `HeatPanel.DetectStageStart`. It compares `RunState.StagesStarted` each frame. A jump or a load sets the baseline again, so a jump does not alert. | `StageAlerts` | high | S | done |
| H3 | The win and loss use plain dialogs. The score and the run numbers are in a text block. | Win: a centered panel "TECH DELIVERED" with the reason and score text, then sector, run time, tech held and tech value. Loss: the game-over screen gets the title "THE CORPORATION RECOVERED THE TECH", the reason and the same run numbers. | `Ui/RunEndUi.cs`, `Ui/CenterPanel.cs`, and the `GameOverGui` constructor postfix in `Ux/CareerInfoPatches.cs`. | `RunEndPanel` | high | S | done |
| H4 | A new player does not know the premise or the rules. | Run intro panel at the start of a new run: the premise and five rules (goal, heat, cooling, cargo, selling). It does not pause the game. Enter or "Fly" closes it. | `RunEndUi.ShowIntro`, set by `Run.Hooks.NewRunStarted`. | `RunIntro` | high | S | done |
| H5 | The Buyer station looks like any other station. | A green "BUYER" tag on its blip, near (physical ship) and far (stasis landmark). The galaxy map tooltip of an unvisited system says "First visit: the Buyer waits here" when the next new system is the last sector. | Postfixes on `ShipIndicatorSource.GetNonMouseoverText` and `StasisLandmark.GetNonMouseoverText` (`Journey.Buyer.Tag`), `CareerGameModeManager.GetMapNodeToolTip`. | `BuyerMarker` | high | S | done; not seen in game yet (screenshot pending) |
| H6 | Tech Vaults look like other parts. A player cannot see at a glance where the cargo is. | A pulsing gold outline on Tech Vault parts of your ships, in the vanilla selection box style. | `Ux/Markers.cs`: a `BatchedPartNineSliceRenderer` with `SimGuiRules.SelectedPartNineSlice`, drawn from `SimOverlayRenderer.DrawingCustomOverlays`. Part ID from `Tech.Vaults.VaultId`. | `VaultMarker` | medium | S | done; not seen in game yet (screenshot pending) |
| H7 | Hunter waves show a big center text (Hunters area, `ShowAreaName`), and the heat panel names the stage too. | Keep both for now: the center text is the alarm, the panel keeps the state. | Hunters area, `HunterSpawner.ShowAlert`. | — | low | S | later: decide after the Hunters merge if one of them goes |
| H8 | A sale of tech shows no effect on heat or score. | Show "heat ×1.05 → ×0.90, score −X" in the trade screen row of the tech resource. | `CommTradeTab` postfix. It needs the Tech resource and the Journey score rule. | — | medium | M | later: depends on Tech and Journey |
| H9 | The score is only text inside `RunState.EndReason`, written by the Journey area. | Store `Score` and `DeliveredUnits` in `RunState`, so that the win panel can show the score as a big number. | Small Core change, set from `Buyer.OnDelivered`. | — | medium | S | later: Core and Journey change, for the integrator |

## Vanilla UI

| ID | Problem | Change | How | Switch | Impact | Cost | Status |
|---|---|---|---|---|---|---|---|
| V1 | The flight HUD never shows how damaged a ship is. Damage shows only as red ghosts on ship icons and as a bar for one selected part. | Hull integrity (health of all parts ÷ health with every part whole, destroyed parts included): a "hull" box first in the ships card info row, a "Hull 63%" line in the hover text of any damaged ship in sight (enemies too), and the same line on the blips of your own damaged ships. Colors: good, limited, bad. | `Ux/ShipIntegrity.cs`: postfixes on the `ShipsCard` constructor and `UpdateForSingleShip`, `SimModeManager.GetShipMouseOverText`, `ShipIndicatorSource.GetNonMouseoverText`. Cached for 0.25 s per ship. | `ShipIntegrity` | high | S | done |
| V2 | A lost ship disappears from the ships card without a word. "No control" shows only for one selected ship. | A red banner, the error sound and a chat log line when one of your ships is destroyed, becomes derelict or loses control. | `Ux/ShipAlerts.cs`: a 4 Hz poll of your ships: a ship that leaves the list in the same sim is destroyed (disposed or no parts) or derelict (junk); `CommandManager.CanExecuteCommands` going false is a loss of control. A new sim (jump, load) resets the poll, so a jump does not alert. | `ShipLossAlerts` | high | S | done |
| V3 | Bug: the mission HUD shows the distance to a mission in another system divided by 1000 (`MissionsHud.MissionWidget.Distance`), so 7.5 L.Y. reads "0.0 L.Y.". The mission log shows the right value. | Show the right value and the jump count: "(7.5 L.Y., 2 jumps)". | Postfix on the `MissionsHud.MissionWidget.Distance` setter. Jumps from `MapNodeManager.FindPathTo(requireJumpable: true)`, cached per route. | `MissionDistance` | medium | S | done |
| V4 | The galaxy map tooltip shows only name, type, tier and factions. It does not say how far a system is or what is there for you. | Add jumps and distance (or "no known route"), "not visited", the count of your missions there. In a Hot Cargo run also: heat when you left, and the sector number of a first visit (H5). | Postfix on `CareerGameModeManager.GetMapNodeToolTip`. | `MapNodeDetails` | medium | S | done |
| V5 | The fame tooltip says only "Fame". Fame controls the crew limit, which the player cannot see. | "Fame 1,234: Title", "Next title: X in 300 fame", "Crew 24 of 30 allowed by your fame". | Postfix on the `PlayerFameLabel` constructor: a live tooltip text (`LiveText`). | `FameTooltip` | medium | S | done |
| V6 | The game-over screen shows no numbers. Clients in co-op see no buttons and no reason. | Career numbers: fame, missions completed, systems visited. Clients get "Waiting for the host to load or quit." | `GameOverGui` constructor postfix. | `GameOverStats` | low | S | done |
| V7 | Resources turn red only below zero. Low ammo or battery is invisible until it runs out. Totals sum all selected ships, which hides one empty ship. | Color a resource orange below 25 % of capacity and red below 10 %; flash on each crossing. | Postfix on the private `PlayerResourcesDisplay.UpdateResource(...)`; it sets `quantityLbl.TextRenderer.Color` each frame. | — | high | S | later: needs a per-resource "low" rule (which resources matter in flight), design first |
| V8 | Off-screen ships take damage without any sign. | Ping the ship blip and flash its ships card icon when it loses parts, throttled. | Postfix on `SimModeManager.OnPartDestroyed`; queue to the GUI thread; `ObjectIndicator.ShowPing`. | — | high | M | later: sim callbacks can run on worker threads, needs care |
| V9 | The ships card with several ships shows icons only. | A thin health bar under each icon (data from V1) and status badges. | Postfix on `ShipIconButton.OnRender`; draw like `PartsCard.OnDrawHealthBar`. | — | medium | M | later |
| V10 | Several selected parts show no health at all (`PartsCard.UpdateForMultipleParts` hides the info box). | "N parts, average health, X damaged". | Patch `PartsCard.UpdateForMultipleParts` and `OnDrawHealthBar`. | — | medium | S | later |
| V11 | Stations hide their services and missions until you hail them. | Badges on the comms list and the station blip: trade, crew, techs, mission count, pending rewards. | Postfixes on `CommsToolbox.CallShipItem.Update` and `ShipIndicatorSource.DrawCustomIcon`; data from `ShipAI.WillTradeWith/WillProvideMissions/...`. | — | high | M | later |
| V12 | Opening a mission location from comms closes the comms window (`CommWindow` subscribes `MapShown` to close). | Keep comms open, or show the route in a tooltip instead. | Postfix on `CommWindow.OnParented`. | — | medium | M | later: needs a play test of the map over comms |
| V13 | The trade tab hides that a sale counts toward the station's delivery mission, and hides the reputation price factor. | "Delivery 40/80 → +4,500" on the row and "Prices ×0.8 (Ally)" in the header. | Postfix on `CommTradeTab` constructor and `OnUpdatingUIState`. | — | medium | M | later |
| V14 | The wanted label shows no faction, no timer, and no "seen" state. | Sub-line: faction, seconds left, "seen: timer paused". | Postfix on `WantedLabel.OnBeforeDraw`; data from `WantedManager`. | — | medium | S | later |
| V15 | Dialogue options have no number hotkeys. | Keys 1 to 9 for options. | Poll keys in `CommDialogueTab.OnUpdatingUIState`. | — | low | S | later |
| V16 | Ship blips of your own fleet in another system: no "under attack" sign. | — | Needs V8. | — | low | M | later |

Already in vanilla, so not changed: camera north (Backspace), idle crew filter (crew roster), crew distance warning, auto slow when an enemy shows (`AutoSlowWhenEnemySpotted`), on-ship part indicators, pinned resources (right-click in the resources box), Sell Surplus and Buy Needed in the trade menu.

## Screenshots

Status: **not taken yet.** The capture code is in this branch, but it is not verified on the PC. The player's slot (main) is the only slot with graphics, and it was in use for a playtest. Run the steps below when the main slot is free.

### How the capture works (no focus, no visible window)

1. The scripts start the main-slot game minimized. Headless test slots have no graphics device (`NullGraphicsManager` throws on a screenshot), so screenshots need the main slot.
2. The D3D11 graphics manager skips all drawing while the window is minimized: `CanRender => !Window.IsMinimized` (HalflingPlatformWDX), read by `Director.DoDraw`.
3. The autotest command `render on` patches that getter to return true (`Ux/Screenshot.cs`). The game then draws each frame into the swap chain back buffer, which keeps its last size. `Present` still skips, because DXGI reports the minimized window as occluded (the window loop checks `Present` with the test flag first). Nothing shows on the screen, and the window never gets focus.
4. `screenshot NAME` waits for the end of a frame, copies the back buffer (`GraphicsManager.GetEndOfFrameScreenShot`) and saves `Mods/hot_cargo/screenshots/NAME.png`. The log line `ux: force render on; window minimized=... size=...` gives the back buffer size.

Risks to check on the first run:

- The back buffer size of a window that starts minimized is not known. If the PNG is tiny or black, the swap chain never got a real size. Then the fallback is to show the window without activation at an off-screen position (`ShowWindow(SW_SHOWNOACTIVATE)` after `SetWindowPlacement` with an off-screen rectangle), and to minimize it again after the capture.
- `AutoPauseOnLostFocus` can pause the game. The script sends `pause off`.

### Steps

Run these on the Mac from the UX worktree, only when the main slot is free.

1. Check that no main-slot game runs. The output must be empty:
   `source scripts/pc.sh; SLOT=main; pc "$PS_PATHS"'Get-SlotProcess'`
2. Deploy to the main slot: `SLOT=main scripts/deploy.sh`
3. Install the script:
   `SLOT=main; pc "$PS_PATHS"'$in = [Console]::In.ReadToEnd(); [IO.File]::WriteAllText((Join-Path $modDir "autotest.txt"), $in)' < tests/autotest/ux-screens.txt`
4. Start the game minimized: `SLOT=main scripts/run.sh`. The script quits the game when it is done (about 2 minutes).
5. Check the log for `ux: screenshot` lines: `SLOT=main scripts/logs.sh 60`
6. Pull the images: `mkdir -p docs/screenshots; pc "$PS_PATHS"'tar.exe -cf - -C (Join-Path $modDir "screenshots") .' | tar -xf - -C docs/screenshots`
7. Before you commit the images, look at each one. Crop anything that shows a user name, a file path or a Steam name.

The script `tests/autotest/ux-screens.txt` takes these pairs (before = switch off, after = switch on):

| Image | Shows |
|---|---|
| `ux-intro` | H4 run intro |
| `ux-heat-before`, `ux-heat-alert`, `ux-heat-close` | H1 old text overlay; heat panel with a stage alert; heat panel close to the next stage (pulse) |
| `ux-hull-before`, `ux-hull` | V1 ships card without and with the hull box (ship at about 70 % integrity) |
| `ux-map` | galaxy map with the heat panel hidden |
| `ux-win` | H3 win panel |
| `ux-loss` | H3 loss on the game-over screen, with V6 career numbers |

## Tests

- `tests/autotest/ux-basic.txt` (headless slot): heat panel on and off, two stage alerts, hull integrity after damage, map open and close, one ship alert when the own ship is destroyed, the loss path through the patched game-over screen. Any failed UI patch logs an ERROR line and fails the test.
- `tests/autotest/ux-screens.txt` (main slot only): the screenshots above.

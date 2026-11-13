# Mission fidelity audit

Playtest 2026-11-13: "We had a delivery mission … we never picked anything up and never gave it. We just found the person and the mission was over."

Rule for every fix:
- If the text says that you carry something, it is a real item in your hold. It takes space, and you can lose it.
- If the text says that you give, take, board, scan or meet, the game has a real action for it: a comms button, a range condition, or a crew or part action. The action has a visible result.
- Use vanilla systems and the mod's own systems first: resources and storage, `DeliverResources`, ship comms pages (spec 767-771), shared choices (section 32), mission panel orders.
- A hand-over can carry one small choice or risk. No new big systems.

Priority: P1 = players see it often and the gap breaks the fiction. P2 = less often, or a smaller gap. P3 = rare, or an accepted abstraction.

## Gaps

| # | Content | Text says | Game does | Priority |
|---|---|---|---|---|
| 1 | Courier Job (`mod/events/spawns.rules`, `HotCargoCourier`) | Fly a sealed case to the contact, then come back. | `GotoShip` (done when a player ship sees the contact), then `ReceiveRewards`. No case, no hand-over. | P1 |
| 2 | Quiet Defector: Extraction (`ChainCarefulTarget`) | Get Sabine Okafor off the transport. The Debrief page has her drink your coffee. | Kill the escorts, keep the transport alive, collect pay. She is never picked up. The transport stays an enemy. | P1 |
| 3 | The Black Box: The Black Box (`ChainBlackBox`) | Get the flight recorder from the wreck. The broker wants it. | Kill the drone, collect pay. Nobody touches the wreck. The Playback page then plays a recorder you never took. | P1 |
| 4 | Prototype Retrieval (`HotCargoPrototype`) | Lift the core out and bring it to the broker. | The core part disappears when a ship is near it. You collect pay at the station without a core. | P2 |
| 5 | Repossession (`HotCargoCapture`) | The bank pays for the ship. | Capture completes the mission. You keep the ship and get the pay. | P2 |
| 6 | Hold the Pass (`HotCargoHoldPass`) | Hold the pass until the convoy is through. | A timer in a ring. No convoy exists. | P2 |
| 7 | The Black Box: Courier Run (`ChainCourierHit`) | Leave the courier whole: its hold has prototype parts. | Same careful-targeting mission as #2. Success adds +1 tech straight into a vault. | P2 |
| 8 | Quiet Defector: Okafor's Depot (`ChainDepot`) | Take the depot apart. | Success adds +1 tech straight into a vault. | P3 |
| 9 | Magpies: Take It Back (`ChainAmbush`) | Rook's ships have your processors in their holds. | Success adds 3 processors through the result row. The wrecks drop their own loot. | P3 |
| 10 | Boarding choices: Abandoned Ship, Derelict Survey Ship, Cold Hulk, Hunter Wreck, The Informant ("lean on him") | Send a team aboard. | A crew count is a need. No crew move. Loot is real resources. The trigger already needs a ship close by. | P3 |
| 11 | Sundive (`HotCargoSundive`) | Salvage the recorder, then get clear. | The objective remembers which ship "holds" it. No item. | P3 |
| 12 | Discreet Bounty (`HotCargoShadyBounty`) | The client says to come alone. | Nothing checks it. | P3 |
| 13 | Event resource gains (`Resources` outcomes) | Cargo drifts to your hull. | Checked: units that do not fit are lost, and a "no storage" result line says so (`EventRunner.ChangeResource`). | P3 |

Checked and true to the text: Convoy Duty, Joint Strike, Drone Siege, Sunside Ore, Salvage Rights, Storm Survey, Claim Jumpers, Listening Post, Raid the Lair, Tow Job, Freighter Under Fire, Rival Crew, bounty hunters, Salvage Race, Reactor Shielding, Clear the Lane, Navy Tender, Quiet Freight, station raids and ambushes, every tech cost (it comes out of a vault), hunter hand-overs (diplomacy moves real tech), the Buyer sale (normal trade), Relic ship.

## Fixes

### 1. Courier Job (P1)
- New resource `yarden_zamir.sealed_case`: 1 per storage slot, normal storage holds it, stations do not trade it. A destroyed storage drops it as a loose crate, like any cargo.
- Accept: the accepting player's nearest ship with a free storage slot gets the case. No room: the accept is refused with a reason, like the contract fee check.
- Hand-over: the contact ship has a comms line "Hand over the sealed case." while a player ship is in comms range and the fleet holds the case. It opens a shared-choice page on the contact (spec 769, 771):
  - Hand it over: the case leaves your hold. The objective completes.
  - Open it first: the case leaves your hold. More pay, and more heat; a heat-weighted chance that the seal was a tracer.
- Fail: the fleet holds no case before the hand-over (the storage was destroyed and nobody picked the crate up), or the time limit runs out.
- Then collect the pay at the station, as now.

### 2. Extraction (P1)
- After the escorts die, hail the transport: "Get Okafor out." Bringing her aboard needs a free bunk and adds 1 crew. A second choice also strips the hold (processors, heat +10).
- The Debrief page then decides: keep her, hand her to the navy (−1 crew) or sell her (−1 crew).
- She dies with the transport: a transport destroyed before the pickup gives the `lost` result.

### 3. The Black Box (P1)
- New resource `yarden_zamir.flight_recorder` (1 per slot). It can serve Sundive (#11) later.
- A junk capsule beside the wreck holds exactly one recorder. Salvage it (vanilla salvage), and crew carry the recorder home.
- Hand it to the station's broker in comms. The navigator keeps a copy, so the Playback page sells or uses the copy.

### 4. Prototype Retrieval (P2)
- The lift puts a Prototype Core item (`yarden_zamir.prototype_core_cargo`) into the nearest player ship with room. No room: the lift waits.
- Hand it to the broker in the station's comms; haggling is the small risk. A lost core fails the mission.

### 5. Repossession (P2)
- After the capture, bring the freighter within 800 of the station and hail the bank. The hand-over waits until no player crew is aboard; the bank then takes the ship.
- A surrendered freighter stays in the system while the job runs, and its crew fly it to the station.

### 6. Hold the Pass (P2)
- Text fix: "hold the pass for N minutes". A real convoy is a bigger change; it only makes sense if the escort AI from Convoy Duty can drive it.

### 7. Courier Run (P2)
- The courier carries 1 tech in a vault. Disabled or surrendered: crew carry the tech over (vanilla resource transfer, spec 16). Destroyed: the vault drops its crate (spec 65), with a chance to burn. No straight +1 tech.

### 8-9. Depot and Take It Back (P3)
- Put the tech or processors into a ship of the enemy group, so the wrecks drop them. Remove the straight gains from the result rows.

### 10. Boarding choices (P3)
- Keep the abstraction. The triggers already need a ship close by, and the loot is real. Revisit when crew boarding gets a sim action.

### 11-13 (P3)
- Sundive: the recorder becomes the item from #3.
- Discreet Bounty: drop "come alone" from the text, or count the players' ships at the trap.
- Event resource gains: test with full storage; drop the surplus as loose cargo if the game does not.

## Status

| # | State |
|---|---|
| 1 | Fixed: `events-courier`, `events-courier-handover`, `events-courier-lost`, co-op step `events.courier` (coop-economy) |
| 2 | Fixed: `chains-extraction` |
| 3 | Fixed: `chains-blackbox` |
| 4 | Fixed: `mechanics-prototype` |
| 5 | Fixed: `mechanics-salvage` |
| 6-12 | Open |
| 13 | Checked, no change |

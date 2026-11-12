# Hot Cargo playtest checklist

Manual checks that the autotests do not cover. Start a new single-player career with the Hot Cargo mod on and debug keys on.
Debug keys are off by default. To turn them on, put this in `Mods\hot_cargo\tunables.rules` (the game reloads it while it runs):

```
Debug
{
	DebugKeys = true
}
```

Keys: F9 / Shift+F9 add or remove 10 heat, F10 starts the next stage, F11 / Shift+F11 add or remove 1 tech.
To see each tactic decision in `hot_cargo_log.txt`, also put `TacticsLog = true` in the `Debug` section.
Mission checks are in [tests/playtest/missions.md](tests/playtest/missions.md).

## Tech

1. Look at the starting ship. Check that a tinted 2×2 Tech Vault replaces one storage bay, and that its 4 tiles show 2 units each (8 units).
2. Start a career with the Model-M starter ship. Check that the vault is outside the hull with a door, and that crew can walk to it.
3. Open the build menu, Storage group. Check that "Tech Vault" is there with its name, description and tinted icon.
4. Dock at a trade station and open the trade window. Check that "Stolen Tech" shows the overlay tech value as its sell price.
5. Sell 1 unit through the trade window. Check that the money rises by the shown price and the overlay tech count drops by 1.
6. In the trade window, check that the station offers the sold unit back at 1.5 × the sell price.
7. Try to move tech into a normal storage bay with the transfer window. Check that the game refuses it.
8. Let an enemy shoot the vault. Check that the stacks shrink as it takes damage, and that repair does not bring them back.
9. Sell the last unit. Check that the loss dialog "The corporation recovered the tech." shows about 3 seconds after the delivery.
10. Press F11 and Shift+F11. Check that the overlay tech count goes up and down by 1.

## Economy and contracts (spec 19, 100-108)

To see sell-outs fast, put `Contracts { InformantShare = 1  InformantChanceMin = 1  InformantChanceMax = 1  InformantDelayMin = 10  InformantDelayMax = 10 }` in `tunables.rules`.

1. Hail a station and open the missions tab. Select offers. Each one shows a "Contract terms" section under the rewards: fee or "No fee", flat heat, trail, rival, informant risk or "Discreet client". Is it readable at a glance? Does it look like the rest of the window?
2. Select a paid offer. The accept button reads "Accept (pay N)", and the rewards show an extra "(paid contract)" money line. Accept it. Check that the money drops by the fee.
3. With less money than a fee, the button reads "Fee N: not enough money" and does nothing.
4. Find an offer with "Rival offer: accepting dismisses ...". Accept it. Check that the named offer is gone and that a chat line says why.
5. Accept 4 or more contracts. Check that the heat panel shows "N contracts open, trail +X/min" and that the heat rate in the status line rises.
6. Wait for a sell-out (see the tunables above). Check that the banner names the client and the contract, that hunters jump in, and that the mission log shows "... sold you out on this contract".
7. Balance: are fees worth paying? Does a 4th contract feel like a real choice? Is the informant risk scary but not run-ending?
8. Tech value: watch the heat panel price over a sector. It goes up with time and heat and never down, also when convoy cover lowers heat. Mid-run, do 2 units buy roughly a ship of the current tier?
9. Systems are smaller: is the travel between stations, bounties and gates noticeably shorter? Do systems look too crowded or too empty?

## Hunters and tactics (spec 30-35, 43-49)

Each hunter uses only the tactics of its personality (spec 361). To see every tactic on every hunter, set `Tactics { PersonalitiesEnabled = false }`.

1. Press F10 four times, a few seconds apart. Each stage shows its message at the top of the screen and plays the wanted alert.
2. Each wave comes in from one side with the FTL jump-in effect. Do the ships appear far enough away (2,000 to 3,000)? Is one side per wave readable?
3. Hunters show the "Corporate" name prefix. Is that enough to tell them apart from pirates and police? If not, a colored name or icon needs C# work.
4. Hunters are enemies of faction ships too (like vanilla ambushes). Do police or stations kill hunters too easily? If yes, say so: hunters can be made neutral to factions with a patch.
5. Hunt stage (tier + 1) and Strike stage (tier + 2) at sector 1: is the Strike stage survivable (spec 35) or a sure loss?
6. Pincer: with two or more hunters, do they split and come from different sides? Does the formation turning every 10 to 20 s look natural or twitchy?
7. Kite: a hunter with longer range holds at a fixed distance and backs off when you close in. Is it readable? Is it annoying? Lower `KiteRangeFraction` makes it hold closer.
8. Disable (Hunt and Strike): do your guns and engines break first? Do the enemy target lines point at them?
9. Fall back: shoot a hunter to about a third of its value. Does it turn and run, then sit still and repair? Does it come back? Is the 45 s limit too long?
10. Bait (Strike): does one small ship dive in alone and then run back to the group? Is it clear enough that chasing it is a trap?
11. Save during a fight with hunters, load, and check that the fight continues the same way.
12. With the Tech area merged: hunters prefer the ship with the Tech Vault (`VaultTargetFactor`). Put the vault on a second ship and check that hunters switch to it.

## Strike waves and enemy AI (spec 190-202)

Switch a behavior off to compare: `Tactics { CallEnabled = false }` for hunters, `Tactics { CallOnVanilla = false }` for pirates and other vanilla AIs (same for `Flee`, `Focus`, `Regroup`).

1. Sector 1, low FTL fuel: raise heat to 100 (F9). Strike sends one ship now and one 45 s later. Can you destroy the first one, pick up or salvage its fuel, and jump before the second one arrives? Is it "by the skin of your teeth", or too easy, or impossible? Tune `Hunters.Stages` Strike `EarlyScale`, `WaveGapSeconds` and `Hunters.MinFuel`.
2. Destroy a hunter's hyperdrive or storage. Do hyperium nuggets drop (about half the fuel)? Does salvaging the wreck give the rest?
3. Last sector: three strike ships of tier + 2, one at a time. Is the last sector a real threat but still survivable?
4. Call for help: a hunter calls when it first sees you while another group waits. Is the red ring and "CALLING FOR HELP n%" label easy to notice in a fight? Is 6 s the right window?
5. Hit a calling ship with a disruptor, an EMP missile or an ion beam. Does the call stop at once, with the "call was cut off" log line? Does that feel like a reward for carrying disruptors?
6. Fight a pirate group (asteroid field pirates or a fleet bounty). When you engage one, does it call the others, and do they come? Is a 6,000 call range too far or too short?
7. Fly into an ion storm or electron nebula and fight there. Are calls blocked as expected? Is it clear to the player why no one called?
8. Flee: damage a pirate or hunter below a quarter of its value. About half of them should break off and run (label FLEEING). A hunter jumps out after 25 s (label shows the countdown). Is the window long enough to chase and kill it? Does a fleeing pirate come back after repairs?
9. Destroy all weapons on an enemy. Does it always run?
10. Focus fire: let one of your ships get badly hurt. Do the enemies switch to it? Is that too punishing (lower `FocusWeakFactor`)?
11. Regroup: a lone weak enemy near stronger friends should turn toward them (label REGROUPING) and attack together. Does it look smart, or like hesitation? Is 25 s too long?
12. Co-op: run a strike with calls and flees for 20 minutes. Check for no desync.

## AI personalities (spec 360-369)

Hover an enemy ship: the mouseover names its personality, its traits and its team role. Switch personalities off to compare: `Tactics { PersonalitiesEnabled = false }` (hunters use every tactic, pirates and faction ships use none). Switch one off by name: `Tactics { PersonalitiesOff = ["Cabal Zealots"] }`.

1. Fight pirates, then a faction that wants you (attack one of its ships). Do the two feel different? Can you name a faction's style after one or two fights?
2. Two ships of one faction: do they behave a little differently (one calls, one does not; one flanks, one brawls)? Or does the jitter make factions blur together (lower `Jitter`)?
3. Heavy ships: a big, slow ship never kites or baits. Does any big ship still dance at long range? Report its name and the mouseover line.
4. Fast, light, long-range ships: do they hold distance and back off? Fast short-range ships: do they flank? Short-range armored ships: do they close in?
5. Cover: fight pirates or Fringe ships in an asteroid field. Does a small ship duck behind a rock (label IN COVER), then come out to shoot? Can you flank it, or does it feel like cheating? Tune `CoverPeekSeconds` and `CoverHideSeconds`.
6. Teams (Monolith, Imperium, hunters): do the team's ships all turn on one of your ships? Does the tank lead, the sniper hang back and the flankers swing wide? Put two of your ships in the fight: does the team stay on one?
7. Do the profile scores match the ships? `Debug { TacticsLog = true }` writes each ship's scores to `hot_cargo_log.txt` ("tactic personality"). If too many ships count as heavy or protected, tune `Tactics { Profile { ... } }`.
8. Co-op: fight a faction team among asteroids for 10 minutes. Check for no desync.

## Run and UX

1. Open the galaxy map at the start. Check that no system near the start shows a much higher danger tier than the systems on the way out (spec 3).
2. Hail a station. Check that the game pauses, and that it runs again when you close the comms window.
3. Pause the game by hand, hail a station and close the window. Check that the game stays paused.
4. Lose a run (sell the last unit). Check that the game-over lines sit in a readable panel and do not overlap.
5. Win a run. Check that the win panel shows the score as a big number, and that the heat panel shows the score after the win.
6. Load a career save from before you turned the mod on. Check that a run starts: a Tech Vault with tech, and the run intro.

## Events (spec 67-78, EVENTS.md)

The autotests cover triggers, conditions, costs, effects, spawns, follow-up pages, the MP input and save and load. Only a person can judge the points below.
To see more events fast, put `Events { ArrivalEventChance = 1  StationEventChance = 1  CooldownSeconds = 20 }` in `tunables.rules`.

1. Start a new career. After about 3 seconds, "Clean Getaway?" opens. Does the window look like the game's comms window (title bar, icon, fonts, flat buttons)? Is it centered and easy to read?
2. Does the game pause while the window is open, and continue after "Continue."?
3. Read the costs on the buttons, for example "(-5,000 credits)". Are they clear? Is the blue "[Sensor Array]" tag readable, and is the grey reason ("Needs a Sensor Array") readable on a disabled button?
4. After a choice, read the result lines (money, heat, crew, resources). Do the colors match the vanilla mission reward lines? Is "Heat -20" in green and "Heat +15" in red?
5. Fly to a "?" signal POI. Does the event open when you arrive, and does the "?" go away? Is 900 too close or too far?
6. Fly to a trade station in a new system. Does a station event (The Fence, Smugglers' Den, The Informant) feel like it belongs to that station?
7. Jump to a new system. Does the Arrival event come at a good moment (about 6 seconds after arrival), or does it interrupt a fight?
8. Text tone: are the texts short enough to read at a glance? Do they sound like one game, like FTL? Note any line that is too long or unclear.
9. Balance: is a typical event worth less than a vanilla mission, but enough to make the choice interesting? Are the costs at sector 1 affordable with the starting money (about 10,000 credits)?
10. The Corporate Checkpoint scan: with 6 tech, the scan almost always fails (85 %). Does that read as fair? Does it make you want to sell tech earlier?
11. Story chains: buy the forged codes at the start, then meet a Corporate Checkpoint. Does the hidden option appear? Take the Defector aboard, then look for "Varga's Old Office" in sector 5 or later.
12. Fights: "Freighter Under Fire" (4-minute limit), "The Magpies" and corporate patrols. Do the spawned ships appear at a fair distance? Is the siege winnable in time? Do the Magpies go for the vault?
13. Multiplayer (co-op): both players see the window. One player chooses. Does the other player's window switch to the result? Does a second click on an old page do nothing?
14. Frequency: over a full run, are there too many or too few events? Do two events ever come right after each other?

## Missions (review changes)

1. Convoy Duty: the offer shows a "Heat -15 (convoy cover)" reward line. When you collect the reward at the station, does heat drop? Does the offer text explain why?
2. Discreet Bounty: when a trap springs, the message "It was a setup. Corporate hunters drop out of the dark!" shows with the wanted alert. Is it clear that the bounty was a trap?

## Events batch 2 (spec 79-86)

1. Wanted Broadcast (heat 60+): do the "Bounty Hunter ..." ships read as local hunters, different from corporate hunters? Does their bounty mission appear in the log?
2. The Magpies Return: pay Rook a crate in sector 2 or later, then go 2 more sectors. Does Rook come back? With Dr. Varga aboard, does the rigged-crate option show, and does the payoff feel earned?
3. Refugee Convoy: do the reputation and fame lines read clearly in the result? Is selling the convoy tempting, given the heat drop?
4. Pirate Toll Gate: compare the toll at low and high heat. Does the button cost make the twist clear ("the hotter you are, the more you pay")?
5. The Buyer's Agent (1 to 2 sectors before the Buyer): is the advance tempting? After a deal, does the trade window at the Buyer show the lower price?
6. Courier Job: look for "Courier Job" in a station's mission list. Is the contact easy to find (target area on the map)? Is 7 minutes too short or too long? Does it feel different from a vanilla delivery?
7. Vault Coolant Leak (at most one per run): does an event with no free choice feel fair, or punishing? Is the choice between a crate, crew and money a real decision?
8. Dead in Space: with a Tractor Beam Emitter, can you actually pull the freighter to the station? Is the distance fair (the nearest station at least 3,000 away can be much farther)? Does the map show the destination ring?
9. "Clean Getaway?" shows the real tech count ("Your vault holds 6 crates"). Do the choice buttons show the gold key numbers from the UX area, with no second "1." in the text?

## Co-op (two players)

The co-op autotests (`tests/autotest-mp/`) cover sync, the host's tunables, events, a sale, a loss, a win, missions, save, load and resync. Only two people can check the points below.

Before you start:

- Both players install the same Hot Cargo release and turn the mod on. A different mod version blocks the join.
- The host's `tunables.rules` applies to the run. The client's file has no effect in co-op. A new co-op run uses the default values for the first seconds, until the host's values arrive. The starting vault and the first system's offers use the defaults.
- Debug keys do not work in co-op.
- If the game shows "resyncing", note what happened just before. Both players then send their `Mods\hot_cargo\csharp\hot_cargo_log.txt`. Search it for `MP DESYNC`.

Checks:

1. Host a career, the friend joins. Do both players see the run intro, the same heat panel values and the same tunables hash?
2. Only the host's starting ship gets the Tech Vault. Is that fine for co-op, or must each player start with a vault? Can the friend's crew take tech from the host's vault?
3. Play 20 minutes with hunters on screen: at least two stages, a Strike wave and a fight. Does a "resyncing" message show? Do both players see the same hunters in the same places?
4. Hunters prefer the ship with the Tech Vault. With the vault on one player's ship, does the other player feel useful in the fight?
5. Each player opens the comms window with a station. The game does not pause in co-op (`PauseOnCommsMultiplayer` is off). Is that right, or must co-op pause too?
6. An event opens. Do both players see it? The friend picks a choice with a cost. Does the friend pay, and not the host? Does the host's window switch to the result?
7. Both players click a choice at the same moment. Does only one choice apply, and do both see the same result?
8. The friend sells tech at a station. Does the friend get the money? Does the tech count and the heat drop for both?
9. The friend accepts a Hot Cargo mission (escort, assault, shady offer). Does it show in both mission logs? Does the reward pay both players?
10. Jump to a new system together. Does the heat carry over the same for both? Does the Arrival event come for both?
11. The host saves. Quit, host again, load the save, and the friend joins. Do heat, stage, the pending event and the missions come back?
12. The friend leaves mid-run. Does the run continue for the host without errors? The game does not accept a join into a running game. To bring the friend back, use the save and load steps of check 11.
13. Sell the last unit or lose the vault. Does the loss show for both players at the same time?
14. Deliver tech to the Buyer. Does the win show for both, with the same score?

## Mechanics missions (spec 130-141, MISSIONS.md)

The autotests cover offers, completion, failure, leave early, intensive scans and save and load. They do not fight for real. To see the missions fast, put `Mechanics { DroneSiegeChance = 1  PrototypeChance = 1  SunMiningChance = 1  TowChance = 1  CaptureChance = 1  SurveyChance = 1  MiningGuardChance = 1  ListeningPostChance = 1 }` in `tunables.rules`, then jump to a new system.

1. Drone Siege: build a platform with **Build New Ship** next to the post. It needs a reactor, a gun and a crew. Fly away. Does the clock keep running? Do the drones kill the platform too fast or too slowly? Are tier-1 to tier-3 drones "small" enough, or should drones be tier 1 only (`Siege { ShipTierMax = 1 }`)?
2. Drone Siege: without a platform, is five minutes of waiting too long, or fine? Does "No ship on station: the clock is stopped" make sense?
3. Leave early: does the "[Click] Leave now and keep X credits" line work in the HUD and in the mission log? Is the amount clear?
4. Prototype Retrieval: can you see the core (magenta storage bay) on the carrier's hull? Can you target the reactors and bridge with part targeting without hitting the core? Does the carrier offer surrender when it is beaten? Does a carrier that was damaged before you arrived heal when the core goes on (known limit)?
5. Sunside Ore: is the heat zone dangerous but survivable for a small mining ship? Are 30 to 45 units too many or too few for 30 minutes? Can you sell Helion Ore at the station (it needs free untyped storage)?
6. Salvage Rights: can a starter-size ship tow the hulk with tractor beams in 25 minutes? Is 800 from the station hull a fair finish line?
7. Repossession: do the escorts die before the freighter? Does the freighter offer surrender? Does claiming the wreck with crew work in the real UI (select crew, set the wreck as home ship)?
8. Storm Survey: is 15 s per marker inside an ion storm fair? Does the starter ship have a working sensor?
9. Listening Post: does the scout follow you well? Is the "[Click] Start the scan here" line easy to find? Do intensive scans feel worth the extra pay? Is the base close enough to be a threat?
10. Frequency: with the economy area's mission limits per system, do mechanics missions show up about once every two systems?
11. Multiplayer (co-op): one player clicks an order. Does the other player see the same result?

## Story chains (spec 160-175, CHAINS.md)

The autotests cover assignment, offers, results (success, abandoned, lost, timeout), choice pages, loops, Return and Arrival stages, rewards, reputation and save and load. Only a person can judge the points below.

1. Open a station's comms in the first system. Is the "[Chain] ..." offer easy to tell apart from the other offers? Does the "Story Chains" category make sense in the list and in the mission log?
2. Read a stage offer. Is the briefing short enough? After a few stages, does the "So far:" line help you remember the story, or is it noise?
3. Finish a stage at the station. Does the choice window open at a good moment (near the station, after the payment)? Does the result text read well above the page?
4. Escort and careful-targeting stages: do they play well with the default ships? Is seven minutes enough for "Extraction"? Can you keep the transport alive while you kill its escorts, or do your turrets kill it by accident?
5. "Reactor Shielding": is 40 to 60 iron in 15 minutes fair at sector 1? Is buying the iron at the station too easy a way out?
6. "Salvage Race": can you find the pod and haul 3 processors in 8 minutes? Do the Magpie ships make it a race?
7. Return stages: refuse Mara's bill, leave the system and come back. Do the collectors appear about 8 seconds after you arrive? Does the result read as a consequence of your refusal?
8. Arrival stages: take Okafor aboard or sell her. Do "Old Schedules" or "Okafor's Friends" come at a fair moment in the next system or the one after?
9. Mara's standing offer: does it show on each visit and stay away inside one visit? Is 1.33 × the tech value worth a detour?
10. Frequency: with every station holding a chain, are there too many offers per system? If yes, lower `Chains.StationChance` (for example 0.5) in `tunables.rules`.
11. Multiplayer (co-op): both players see the chain offer and the chain pages. One player chooses. Does the chain advance the same for both?

## System archetypes (spec 220-229, WORLDS.md)

The autotests cover the archetype mix on the map, the hazard tooltip, the current drift, the belt passes, the pass hold win and loss, heat depth, the sundive, lair discovery, save and load, and the current in co-op. Only a person can judge the points below.

1. Open the galaxy map at the start. Hover a few systems. Is the gray hazard line under the system type easy to read? Do you plan your route around it?
2. Jump into a new system. Does the toast and chat line ("Trade Hub. A current runs ...") show after the system name, not over it?
3. Trade Hub: find the current (teal band with drifting arrows). Fly with it and against it. Is the push felt but not annoying? Is the direction clear from the arrows? Tune `Worlds.CurrentAccel` (9) and `Worlds.CurrentFlowSpeed` (45).
4. Trade Hub: is the hub world visible from the station, and does the system feel different from a basic one? If not, say what is missing.
5. Mining Belt: fly a large ship into the belt. Is it hard to cross outside a pass? With a small ship, can you thread it? Do the pass rings show on the map? Watch the frame rate near the belt (about 1,800 rocks).
6. Hold the Pass: clear the campers, then hold. Is 2 minutes with a wave every 35 s fun or a chore? Is the "pass is empty" warning clear before the mission fails?
7. Solar Zone: fly toward the sun. Does the HEAT ZONE line show the right ship and turn red at about half depth?
8. Sundive: the heat burns Tech Vaults fast (a quick dive at depth 0.2 cost 3 of 6 units in a test). Is that a fair tension ("move the tech to another ship first"), or does it make the mission a trap? Options: a shallower site, a shorter salvage (`Worlds.SundiveSalvageSeconds`), or a hint in the offer.
9. Sundive with shields up: do shields make the dive safe enough to be the clear answer?
10. Deep Space: is the lair search circle a fair size? Do the storms around the lair make the approach tense without killing the ship?
11. Mix: in one run of 8 sectors, did you meet each archetype at least once? Did any feel like extra travel? The weights are in `mod/worlds/archetypes.rules` (restart needed).
12. Co-op: both players see the current push and the same pass hold clock.

## Ship tactics (spec 400-412)

The autotests `shipai-basic`, `shipai-save` and the co-op test `shipai` cover row order, most conditions and actions, saves, designs and sync. These checks need a person.

1. Select your ship and press Ctrl+K, or click the "AI" button in the ships card. Does the window fit the game's look? Are the drop lists easy to read?
2. Apply each preset with Auto on and fight a Patrol stage (F10 with debug keys). Does each preset behave the way its name says? Which one feels useless?
3. Sniper: does the ship keep its distance, and does it still hit? Note any target that it never reaches.
4. Give a move order while Auto is on. Does the ship obey at once, and do the tactics take over again when it arrives?
5. Put two weapons in part group 1 (build menu, part groups) and add a row "Always → Fire: hold, group 1". Do only those two stop firing?
6. Save the ship design, then start a new career with it (or use "Load ship (replace)" in the build menu). Do the rows and the Auto switch come back?
7. Copy the tactics of one ship and paste them into another. Paste text from a chat message. Does a bad text show a clear message?
8. Buy a second ship and give it the Escort preset. Does it stay with your first ship and fight the enemies that attack it?
9. Flee and jump: give a ship with an FTL drive "My health below 40% → Move: flee (FTL jump)". Does it jump to an ally when hurt?
10. Co-op: each player edits their own ship's tactics with the window open on both machines. Does the other player see the change? Is a "resyncing" message shown?
11. Is something missing from the condition or action lists that you wanted in a fight?
## Wanted and hunted (spec 470-473)

1. Attack a faction ship (or a station) so that police hunt you, and get heat to the first stage (25). Does the top message read clearly? Does the chat line explain the ways out well enough to act on?
2. Try the vanilla payoff: hail one of the police ships. Does the "pay the bounty" option show, as the hint says?
3. A Clean Record and Decoy Run: is the price fair against the vanilla bounty? Does the heat risk feel fair?
4. Does the warning come back too often (it shows again each time you become both wanted and hunted)?

## How to Play and the run log (spec 720-734)

The autotests `onboarding-hints` and `onboarding-log` cover each trigger, the order and the log lines. These checks need a person.

1. In the game settings, click "Reset Tutorials", then start a new career. Do the Hot Cargo pages show in the "How to Play" list before the vanilla ones? Does each page show at a moment when it helps?
2. Open each Hot Cargo page. Is it short enough to read in a fight? Is a term unclear or a page missing?
3. Do the vanilla tutorials come back after about 5 minutes, once you read the Hot Cargo pages? Is that too early or too late?
4. Turn "Show tutorials" off in the game settings. Do the Hot Cargo pages stop too?
5. Open the career log (the mission log button) and the "Hot Cargo run" tab. Does it tell the story of your run? Is a line missing, or is one noise?
6. Co-op: do both players see the same run log lines?

## Research and abandoned ships (spec 760-766)

1. The Archive at a station: is "Trade 1 crate (worth 15,000) for research worth 21,000" clear? Do the new parts show as buildable at once?
2. A Lab That Won't Take No: does refusing feel like a real threat (heat +20)?
3. Data Broker: is the sale price tempting without being free money late in the run?
4. Fly to an abandoned ship ("?" POI). Does the event open when you are close? Is a corporate trap at high heat a fair surprise?
5. Find the relic ship (one per run, sector 2 or later). Does the vault message make you want to look for it? Is the "?" easy to spot?

## Events in ship comms (spec 767-771)

1. Dead in Space: does the freighter call you, and does its hail show the page in the comms window? Is "Fly within comms range to answer, or hail it" clear?
2. Dock at a trade station and open its comms. Do "Sell a crate off the books" and station events read well inside the comms window? Do the gold key numbers work there?
3. Hail a faction patrol ship at heat 10+. Does "Ask about hunters nearby" show?
4. Shoot the freighter while its page is open. Does the page close with "The ship is gone"?
5. Co-op: both players open the same ship's comms. One chooses. Does the other's window update, with no double effect?

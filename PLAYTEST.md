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

1. Look at the starting ship. Check that a tinted 2×2 Tech Vault replaces one storage bay, and that its stacks show 6 units.
2. Start a career with the Model-M starter ship. Check that the vault is outside the hull with a door, and that crew can walk to it.
3. Open the build menu, Storage group. Check that "Tech Vault" is there with its name, description and tinted icon.
4. Dock at a trade station and open the trade window. Check that "Stolen Tech" shows the overlay tech value as its sell price.
5. Sell 1 unit through the trade window. Check that the money rises by the shown price and the overlay tech count drops by 1.
6. In the trade window, check that the station offers the sold unit back at 1.5 × the sell price.
7. Try to move tech into a normal storage bay with the transfer window. Check that the game refuses it.
8. Let an enemy shoot the vault. Check that the stacks shrink as it takes damage, and that repair does not bring them back.
9. Sell the last unit. Check that the loss dialog "The corporation recovered the tech." shows about 3 seconds after the delivery.
10. Press F11 and Shift+F11. Check that the overlay tech count goes up and down by 1.

## Hunters and tactics (spec 30-35, 43-49)

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

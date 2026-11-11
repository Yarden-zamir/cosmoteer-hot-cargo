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

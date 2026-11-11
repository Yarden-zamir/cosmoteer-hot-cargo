# Hot Cargo — Cosmoteer career mod spec

Working title. Target: Cosmoteer 1.0.0 (.NET 10), Enhanced Mod Loader with Harmony 2.4.3.

## Premise

The player starts the career with stolen corporate technology. The corporation hunts the player to get it back.
The player can sell units of the technology for money. Each sale lowers the pressure, and each sale is worth more later in the game.

## Terms

- **Tech unit**: one unit of the stolen technology. The player holds a whole number of units.
- **Tech value**: the money that one tech unit gives when the player sells it.
- **Heat**: how close the corporation is to the player in the current sector. Range 0 to 100.
- **Heat multiplier**: a factor that comes from the number of tech units the player holds.
- **Tunables file**: the mod file that holds all numbers in this spec. The mod reloads it while the game runs.

## Requirements

### Tech units

1. The career starts with `StartingTechUnits` tech units (default 6).
2. The player can sell one tech unit at a trade station, through the station comms.
3. A sale gives the current tech value in money and removes one tech unit.
4. Tech value = `TechBaseValue` + `TechValuePerGameHour` × game hours played + `TechValuePerFame` × total fame earned.
5. The station comms show the current tech value before the player sells.
6. In multiplayer, a sale goes through the game's multiplayer input system, like `MPAcceptMissionInput`. The game simulation must stay the same on all clients.

### Heat

7. Heat belongs to the current sector.
8. Heat increases by heat multiplier × (`HeatPerGameMinute` × game minutes + `HeatPerFame` × fame earned).
9. Heat multiplier = `HeatMultiplierByUnits[tech units held]`. The table has one entry for each possible unit count, from 0 to `StartingTechUnits`.
10. Heat uses game time. Heat does not increase while the game is paused.
11. When the player enters a new sector, heat = old heat × `HeatCarryOverFraction`.
12. The HUD shows heat, the heat multiplier and the next threshold.

### Corporate response

13. `HeatStages` is a list of thresholds. Each threshold has a response: a warning, scouts, a hunter patrol, a hunter fleet or a strike force.
14. When heat crosses a threshold, the response starts once for each sector visit.
15. Hunter fleet strength scales with `HunterStrengthByStage` and the player fleet value.
16. The highest stage ends in a hard fail or in survivable waves. **Open decision.**

### Missions

17. An escort mission spawns a friendly AI ship that travels to a destination. The player must keep it alive.
18. A fleet assault mission spawns an allied AI fleet and a hostile station. The player and the allies destroy the station together.
19. A shady mission looks like a normal mission offer, with a reward of `ShadyRewardFactor` × normal reward.
20. A shady mission is a trap with chance `ShadyTrapChanceBase` + `ShadyTrapChancePerHeat` × heat. A trap spawns a corporate ambush at the mission location.
21. Each new mission type uses the vanilla objective types where possible: `ProtectShips`, `DefeatShips`, `GotoShip`, `Sequential`, `AnyOrder`, `TimeLimit`.

### Tunables and debug

22. All numbers in requirements 1 to 20 come from the tunables file.
23. The mod watches the tunables file. When the file changes, the mod reloads it without a game restart.
24. If the file has an error, the mod keeps the last valid values and shows the error in game.
25. A debug overlay shows heat, the heat rate, the heat multiplier, tech units, tech value and the current stage.
26. In multiplayer, the mod does not reload. All players must have the same tunables file. The mod compares a hash of the file at join.
27. Mission definitions (requirements 17 to 19) are `.rules` content. A change to them needs a game restart.

### Persistence

28. Tech units, heat and the current stage are saved with the career save and loaded with it.

## Known engine facts (checked on game 1.0.0)

- Fame exists. `career.rules` has `FameTitles` with `FameLevelMultiplier`, and missions give fame through `FameReward`.
- A wanted and police system exists: the `Wanted` block in `career.rules` and `Cosmoteer.Modes.Career.WantedManager`.
- Missions are composed in `.rules` from ship spawners and objectives, for example `Data/modes/career/encounters/encounter_ambush.rules`.
- No general rules reload was found. Only strings and music have a reload.
- Multiplayer uses input classes (`MPAcceptMissionInput`, `MPAbandonMissionInput`), so the simulation probably runs in lockstep.

## Not yet known

- The values of `ShipSpawner.ShipAllegiance`. This decides if allied AI ships work with rules only (requirements 17, 18).
- A random choice between spawn variants with rules only. If none exists, shady missions need C# (requirement 20).
- The API of the game's `.rules` parser (`Halfling.ObjectText.OTFile`) for the tunables file.
- How the career save stores mod data (requirement 28).

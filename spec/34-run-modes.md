## 34. Run modes

Difficulty pressure, Ironman losses and Strike music. Code: `src/HotCargo/RunModes`. Pure math: `RunModes/PressureModel.cs`. Tunables: the `RunModes` section.

680. **Difficulty table.** The vanilla difficulty settings of the career (combat difficulty, economic difficulty, crew scarcity) scale Hot Cargo's pressure. `RunModes.Combat`, `RunModes.Economy` and `RunModes.Crew` hold one row for each vanilla level, by level ID, with five factors: `HeatRate`, `HunterShips`, `HunterTiers`, `ContractFees` and `TechValue`. The rows of the career's three levels combine: the factors multiply and the tiers add. The start job of the run (requirement 1201) adds one more row. A level without a row counts as 1 and 0 tiers, so a level that another mod adds plays like the default. The tunables keep their values; the code multiplies at each read. The levels are the confirmed vanilla values, so a level change in the game settings (host only in co-op) applies on every machine at the same tick, with the timing in TUNABLES.md. A row that names no vanilla level is a tunables error. Defaults (an empty cell is 1, or 0 tiers):

| Setting | Level | Heat | Hunter ships | Hunter tiers | Fees | Tech value |
|---|---|---|---|---|---|---|
| Combat | `no_death` | 0.8 | 0.67 | −1 | | |
| Combat | `easy` (vanilla default) | | | | | |
| Combat | `normal` | 1.1 | | | | |
| Combat | `hard` | 1.2 | 1.34 | | | |
| Combat | `very_hard` | 1.3 | 1.34 | +1 | | |
| Economy | `free_build` | | | | | |
| Economy | `fast` (vanilla default) | | | | | |
| Economy | `normal` | | | | 1.2 | 0.9 |
| Economy | `slow` | | | | 1.4 | 0.8 |
| Crew | `max` | 0.9 | | | | |
| Crew | `plentiful` | 0.95 | | | | |
| Crew | `standard` (vanilla default) | | | | | |
| Crew | `scarce` | 1.1 | | | | |

The vanilla defaults stay at 1, so the balance numbers (BALANCE.md) hold for them. Combat difficulty sets the danger, so it scales heat and hunters. The economy sets the money, so it scales fees and the tech value: on a slow economy missions pay less, and a lower tech value keeps a sale from replacing missions. Crew scarcity limits how fast a fleet grows, so it scales heat a little.
681. **Heat rate.** Heat from time and from fame (requirements 24, 25) × `HeatRate`. `Run.HeatMultiplier` is the tech multiplier (requirement 26) × `HeatRate`. The heat panel rate, the overlay and the trade hint use it. Contract heat (flat heat, trail, informants), event, mission and debug heat keep their values.
682. **Hunters and fees.** Each hunter stage response (requirement 190), the repeat Strike waves too, sends round(ships × `HunterShips`) ships, half away from zero and at least 1, of the tier + `HunterTiers`. The co-op scaling (requirement 334) applies after it. Informant sell-outs (requirement 104) keep their count and tier. Each contract accept fee (requirement 102), rules fees too, × `ContractFees` when its terms roll. The completion bonus follows the fee. An offer keeps the terms that it rolled.
683. **Tech value.** The tech value (requirement 19) × `TechValue`, rounded to 100. The station price, the buy-back price, the Buyer price and the score follow it. A level change mid-run can lower it.
684. **Display.** When a factor differs from 1, the run intro (UX.md H4) shows a "DIFFICULTY" line with the names of the three levels and each factor that is not 1. The heat panel has a tooltip with the same level names and one line for each factor that is not 1, or "The corporation hunts at its normal pace." when every factor is 1. After a start job pick, the factors include the job's, and a "JOB" line names it (requirement 1205). The tooltip is always available, so a loaded run and a level change mid-run show it too; the intro line shows only at the start of a new run. Screenshot: `docs/screenshots/runmodes-paneltip.jpg` (`tests/screens/runmodes.txt`).
685. **Ironman.** In an Ironman career (vanilla `IronmanModeEnabled`), a Hot Cargo loss (requirement 9) deletes the Ironman save and clears the last save, like the vanilla game over. Only the host has the save, so only the host deletes it. The save does not come back: no save is allowed after a loss (`AllowSaveGame`), so the exit save and the autosave do not run. The run intro says "IRONMAN: lose the tech and this save is deleted.". The game-over screen says that the save is deleted and that the run cannot be reloaded. The win screen says that the score is final; a win keeps the save. Without Ironman nothing changes (requirement 11).
686. Removed: the fame title tier prototype. Requirement 305 (a hunter tier floor at the main route tier of the sector index) gives the floor in low side systems that it was meant to give.
687. Removed: the fame title probes and their recommendation (requirement 686).
688. **Strike music.** With `StrikeMusic` on (default), while the run is at the last heat stage (all stages started, requirement 31), the music plays its battle layers: a postfix on `MusicParameters.Update` sets the vanilla `battle` value to true. Each vanilla track has a battle layer (`battle == True or wanted == True`), and the vanilla fade-out waits while battle is true. Below the last stage, after a jump, after a win or with the switch off, the music is vanilla. Limit: the intro track of a cluster has no battle layer, so the battle layers start with the next track. The log names the playing track and its active conditional layers at each switch. Local audio only, no sim state.
689. **Tests.** `runmodes-difficulty` (every vanilla level against its row: heat multiplier, tech value, station price, fee factor, every stage response; then a hard combination through the vanilla settings inputs: the heat tick rate, the station price, re-rolled fees and a real Patrol spawn), `runmodes-ironman` (a loss keeps the save without Ironman; deletes it with Ironman, with the game-over line; a win keeps it, with the win line), `runmodes-paneltip` (the tooltip text that the game shows at the default levels, after a level change and in a loaded run, and the open tooltip box), `runmodes-music` (the switch at Strike, the off switch, the return below the last stage), co-op step `runmodes.difficulty` in `coop-core` (the host's level change, heat, tech value, hunter tiers and a spawn on both machines, no desync).

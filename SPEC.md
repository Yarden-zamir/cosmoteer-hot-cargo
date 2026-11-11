# Hot Cargo — game design spec and plan

Cosmoteer career mod. Target game version: 0.30.5a (assembly version 1.0.0, .NET 10).
Status: design complete, implementation not started. Items marked **[verify]** wait for engine research.

## 1. Pitch

You stole corporate technology. The corporation hunts you across the galaxy.
Get the tech to a buyer at the far edge of the map before the hunters catch you.
Each unit you sell on the way buys safety and money, but lowers your final score.

A run is short, like FTL: about 8 sectors and 2 to 3 hours.

## 2. Design pillars

- **Hold or sell.** Every tech unit you hold makes the chase harder. Every unit you sell makes the run easier now and poorer at the end.
- **Move on or burn.** Missions earn fame, and fame brings the hunters faster. A sector supports only a few missions before you must leave.
- **Protect the cargo.** The tech is a physical thing in your ship. If you lose it, the run ends when the last unit is gone.
- **Tune live.** All numbers are in one file. The game picks up changes without a restart.

## 3. Scope

In scope: run structure and length, win and loss, tech resource and Tech Vault part, dynamic tech price, heat, corporate response, three new mission types, new AI tactics for hunters, tunables with live reload, debug overlay and keys, save support, co-op multiplayer support.

Out of scope for this batch:

- Multiplayer pings. They are deferred.
- Camera reset to north. The game already does this: press Backspace, or click the north arrow on the minimap.
- Steam Workshop release. The mod loads through a .NET startup hook (see section 15).

## 4. Terms

- **Run**: one career from start to win or loss.
- **Sector**: a group of systems on the career map. The run moves through sectors in order. **[verify]** the engine meaning of sector.
- **Sector index**: the number of the current sector in the run. The first sector is 1.
- **Tech unit**: one unit of the stolen technology. It is a resource.
- **Tech Vault**: a ship part. It is the only part that can store tech units.
- **Fleet tech count**: the tech units that all human players hold, in vaults and in crew hands.
- **Tech value**: the price that a station pays for one tech unit.
- **Heat**: how close the corporation is in the current sector. Range 0 to 100.
- **Heat multiplier**: a factor that comes from the fleet tech count.
- **Stage**: a level of corporate response. Heat thresholds start each stage.
- **Buyer**: a special station in the last sector. You deliver the tech there to win.
- **Tunables file**: `tunables.rules` in the mod folder. It holds all numbers that C# code reads.

## 5. Run structure and length

1. A run has `RunSectorCount` sectors (default 8). The Buyer is in the last sector.
2. The career map is small enough that a direct route crosses all sectors in about `RunSectorCount` sector jumps. **[verify]** which galaxy size settings and map rules control this.
3. The danger tier of a sector rises with the sector index. Sector 1 is tier 1. The last sector is about tier `FinalSectorTier` (default 12).
4. Fame and money rewards are scaled by `RewardScale` (default 2.0), so that ship growth keeps up with the shorter run.
5. The new career screen keeps the vanilla difficulty options. The mod does not add its own difficulty screen.

## 6. Win and loss

6. **Win**: the players dock at the Buyer and deliver at least one tech unit.
7. When the players win, a dialog shows the score. Score = money + delivered units × tech value × `BuyerPriceFactor` (default 1.5).
8. After a win, heat turns off. A dialog shows the score, and the players can continue the career.
9. **Loss**: when the fleet tech count reaches 0, the run is lost. Any cause counts: vault damage, a destroyed vault, crew that die while they carry tech, or a sale of the last unit. A Buyer delivery is the only exception, because it wins the run first.
10. A loss shows a dialog: "The corporation recovered the tech." The game then shows the game-over screen.
11. The mod does not delete save files after a loss.
12. The loss check runs only when the count is certain: no ship jump is in progress, and no friendly ships wait in another system. The count must stay at 0 for `LossGraceSeconds` (default 3 s), so tech that crew carry between ships does not trigger a loss.

## 7. Tech units and the Tech Vault

13. Tech is a new resource: "Stolen Tech". It uses the normal resource systems: storage, crew carrying, transfer between ships, and trade.
14. Only the Tech Vault part stores tech.
15. The Tech Vault is a buildable part. Its rules define its size, health, cost and capacity. Defaults: 2×2, high health, capacity 6 units. Rules changes need a game restart.
16. To move tech between ships, the players build a vault on the other ship. The crew then carry the tech over, like any other resource.
17. A new run starts with `StartingTechUnits` (default 6) in a Tech Vault on the starting ship.
65. Damage to a Tech Vault destroys tech inside it. Each time a vault takes damage equal to `VaultDamagePerUnitLost` × its maximum health (default 0.15), it loses one unit. A destroyed vault loses all units inside it.
66. Repair of a vault does not bring back lost tech.

## 8. Tech value and selling

18. Players sell tech through the normal station trade screen. The mod adds no special sell screen.
19. Tech value = `TechBaseValue` × (1 + `TechGrowthPerSector` × (sector index − 1) + `TechGrowthPerGameHour` × game hours).
    Defaults: base 10,000, +35 % for each sector, +10 % for each game hour.
20. The trade screen shows the current tech value as the resource price.
21. Trade stations buy tech. Stations do not sell tech. **[verify]** which rules decide which stations trade a resource.
22. Tech value is computed from simulation state only, so it is the same on all multiplayer clients.

## 9. Heat

23. Heat belongs to the current sector and starts at 0 in sector 1.
24. Each simulation tick, heat increases by heat multiplier × `HeatPerGameMinute` × elapsed game minutes. Default: 2 heat per minute.
25. When the players earn fame, heat increases by heat multiplier × `HeatPerStandardMission` × (fame earned ÷ standard mission fame of the sector tier). Default: 15 heat for each standard mission.
    The division keeps "heat per mission" stable across tiers, so that the tunable reads as "missions per sector".
26. Heat multiplier = `HeatMultiplierByUnits[fleet tech count]`. Default table for 0 to 6 units: `[0.25, 0.45, 0.6, 0.75, 0.9, 1.05, 1.2]`. A count above the table uses the last entry.
27. Heat does not increase while the game is paused. Game speed changes the rate, as it does for all game time.
28. When the players enter a new sector, heat = old heat × `HeatCarryOverFraction` (default 0.25).
29. Heat stays in the range 0 to 100.

With defaults and 6 units held, a sector reaches the top stage after about 6 missions, or after about 40 minutes with no missions.

## 10. Corporate response

30. `HeatStages` is a list of stages. Each stage has a threshold and a response. Defaults:

| Stage | Heat | Response |
|---|---|---|
| Warning | 25 | Alert sound and message. One scout ship of the sector tier. |
| Patrol | 50 | Two hunter ships of the sector tier. |
| Hunt | 75 | Three hunter ships of the sector tier + 1. |
| Strike | 100 | A strike force of the sector tier + 2. A new wave every `StrikeWaveIntervalSeconds` (default 180 s) while heat stays at 100. |

31. Each stage starts once for each sector visit. The Strike stage repeats its waves. A wave spawns only enough ships to reach `MaxLiveHunters` (default 12) live hunters in the system.
32. Hunters spawn at a distance of `SpawnDistanceMin` to `SpawnDistanceMax` (default 2,000 to 3,000) from the center of the player fleet. All ships of one wave come from one direction, spread over `WaveArcDegrees` (default 40°). They always know where the players are, move to them and attack. Hunters far from the players move toward them in stasis too.
33. Hunters are a corporate force, not a rules faction. They use built-in ships with the `ShipTags` tag (default `combat`) from the `ShipFactions` pools (default: all factions), at the wanted tier or the nearest lower tier. Their names start with `ShipNamePrefix` (default "Corporate"). All hunters in a system share one hostile player index, so they are allies of each other and enemies of the players. Like vanilla ambushes, they are also enemies of faction ships. Resolved: a rules faction needs an icon, two player indices and its own ship pool, and the faction reputation systems would treat it as a normal faction. A unique player index gives the same result at much lower cost.
34. Hunters prefer to attack ships that carry Tech Vaults. A ship with a Tech Vault part counts `VaultTargetFactor` times more (default 3) when a hunter picks the nearest target.
35. The Strike stage is dangerous but survivable. It does not end the run directly. Only a loss under requirement 9 ends the run. **Open decision:** the user may choose a hard fail instead.

## 11. Missions

New mission types appear in the normal mission lists, next to the vanilla ones.

36. **Escort**: a friendly AI ship flies to a destination in the system. The players must keep it alive until it arrives. Reward on arrival.
37. **Fleet assault**: an allied AI fleet and the players attack a hostile station together. Reward when the station is destroyed.
38. **Shady offer**: a mission offer with a reward of `ShadyRewardFactor` × the normal reward (default 2.0). It uses the shape of a normal mission (delivery or bounty).
39. When the players arrive at a shady mission location, it is a trap with chance `ShadyTrapChanceBase` + `ShadyTrapChancePerHeat` × heat. Defaults: 0.2 + 0.006 per heat point, so 80 % at heat 100.
40. A trap spawns a corporate ambush of the sector tier + 1 at the mission location. A trap that the players survive pays `ShadyTrapSurvivalReward` × the normal reward (default 0.5).
41. All new missions earn fame, and so they add heat through requirement 25.
42. New mission types use vanilla objectives where possible: `ProtectShips`, `DefeatShips`, `GotoShip`, `Sequential`, `AnyOrder`, `TimeLimit`. **[verify]** allied ship spawns and random trap choice.

## 12. Hunter AI tactics

43. Hunters use new tactics. Each tactic has an on/off switch (`<Tactic>Enabled`) and a minimum stage (`<Tactic>MinStage`, a stage name or empty) in the tunables file. With no active run, or with a tactic off, the hunter AI uses the vanilla module for that job.
44. **Kite**: ships with a longer weapon range than the target hold at `KiteRangeFraction` (default 0.9) of their range, edge to edge. A ship kites only when that distance is outside the target's longest weapon range.
45. **Pincer**: in a group of two or more on the same target, ships split and attack from sides that are `PincerAngleDegrees` (default 120°) apart, alternating left and right of the group leader. With more ships than fit in a circle at that angle, the angle shrinks to 360° ÷ group size. The leader turns the whole formation a little every 10 to 20 s.
46. **Disable**: ships aim all weapons at the nearest weapon or thruster part of the target. Default minimum stage: Hunt.
47. **Fall back**: a ship below `FallBackHealthFraction` (default 0.35) moves `FallBackDistance` (default 1,500) away from the nearest player ship and repairs. It returns at `FallBackRecoverFraction` (default 0.6) or after `FallBackMaxSeconds` (default 45 s), and cannot fall back again for the same time. Health is built value ÷ blueprint value, as in the vanilla AI.
48. **Bait**: in a group of two or more, the ship with the most thrust per mass comes within `BaitCloseDistance` (default 150) of the nearest player ship, then pulls back to the middle of its group for `BaitPullBackSeconds` (default 12 s), and repeats. Default minimum stage: Strike.
49. Each tactic has a visible tell, so the players can read it and counter it. For example, a kiting ship keeps its distance, and a ship that falls back turns away.

## 13. Tunables, debug and live tuning

50. The C# code reads all numbers in requirements 1, 3, 4, 7, 12, 17, 19, 23 to 48 and 65 from `tunables.rules`. `tunables.rules` holds only changed values. The mod writes all values and defaults to `tunables.defaults.rules` at game start.
51. The file uses the game's `.rules` format.
52. The mod watches the file. When the file changes, the mod reloads it on the next frame, without a game restart.
53. If the file has an error, the mod keeps the last valid values and shows the error in game.
54. Part, resource and mission definitions are mod `.rules` content. A change to them needs a game restart.
55. An overlay shows heat, the heat rate, the heat multiplier, the fleet tech count, the tech value, the stage and the sector index. F8 toggles it.
56. When `DebugKeys = true`, debug keys work: add or remove heat, set the stage, add or remove tech units, and spawn the next stage.

## 14. Persistence and multiplayer

57. Heat, the stages that already started, the sector index and the run result are saved with the career save and loaded with it. **[verify]** the save hook.
58. All logic that changes simulation state runs in the deterministic simulation tick. It uses the simulation random source, not `System.Random`.
59. The fleet tech count and the loss rule count all human players together. A loss ends the run for everyone.
60. In multiplayer, the mod does not reload tunables. The overlay shows a short hash of the tunables file, so players can compare.
61. Debug keys do not work in multiplayer.

## 15. Mod activation and loading

62. The C# code loads through a .NET startup hook. `scripts/deploy.sh` adds `STARTUP_HOOKS` to `Cosmoteer.runtimeconfig.json` and writes `Bin/steam_appid.txt`.
63. The C# code does nothing unless the Hot Cargo rules mod is enabled in the game's mod list. With the mod off, the game stays vanilla, including vanilla multiplayer.
64. A Steam "verify files" removes the hook. Run `scripts/deploy.sh` again to restore it.

## 16. Default tunables

| Tunable | Default | Requirement |
|---|---|---|
| `RunSectorCount` | 8 | 1 |
| `FinalSectorTier` | 12 | 3 |
| `RewardScale` | 2.0 | 4 |
| `BuyerPriceFactor` | 1.5 | 7 |
| `StartingTechUnits` | 6 | 17 |
| `VaultDamagePerUnitLost` | 0.15 | 65 |
| `LossGraceSeconds` | 3 s | 12 |
| `TechBaseValue` | 10000 | 19 |
| `TechGrowthPerSector` | 0.35 | 19 |
| `TechGrowthPerGameHour` | 0.10 | 19 |
| `HeatPerGameMinute` | 2 | 24 |
| `HeatPerStandardMission` | 15 | 25 |
| `HeatMultiplierByUnits` | [0.25, 0.45, 0.6, 0.75, 0.9, 1.05, 1.2] | 26 |
| `HeatCarryOverFraction` | 0.25 | 28 |
| `HeatStages` | see section 10 | 30 |
| `StrikeWaveIntervalSeconds` | 180 s | 30 |
| `SpawnDistanceMin`, `SpawnDistanceMax` | 2000, 3000 | 32 |
| `ShadyRewardFactor` | 2.0 | 38 |
| `ShadyTrapChanceBase` | 0.2 | 39 |
| `ShadyTrapChancePerHeat` | 0.006 | 39 |
| `ShadyTrapSurvivalReward` | 0.5 | 40 |
| `KiteRangeFraction` | 0.9 | 44 |
| `PincerAngleDegrees` | 120° | 45 |
| `FallBackHealthFraction` | 0.35 | 47 |

The defaults are first guesses. Live tuning sets the real values.

## 17. Architecture

```
mod/                       rules content, copied to <Cosmoteer Mods>/hot_cargo
  mod.rules                mod header and Actions
  tunables.rules           all C# numbers (live reload)
  resources/stolen_tech    resource definition and icon
  parts/tech_vault         part definition and sprites (vanilla sprites where possible)
  missions/                escort, fleet assault, shady offer
  tactics/ai_hunter.rules  hunter AI (HotCargoHunter)
  run/                     galaxy size, sector tiers, Buyer station
  strings/en.rules         all text
src/HotCargo/              C# mod
  StartupHook.cs           entry point, assembly resolve
  Mod.cs                   Harmony setup, activation check (requirement 63)
  Tunables.cs              typed tunables record, parse, file watch, last-valid fallback
  RunState.cs              heat, stages, sector index, result; save and load
  HeatModel.cs             pure functions: heat delta, multiplier, stage crossing, tech value
  Fleet.cs                 fleet tech count, sale and delivery detection
  RunRules.cs              win and loss
  Hunters/*.cs             stage responses, hunter spawns, hunt strategy
  Tactics/*.cs             AI tactics modules, TacticsModel (pure tactic math)
  Missions/TrapObjective.cs  shady trap roll
  Ui/Overlay.cs, Ui/Dialogs.cs
  Patches/*.cs             one file for each patched game area
tests/HotCargo.Tests/      fast unit tests for HeatModel and Tunables parsing only
scripts/                   pull-game, deploy, run, stop, logs
```

Rules:

- `HeatModel` has no game types. All math lives there, so that it is testable on the Mac without the game.
- Patches stay thin. They call into the mod classes.
- One tunables record type. The overlay and the code read the same values.

## 18. Plan

The work is one batch. The order inside the batch follows the dependencies.

| Step | Work | Depends on |
|---|---|---|
| 0 | Toolchain: startup hook loader, Harmony, deploy and run scripts. **Done.** | — |
| 1 | Engine research: resources and trade, career and saves, missions and AI. **Running.** | 0 |
| 2 | Core: activation check, tunables with live reload, run state, heat model, overlay, debug keys, save and load. | 1 |
| 3 | Tech: resource, Tech Vault part, starting cargo, price patch, fleet count, loss rule. | 1 |
| 4 | Run: short map, sector tiers, reward scale, Buyer station, win rule. | 1 |
| 5 | Corporate response: hunter faction, stage spawns. | 2, 3 |
| 6 | Missions: escort, fleet assault (rules), shady offer with trap roll (C#). | 2, 5 |
| 7 | Hunter AI tactics. | 5 |
| 8 | Verification and a tuning pass. | all |

Parallel work: steps 3 and 4 touch separate files and can run at the same time as step 2. Steps 6 and 7 can run in parallel after step 5.

## 19. Verification

- **Mac**: `dotnet build` and unit tests for `HeatModel` and tunables parsing. These tests are fast and catch silent math errors.
- **PC smoke runs**: `scripts/run.sh` starts the game minimized. The mod writes a self-check to `hot_cargo_log.txt` at load: tunables parsed, patches applied, rules content found. Scripts read the log.
- **In game**: a new career in single player with debug keys. Checks: overlay values, heat growth, each stage spawn, sale price, vault loss, Buyer win, save and load.
- **Multiplayer**: one co-op session with two players. Checks: no desync over 20 minutes with hunters and a sale.
- **Playtest checklist** for the user, in `PLAYTEST.md`.

## 20. Risks

- **Game updates** can break Harmony patches. `scripts/pull-game.sh` refreshes the game files and decompiled code for a fix.
- **Desync** in co-op if any logic leaves the simulation tick or uses a non-simulation random source. Requirement 58 covers this, and the multiplayer test checks it.
- **Short career** depends on how much the map rules allow. If the map cannot be made small through rules, step 4 needs C# map patches.
- **The startup hook loads into every Cosmoteer start.** Requirement 63 keeps the game vanilla when the rules mod is off.

## 21. Open decisions

- Strike stage: survivable waves (default) or a hard fail (requirement 35).
- Tech Vault defaults: one vault holds all 6 units, or each vault holds fewer units, so that the cargo spreads over several vaults (requirement 15).
- Sale of the last unit: it ends the run under requirement 9. The alternative is to block the sale of the last unit (requirement 9).

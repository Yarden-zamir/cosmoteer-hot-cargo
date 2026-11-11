# Hot Cargo — game design spec and plan

Cosmoteer career mod. Target game version: 0.30.5a (assembly version 1.0.0, .NET 10).
Status: implemented in one batch. Tuning and playtests are open (section 18, step 8).

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
- **Sector**: one star system, that is one node of the galaxy map.
- **Sector index**: the number of distinct systems that the players visited in the run. The first system is 1. A return to a visited system does not change it.
- **Tech unit**: one unit of the stolen technology. It is a resource.
- **Tech Vault**: a ship part. It is the only part that can store tech units.
- **Fleet tech count**: the tech units that all human players hold, in vaults and in crew hands. Station crew that carry a sale that is not paid yet also count.
- **Tech value**: the price that a station pays for one tech unit.
- **Heat**: how close the corporation is in the current sector. Range 0 to 100.
- **Heat multiplier**: a factor that comes from the fleet tech count.
- **Stage**: a level of corporate response. Heat thresholds start each stage.
- **Buyer**: a special station in the last sector. You sell the tech there to win.
- **Tunables file**: `tunables.rules` in the mod folder. It holds all numbers that C# code reads.

## 5. Run structure and length

1. A run has `RunSectorCount` sectors (default 8). The Buyer appears when the players first enter the system with sector index = `RunSectorCount`. A mission in the log points to it, and a message shows it.
2. The career map is small enough that a direct route crosses all sectors in about `RunSectorCount` sector jumps. The mod adds the map size "Hot Cargo run" (`mod/journey/galaxy.rules`, 50 systems, about 7 jumps from the start to the far edge). It is first in the map size list, so it is the default while the mod is on. The player can still choose Standard. These are rules values: a change needs a game restart and applies to new careers only.
3. The danger tier of a sector rises with the sector index. Sector 1 is tier 1. The last sector is about tier `FinalSectorTier` (default 12). On the main route the tier rises by 2 for each jump. `FinalSectorTier` is a rules value in `mod/journey/galaxy.rules`, not a live tunable. Side systems keep the vanilla faction tiers, but no system has a low tier above start tier + 2 × its jumps from the start (rules type `HotCargoTierCap`). So a side system is never more dangerous than the main route at the same distance. Faction home systems (low tier above `FinalSectorTier`) keep their tier.
4. Fame and money rewards are scaled by `RewardScale` (default 2.0), so that ship growth keeps up with the shorter run. The scale multiplies the difficulty reward factors. So it applies to mission rewards, fame for defeated ships and the reward text in the UI.
5. The new career screen keeps the vanilla difficulty options. The mod does not add its own difficulty screen.

## 6. Win and loss

6. **Win**: the players sell at least one tech unit to the Buyer. The run is won when the crew deliver the first units of the sale.
7. When the players win, a win panel shows the score. Score = money + units still held × the Buyer unit price. The Buyer unit price = tech value × `BuyerPriceFactor` (default 4) × the run's Buyer price multiplier (requirement 84; 1 unless the players took an advance). A sale to the Buyer pays that price for each unit, so delivered units are in the money once, and an unsold unit counts as much as a sold one. The players go to the Buyer instead of a station because of this factor. Delivered units are the units of all crew deliveries to the Buyer; a delivery after the win sets the score again. The run state saves the score and the delivered units. `Buyer.Score` holds the formula.
8. After a win, heat turns off. The win panel shows the score and the run numbers, and the players can continue the career.
9. **Loss**: when the fleet tech count reaches 0, the run is lost. Any cause counts: vault damage, a destroyed vault, crew that die while they carry tech, or a sale of the last unit. A Buyer delivery is the only exception, because it wins the run first.
10. A loss shows the game-over screen with the title "The corporation recovered the tech", the reason and the run numbers.
11. The mod does not delete save files after a loss.
12. The loss check runs only when the count is certain: no ship jump is in progress, and no friendly ships wait in another system. The count must stay at 0 for `LossGraceSeconds` (default 3 s), so tech that crew carry between ships does not trigger a loss. The run state saves the time at 0, so a load or a client that joins sees the same timer.

## 7. Tech units and the Tech Vault

13. Tech is a new resource: "Stolen Tech". It uses the normal resource systems: storage, crew carrying, transfer between ships, and trade.
14. Only the Tech Vault part stores tech.
15. The Tech Vault is a buildable part. Its rules define its size, health, cost and capacity. Defaults: 2×2, 12,000 health (3 × the vanilla storage bay), 96 steel, capacity 8 units (4 tiles × stack size 2). Rules changes need a game restart.
16. To move tech between ships, the players build a vault on the other ship. The crew then carry the tech over, like any other resource.
17. A new run starts with `StartingTechUnits` (default 8) in a Tech Vault on the starting ship. The vault replaces a vanilla 2×2 storage bay of the ship. A ship without one gets the vault on a free spot next to the hull, with a door to a neighbor part where the rules allow one.
65. Damage to a Tech Vault destroys tech inside it. Vault health has a step every `VaultDamagePerUnitLost` × its maximum health (default 0.11) below the maximum. Each time damage takes the health past a step, the vault loses one unit. A destroyed vault loses all units inside it.
66. Repair of a vault does not bring back lost tech. Damage after a repair crosses the steps again and costs tech again.

## 8. Tech value and selling

18. Players sell tech through the normal station trade screen. The mod adds no special sell screen.
19. Tech value = ship value × (`TechShipFraction` + `TechShipFractionPerGameHour` × game hours + `TechShipFractionPer100Notoriety` × notoriety ÷ 100), rounded to 100.
    - Ship value = the vanilla `TierValueMaximums` entry (`Data/modes/career/tier_tables.rules`) of the peak tier: the highest low tier of a system that the players were in. It is the most that a ship of that tier is worth.
    - Notoriety = the sum of every heat gain in the run (time, fame, contracts, events, debug keys). Heat losses do not lower it. The run state saves it.
    - Game time, notoriety and the peak tier never go down, so the tech value never goes down. Time and notoriety count in whole steps (`PriceStepMinutes` 1 min, `NotorietyStep` 5), so a sale in flight sees one price.
    - Defaults 0.25, 0.10 and 0.05. Purpose: 1 or 2 units pay for a ship of even power for the current stage. Examples: start (tier 1) 15,000, a quarter of a tier 1 ship. Mid-run (tier 7, 1.2 h, 250 notoriety) about 114,000 = 0.5 × 230,000, so 2 units buy a full tier 7 ship. Late (tier 12, 2.5 h, 600 notoriety) 640,000 = 0.8 × 800,000. A later sale pays more.
    - Against rewards: a standard mission pays 0.05 × the tier's ship value × the difficulty money factor × `RewardScale`. On the default Fast economy (2×) with `RewardScale` 2 that is 0.2 × ship value, so one unit mid-run is worth about 2.5 standard missions. Ship prices do not change with difficulty, so the tech value does not use the money factors.
20. The trade screen shows the current tech value as the resource price.
21. Trade stations buy tech. Stations do not sell tech. A station stocks only the resources in the `StationResourceTradeDeltas` lists of the sector rules, and tech is in none of them. Stations take sold tech into free untyped storage tiles. Players can buy back tech that they sold, at `BuyBackFactor` (default 1.5) × the tech value.
22. Tech value is computed from simulation state only, so it is the same on all multiplayer clients.

## 9. Heat

23. Heat belongs to the current sector and starts at 0 in sector 1.
24. Each simulation tick, heat increases by heat multiplier × `HeatPerGameMinute` × elapsed game minutes. Default: 2 heat per minute. Open contracts add a trail on top (requirement 101).
25. When the players earn fame, heat increases by heat multiplier × `HeatPerStandardMission` × (fame earned ÷ standard mission fame of the sector tier). Default: 15 heat for each standard mission. The standard mission fame includes the same factors as the payout: the difficulty fame factor and `RewardScale`.
    The division keeps "heat per mission" stable across tiers, so that the tunable reads as "missions per sector".
26. Heat multiplier = `HeatMultiplierByUnits[fleet tech count]`. Default table for 0 to 8 units: `[0.25, 0.4, 0.52, 0.64, 0.76, 0.88, 1.0, 1.1, 1.2]`. A count above the table uses the last entry.
27. Heat does not increase while the game is paused. Game speed changes the rate, as it does for all game time.
28. When the players enter a new sector, heat = old heat × `HeatCarryOverFraction` (default 0.25).
29. Heat stays in the range 0 to 100.

With defaults and 8 units held (no contract trail), a sector reaches the top stage after about 6 missions, or after about 40 minutes with no missions.

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

36. **Escort**: a friendly AI ship flies to a destination in the system. The players must keep it alive until it arrives. Reward on arrival. The ship is a civilian freighter next to a station; the destination is a jump point 4,500 to 6,500 away, with 2 to 3 hostile raiders (sector tier − 1) near it. The freighter moves only while a player is near it, because ships far from players are in stasis. When the mission completes, heat changes by `EscortHeatChange` (default −15): a registered convoy hides the players' trail. The offer shows this as a reward line.
37. **Fleet assault**: an allied AI fleet and the players attack a hostile station together. Reward when the station is destroyed. The station is the vanilla pirate base shape. The fleet is 2 to 3 faction military ships of one faction; the mission belongs to that faction.
38. **Shady offer**: a mission offer with a reward of `ShadyRewardFactor` × the normal money reward (default 2.0). Fame stays normal. It uses the shape of a vanilla fugitive bounty and sits in the bounty list. Tells: no faction (so no reputation reward), the high pay, and odd wording. Shady deliveries are not implemented, because a delivery needs a station or trade ship as the target.
39. When the players arrive at a shady mission location, it is a trap with chance `ShadyTrapChanceBase` + `ShadyTrapChancePerHeat` × heat. Defaults: 0.2 + 0.006 per heat point, so 80 % at heat 100. Arrival means a human ship within `TriggerDistance` (rules, default 2,500) of the target ship, or the target is destroyed. The roll uses the heat at that moment.
40. A trap spawns an ambush of 2 ships of the sector tier + 1 near the player ship nearest to the mission location (`mod/missions/shady_ambush.rules`). The ships are local-faction ships with a unique hostile player index and the hunter AI (`HotCargoHunter`). When the trap springs, a message and the wanted alert tell the players that it was a setup. A trap that the players survive pays `ShadyTrapSurvivalReward` × the normal money and fame reward (default 0.5). Survive means that all ambushers are destroyed. If the players leave the system first, the trap ends without that reward.
41. All new missions earn fame, and so they add heat through requirement 25.
42. New mission types use vanilla objectives where possible: `ProtectShips`, `DefeatShips`, `Sequential`, `AnyOrder`, `ReceiveRewards`. The game has no objective for "an AI ship reached a location" and no random objective, so the mod adds the rules types `HotCargoEscort` and `HotCargoShadyTrap` (objectives) and `HotCargoScaled` (a reward that multiplies a vanilla Money or Fame reward by a mission tunable). Allied ships are a sibling spawner group with faction allegiance; a child of a hostile station would inherit its allegiance.

Note: accepted Hot Cargo missions store the custom objectives in the save. A save with an accepted Hot Cargo mission does not load without the mod.

## 12. Hunter AI tactics

43. Hunters use new tactics. Each tactic has an on/off switch (`<Tactic>Enabled`) and a minimum stage (`<Tactic>MinStage`, a stage name or empty) in the tunables file. A minimum stage that is not a `Heat.Stages` name is a tunables error (requirement 53). With no active run, or with a tactic off, the hunter AI uses the vanilla module for that job.
44. **Kite**: ships with a longer weapon range than the target hold at `KiteRangeFraction` (default 0.9) of their range, edge to edge. A ship kites only when that distance is outside the target's longest weapon range.
45. **Pincer**: in a group of two or more on the same target, ships split and attack from sides that are `PincerAngleDegrees` (default 120°) apart, alternating left and right of the group leader. With more ships than fit in a circle at that angle, the angle shrinks to 360° ÷ group size. The leader turns the whole formation a little every 10 to 20 s.
46. **Disable**: ships aim all weapons at the nearest weapon or thruster part of the target. Default minimum stage: Hunt.
47. **Fall back**: a ship below `FallBackHealthFraction` (default 0.35) moves `FallBackDistance` (default 1,500) away from the nearest player ship and repairs. It returns at `FallBackRecoverFraction` (default 0.6) or after `FallBackMaxSeconds` (default 45 s), and cannot fall back again for the same time. Health is the cost of each part times its health fraction, summed and divided by the blueprint cost. Destroyed parts count as 0.
48. **Bait**: in a group of two or more, the ship with the most thrust per mass comes within `BaitCloseDistance` (default 150) of the nearest player ship, then pulls back to the middle of its group for `BaitPullBackSeconds` (default 12 s), and repeats. Default minimum stage: Strike.
49. Each tactic has a visible tell, so the players can read it and counter it. For example, a kiting ship keeps its distance, and a ship that falls back turns away.

## 13. Tunables, debug and live tuning

50. The C# code reads all numbers in requirements 1, 4, 7, 12, 17, 19, 23 to 48, 65, 67 to 78 and 100 to 104 from `tunables.rules`. `tunables.rules` holds only changed values. The mod writes all values and defaults to `tunables.defaults.rules` at game start.
51. The file uses the game's `.rules` format.
52. The mod watches the file. When the file changes, the mod reloads it on the next frame, without a game restart.
53. If the file has an error, the mod keeps the last valid values and shows the error in game. Errors include values that break sim logic later, for example a stage name that `Hunters.Stages` or a tactic does not match (`TunablesFile.Validate`).
54. Part, resource and mission definitions are mod `.rules` content. A change to them needs a game restart.
55. A heat panel shows heat as a bar with a mark for each stage, the heat rate (time heat plus the contract trail), the open contracts and the trail rate when any are open, the fleet tech count, the tech value, the stage, the next stage and the sector index. After a win it shows the score. With debug keys on, it also shows the heat value, the heat multiplier and the tunables hash. F8 toggles it. With `Ux.HeatPanel = false`, a text overlay shows the same values. UI switches are in UX.md.
56. When `Debug.DebugKeys = true` (default false), debug keys work: add or remove heat, set the stage, add or remove tech units (F11 / Shift+F11, one unit), and spawn the next stage.

## 14. Persistence and multiplayer

57. Heat, the stages that already started, the sector index, the run result, notoriety and the peak tier (requirement 19), the contract state (requirement 107) and the event state (requirement 76) are saved with the career save and loaded with it. Resolved: the save hook is a postfix on `CareerGameModeManager.WriteTo` (path `HotCargo`), and the load hook a postfix on its `GenericSerialReader` constructor. A save from before the mod was enabled has no Hot Cargo state. At load, a new run starts at that point of the career, with the starting Tech Vault and the run intro.
58. All logic that changes simulation state runs in the deterministic simulation tick. It uses the simulation random source, not `System.Random`.
59. The fleet tech count and the loss rule count all human players together. A loss ends the run for everyone.
60. In multiplayer, the mod does not reload tunables. The heat panel shows a short hash of the tunables file, so players can compare.
61. Debug keys do not work in multiplayer.

## 15. Mod activation and loading

62. The C# code loads through a .NET startup hook. `scripts/deploy.sh` adds `STARTUP_HOOKS` to `Cosmoteer.runtimeconfig.json` and writes `Bin/steam_appid.txt`.
63. The C# code does nothing unless the Hot Cargo rules mod is enabled in the game's mod list. With the mod off, the game stays vanilla, including vanilla multiplayer.
64. A Steam "verify files" removes the hook. Run `scripts/deploy.sh` again to restore it.

## 16. Default tunables

| Tunable | Default | Requirement |
|---|---|---|
| `RunSectorCount` | 8 | 1 |
| `FinalSectorTier` | 12 (rules value in `mod/journey/galaxy.rules`, restart needed) | 3 |
| `RewardScale` | 2.0 | 4 |
| `BuyerPriceFactor` | 4 | 7 |
| `StartingTechUnits` | 8 | 17 |
| `VaultDamagePerUnitLost` | 0.11 | 65 |
| `LossGraceSeconds` | 3 s | 12 |
| `TechShipFraction` | 0.25 | 19 |
| `TechShipFractionPerGameHour` | 0.10 | 19 |
| `TechShipFractionPer100Notoriety` | 0.05 | 19 |
| `NotorietyStep` | 5 | 19 |
| `HeatPerGameMinute` | 2 | 24 |
| `HeatPerStandardMission` | 15 | 25 |
| `HeatMultiplierByUnits` | [0.25, 0.4, 0.52, 0.64, 0.76, 0.88, 1.0, 1.1, 1.2] | 26 |
| `HeatCarryOverFraction` | 0.25 | 28 |
| `HeatStages` | see section 10 | 30 |
| `StrikeWaveIntervalSeconds` | 180 s | 30 |
| `SpawnDistanceMin`, `SpawnDistanceMax` | 2000, 3000 | 32 |
| `ShadyRewardFactor` | 2.0 | 38 |
| `ShadyTrapChanceBase` | 0.2 | 39 |
| `ShadyTrapChancePerHeat` | 0.006 | 39 |
| `ShadyTrapSurvivalReward` | 0.5 | 40 |
| `EscortHeatChange` | −15 | 36 |
| `Events.ArrivalEventChance`, `ArrivalDelaySeconds` | 0.6, 6 s | 69 |
| `Events.StationEventChance`, `StationTriggerDistance` | 0.5, 1500 | 69 |
| `Events.SignalTriggerDistance` | 900 | 69 |
| `Events.CourierChance` | 0.5 | 85 |
| `Events.MaxUnavoidablePerRun` | 1 | 87 |
| `Events.CooldownSeconds`, `MaxEventsPerSystem` | 120 s, 2 | 70 |
| `Events.MoneyScale`, `FameScale`, `HeatScale` | 1, 1, 1 | 73 |
| `Contracts.TrailFreeContracts`, `TrailHeatPerContractMinute` | 3, 1 | 101 |
| `Contracts.FeeChance`, `FeeMin`, `FeeMax`, `PaidBonusFactor` | 0.5, 0.3, 0.8, 2 | 102 |
| `Contracts.RivalPairChance` | 0.2 | 103 |
| `Contracts.InformantShare`, `InformantChanceMin`, `InformantChanceMax` | 0.75, 0.05, 0.15 | 104 |
| `Contracts.InformantDelayMin`, `InformantDelayMax`, `InformantHeat`, `InformantShips` | 45 s, 150 s, 15, 2 | 104 |
| `KiteRangeFraction` | 0.9 | 44 |
| `PincerAngleDegrees` | 120° | 45 |
| `FallBackHealthFraction` | 0.35 | 47 |

The defaults are first guesses. Live tuning sets the real values.

## 17. Architecture

```
mod/                       rules content; scripts/assemble-mod.sh builds mod.rules and strings from it
  mod.header.rules         mod header
  tunables.rules           changed tunables only (live reload)
  <area>/actions.rules     the Actions of each area
  <area>/strings.en.rules  the strings of each area
  tech/                    Stolen Tech resource, Tech Vault part
  journey/                 run map (galaxy.rules), Buyer station
  missions/                mission spawners, shady ambush
  tactics/ai_hunter.rules  hunter AI (HotCargoHunter)
  events/                  choice events (events.rules), outcome spawns (spawns.rules), signal POIs
src/HotCargo.Loader/       startup hook entry point, assembly resolve
src/HotCargo/
  Mod.cs                   Harmony setup, area registration
  Core/                    Run, RunState (save), Tunables, TunablesFile (live reload, validation), HeatModel,
                           CareerPatches (lifecycle), Activation (requirement 63), Errors, SimQueries, DebugKeys
  Tech/ Journey/ Hunters/ Tactics/ Missions/ Ux/ Events/   one folder for each area: <Area>Area.Register, tunables, patches
  Ui/                      overlay, heat panel, run intro and end panels
  Dev/Autotest.cs          headless test commands
tests/HotCargo.Tests/      fast unit tests for the pure model files
tests/autotest/            game tests that scripts/autotest.sh runs on the PC
scripts/                   pull-game, deploy, run, stop, logs, autotest, release
```

Rules:

- The model files (`HeatModel`, `TechModel`, `MissionModel`, `TacticsModel`, `EventModel`) have no game types, so that they are testable on the Mac without the game.
- Patches stay thin. They call into the mod classes.
- One tunables record type. The overlay and the code read the same values.

## 18. Plan

The work is one batch. The order inside the batch follows the dependencies.

| Step | Work | Depends on |
|---|---|---|
| 0 | Toolchain: startup hook loader, Harmony, deploy and run scripts. **Done.** | — |
| 1 | Engine research: resources and trade, career and saves, missions and AI. **Done.** | 0 |
| 2 | Core: activation check, tunables with live reload, run state, heat model, overlay, debug keys, save and load. | 1 |
| 3 | Tech: resource, Tech Vault part, starting cargo, price patch, fleet count, loss rule. | 1 |
| 4 | Run: short map, sector tiers, reward scale, Buyer station, win rule. | 1 |
| 5 | Corporate response: hunter faction, stage spawns. | 2, 3 |
| 6 | Missions: escort, fleet assault (rules), shady offer with trap roll (C#). | 2, 5 |
| 7 | Hunter AI tactics. | 5 |
| 8 | Verification and a tuning pass. **Running.** Steps 2 to 7 are done. | all |

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
- **Short career**: the map size and tier ramp come from rules only (requirement 2). A game update that changes `base_galaxy.rules` can need a matching change in `mod/journey/galaxy.rules`.
- **The startup hook loads into every Cosmoteer start.** Requirement 63 keeps the game vanilla when the rules mod is off.

## 21. Open decisions

- Strike stage: survivable waves (default) or a hard fail (requirement 35).
- Score formula: delivered units count twice, because the money already includes the Buyer payment (requirement 7).

## 22. Events

FTL-style choice events. EVENTS.md holds the design and the catalog.

67. An event is a page with a title, a text and 1 to 4 choices. Event content is in `mod/events/events.rules`. Ships that outcomes spawn are generators in `mod/events/spawns.rules`. A change to these files needs a game restart.
68. A choice has an optional sure cost, an optional need and 1 or more weighted outcomes. The window shows the cost on the button. A choice whose need or cost the fleet cannot meet is greyed out with the reason, or hidden when the rules say so. The need of a choice includes its cost.
69. Triggers (the Courier Job offer in requirement 85 is not an event and has its own roll): Start (once, `StartDelaySeconds` after a new run), Arrival (`ArrivalDelaySeconds` after the first arrival in a system, chance `ArrivalEventChance`), Signal (a player ship within `SignalTriggerDistance` of a signal POI; the POI goes away), Station (the first trade station within `StationTriggerDistance` in a system visit, chance `StationEventChance`; never the Buyer). Signals and stations wait until the Start event opened.
70. Arrival and Station events wait `CooldownSeconds` after the last triggered event. A system gets at most `MaxEventsPerSystem` Arrival and Station events. An event opens at most `MaxPerRun` times per run (rules, default 1; 0 means no limit). Only one event page waits at a time.
71. A trigger picks one eligible event by weight: `Weight + WeightPerHeat × heat + WeightPerTech × fleet tech count`, never below 0. Outcomes use the same weight rule. The outcome roll uses a seed from the simulation random source, taken when the page opens.
72. Outcome effects: money, money in tech value, fame (gain or loss), heat, reputation (requirement 81), the Buyer price (requirement 84), tech units (through the Tech Vaults), crew, vanilla resources, hull damage, spawned ships, story flags and a follow-up page. Money gains are split between the human players like a mission reward. The chooser pays money costs.
73. Event money unit: 1 = the middle of a standard mission's pay at the sector tier × the economy money reward factor × `RewardScale` × `MoneyScale`. A fame unit is a standard mission's fame × the economy fame reward factor × `RewardScale` × `FameScale`. Heat effects × `HeatScale`. Money amounts use the tier and the tech value from when the page opened.
74. A choice goes through a multiplayer input (`HotCargoEventChoice`). The input carries the page serial, so a second click on the same page (by another player) does nothing. The input checks the chooser's need again before it applies the choice.
75. After a choice, the window shows the outcome text and one result line per effect. A follow-up page shows the outcome text above its own text.
76. The event state is saved in the run state: seen counts, flags and the sector at which each flag was set, the waiting page, the last result, the cooldown, the per-system counts and the systems that already rolled a Courier Job.
77. In single player, an open event window pauses the game when `PauseOnEvent` is on. Headless test games do not pause.
78. A content error in an event (a missing spawn, an unknown resource, a bad reference) shows in the overlay at the first event and turns off events for that game. An error while an outcome applies drops the page and shows the error; the game keeps running.
79. Event-spawned ships that fight the players are never tagged `hc_hunter`, so the Hunters area does not count them toward `MaxLiveHunters` and they keep their own hostile player index. Local bounty hunters (Wanted Broadcast) use local faction ship designs, the tag `hc_ev_bounty` and a bounty mission.
80. A need can require a flag that was set at least N sectors ago (`FlagAges`). The event state saves the sector index at which each flag was last set.
81. An outcome can change reputation with the faction that controls the system, in units of a standard mission's reputation at the sector tier. A system without a controlling faction gets no change. A fame loss lowers fame and never goes below 0.
82. A choice cost can grow with heat: `Money + MoneyPerHeat × heat` money units, with the heat from when the page opened.
83. A need can bound the sectors left before the Buyer's sector (`RunSectorCount` − sector index): `MinSectorsLeft` and `MaxSectorsLeft` (−1 = no limit).
84. An outcome can multiply the run's Buyer price multiplier (`BuyerPrice`, above 0 and at most 1). The run state saves the multiplier. The Buyer sale price and the score use it (requirement 7).
85. Courier Job: the first trade station that a player ship comes near in a system rolls `CourierChance` once for the whole run. On success, a Courier Job offer appears in that station's mission list. The job: fly a sealed case to a neutral contact 8,000 to 12,000 away within 7 minutes (the contact counts as reached when a player ship sees it), then collect the pay at the station. Pay: 1.25 × a standard mission's money, 0.5 × its fame.
86. Event missions carry contract terms (economy, requirement 100 and later) as a `HotCargoContract` reward. Missions that events add straight to the log have zero terms: no fee, heat, trail or informant. The Courier Job: free to accept, flat heat 3, trail weight 0.5, informant chance 15 %.
87. An event can be in the Unavoidable category: every choice costs something, with no way to avoid it. A trigger picks at most `MaxUnavoidablePerRun` (default 1) Unavoidable events per run, counted from the seen counts. Vault Coolant Leak is the only one so far.

## 23. Contract economy

A contract is a mission offer that a client gives: every offer in the station comms missions tab and the system and galaxy lists. Code: `src/HotCargo/Contracts`. Numbers: the `Contracts` tunables section.

100. **Terms.** Each contract has terms (`ContractTerms`): accept fee, completion bonus, flat heat on accept, trail weight, rival group and informant chance. Rules terms come first: a mission definition can put `{ Type = HotCargoContract; AcceptFee; FlatHeat; TrailWeight; RivalGroup; InformantChance }` in its Rewards list, and a missing field uses the default. Then `Contracts.Metatypes` gives terms for vanilla mission types. The rest comes from a roll seeded by the mission ID, so every client gets the same terms. The sim tick saves the terms of new offers once per second. Missions of a category in `ExcludedCategories` (default: exploration) are not contracts. Per rule:
     - Hot Cargo missions: Convoy Duty leaves half a trail (0.5) and rolls the rest. Joint Strike has flat heat 5, no fee and no informant. Discreet Bounty has a fee of 0.5, flat heat 4 and no informant (its trap is its own sell-out).
     - Vanilla missions: fee, trail and informant use the roll. Pirate and faction base attacks have flat heat 5.
101. **Trail.** Open contracts (accepted, in progress, with terms) leave a trail. Heat per game minute = (sum of trail weights − `TrailFreeContracts`) × `TrailHeatPerContractMinute`, never below 0. Defaults: 3 free, 1 heat per minute for each contract above 3. The trail ignores the tech multiplier.
102. **Fees.** About half of the contracts (`FeeChance` 0.5) cost a fee. Model: purchase. The player who accepts pays the fee and does not get it back. A fee is `FeeMin` to `FeeMax` (0.3 to 0.8) standard mission pays at the mission tier, with the difficulty money factor and `RewardScale`, rounded to 500. A paid contract pays a bonus of fee × `PaidBonusFactor` (2) on completion, split between the human players like vanilla money. So a paid contract nets one fee more than a free one, and a failed one loses the fee. The bonus is a reward line on the offer (`HotCargoContractBonus`). A player with less money than the fee cannot accept: the accept button shows the fee and does not react, and the accept input refuses.
103. **Rivals.** Offers in one system with the same rival group are rivals. Accepting one dismisses the others, and a chat line names them. The scan pairs a new offer with an earlier offer of another client (another faction or another station): always when one offer targets the other's faction, otherwise with chance `RivalPairChance` (0.2). A new offer in the group of an open contract is dismissed.
104. **Informants.** `InformantShare` (75 %) of contracts carry an informant chance of 5 to 15 %. The accept rolls it with the simulation random source. On a hit, `InformantDelayMin` to `InformantDelayMax` (45 to 150) game seconds later, if the contract is still open, the client sells the players out: heat + `InformantHeat` (15) and a hunter wave of `InformantShips` (2) at the sector tier. A banner and a chat line name the client and the contract, and the mission log shows "<client> sold you out on this contract".
105. **Smaller systems.** For shorter in-system travel, gameplay spawns move in to about 70 % of the vanilla radius: stations (2 to 3 per system, was 2 to 4), military stations, bounties, wanderers and pirates, asteroid fields and belts, discoverables, beacons and the jump gates. Counts drop with the area. Fewer missions: 2 to 4 bounties (was as many as fit), 1 delivery per station (was 2), Convoy Duty 45 % (was 60 %), Joint Strike 30 % (was 40 %), one Discreet Bounty at 60 % (was 1 to 2 at 70 %). Scenery (suns, planets, nebulas, fog) keeps the vanilla size. These are rules in `mod/contracts/actions.rules` and `mod/missions/sysgen_hotcargo.rules`; they change the shared sector rules, so they apply to every map size while the mod is on, and need a game restart.
106. **Contract UI.** The mission info panel (comms missions tab and mission log) has a "Contract terms" section under the rewards. Before the accept it shows the fee (and the player's money when it is too low), the flat heat, the trail ("counts as N; open X → Y; trail +a/min → +b/min"), the rival offer that the accept dismisses, and the informant risk or "Discreet client". After the accept it shows the fee paid, the open count and trail rate, the informant risk, and a sell-out. The accept button reads "Accept (pay N)". Accepting and paying go through the game's own accept input (`MPAcceptMissionInput`), so they are deterministic in multiplayer.
107. The run state saves the terms, the pending sell-outs and the sold-out contracts. Accepted contracts with a bonus keep a `HotCargoContractBonus` reward, so like other Hot Cargo missions such a save needs the mod.
108. After a win, the contract economy stops: no fees, no flat heat, no trail and no sell-outs.


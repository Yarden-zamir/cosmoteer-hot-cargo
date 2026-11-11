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
7. When the players win, a win panel shows the score. Score = money + delivered units × tech value × `BuyerPriceFactor` (default 1.5) × the run's Buyer price multiplier (requirement 84; 1 unless the players took an advance). A sale to the Buyer pays tech value × `BuyerPriceFactor` × that multiplier for each unit. Delivered units are the units of all crew deliveries to the Buyer; a delivery after the win raises the score again. The run state saves the score and the delivered units. **Open decision:** the money already includes the Buyer payment, so delivered units count twice. `Buyer.Score` holds the formula.
8. After a win, heat turns off. The win panel shows the score and the run numbers, and the players can continue the career.
9. **Loss**: when the fleet tech count reaches 0, the run is lost. Any cause counts: vault damage, a destroyed vault, crew that die while they carry tech, or a sale of the last unit. A Buyer delivery is the only exception, because it wins the run first.
10. A loss shows the game-over screen with the title "The corporation recovered the tech", the reason and the run numbers.
11. The mod does not delete save files after a loss.
12. The loss check runs only when the count is certain: no ship jump is in progress, and no friendly ships wait in another system. The count must stay at 0 for `LossGraceSeconds` (default 3 s), so tech that crew carry between ships does not trigger a loss. The run state saves the time at 0, so a load or a client that joins sees the same timer.

## 7. Tech units and the Tech Vault

13. Tech is a new resource: "Stolen Tech". It uses the normal resource systems: storage, crew carrying, transfer between ships, and trade.
14. Only the Tech Vault part stores tech.
15. The Tech Vault is a buildable part. Its rules define its size, health, cost and capacity. Defaults: 2×2, 12,000 health (3 × the vanilla storage bay), 96 steel, capacity 6 units (2 tiles × stack size 3). Rules changes need a game restart.
16. To move tech between ships, the players build a vault on the other ship. The crew then carry the tech over, like any other resource.
17. A new run starts with `StartingTechUnits` (default 6) in a Tech Vault on the starting ship. The vault replaces a vanilla 2×2 storage bay of the ship. A ship without one gets the vault on a free spot next to the hull, with a door to a neighbor part where the rules allow one.
65. Damage to a Tech Vault destroys tech inside it. Vault health has a step every `VaultDamagePerUnitLost` × its maximum health (default 0.15) below the maximum. Each time damage takes the health past a step, the vault loses one unit. A destroyed vault loses all units inside it.
66. Repair of a vault does not bring back lost tech. Damage after a repair crosses the steps again and costs tech again.

## 8. Tech value and selling

18. Players sell tech through the normal station trade screen. The mod adds no special sell screen.
19. Tech value = `TechBaseValue` × (1 + `TechGrowthPerSector` × (sector index − 1) + `TechGrowthPerGameHour` × game hours).
    Defaults: base 10,000, +35 % for each sector, +10 % for each game hour.
20. The trade screen shows the current tech value as the resource price.
21. Trade stations buy tech. Stations do not sell tech. A station stocks only the resources in the `StationResourceTradeDeltas` lists of the sector rules, and tech is in none of them. Stations take sold tech into free untyped storage tiles. Players can buy back tech that they sold, at `BuyBackFactor` (default 1.5) × the tech value.
22. Tech value is computed from simulation state only, so it is the same on all multiplayer clients.

## 9. Heat

23. Heat belongs to the current sector and starts at 0 in sector 1.
24. Each simulation tick, heat increases by heat multiplier × `HeatPerGameMinute` × elapsed game minutes. Default: 2 heat per minute.
25. When the players earn fame, heat increases by heat multiplier × `HeatPerStandardMission` × (fame earned ÷ standard mission fame of the sector tier). Default: 15 heat for each standard mission. The standard mission fame includes the same factors as the payout: the difficulty fame factor and `RewardScale`.
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
| Strike | 100 | A strike force: 2 ships of the sector tier + 1 in sector 1, up to 3 ships of tier + 2 in the last sector (requirement 190). The ships arrive one at a time, 45 s apart (requirement 191). A new wave every `StrikeWaveIntervalSeconds` (default 180 s) while heat stays at 100. |

31. Each stage starts once for each sector visit. The Strike stage repeats its waves. A stage response can arrive in groups (requirement 191). A group spawns only enough ships to reach `MaxLiveHunters` (default 12) live hunters in the system.
32. Hunters spawn at a distance of `SpawnDistanceMin` to `SpawnDistanceMax` (default 2,000 to 3,000) from the center of the player fleet. All ships of one wave come from one direction, spread over `WaveArcDegrees` (default 40°). They always know where the players are, move to them and attack. Hunters far from the players move toward them in stasis too.
33. Hunters are a corporate force, not a rules faction. They use built-in ships with the `ShipTags` tag (default `combat`) from the `ShipFactions` pools (default: all factions), at the wanted tier or the nearest lower tier. Their names start with `ShipNamePrefix` (default "Corporate"). All hunters in a system share one hostile player index, so they are allies of each other and enemies of the players. Like vanilla ambushes, they are also enemies of faction ships. Every hunter carries FTL fuel (requirement 192). Hunters never surrender and never accept surrender. Resolved: a rules faction needs an icon, two player indices and its own ship pool, and the faction reputation systems would treat it as a normal faction. A unique player index gives the same result at much lower cost.
34. Hunters prefer to attack ships that carry Tech Vaults. A ship with a Tech Vault part counts `VaultTargetFactor` times more (default 3) when a hunter picks the nearest target.
35. The Strike stage is dangerous but survivable. Its ships arrive in groups, so players who can fight have time to destroy a hunter, take its fuel and jump away (requirements 191 and 192). It does not end the run directly. Only a loss under requirement 9 ends the run. **Open decision:** the user may choose a hard fail instead.

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

43. Hunters use new tactics. Each tactic has an on/off switch (`<Tactic>Enabled`) and a minimum stage (`<Tactic>MinStage`, a stage name or empty) in the tunables file. A minimum stage that is not a `Heat.Stages` name is a tunables error (requirement 53). With no active run, or with a tactic off, the hunter AI uses the vanilla module for that job. Requirements 193 to 202 add enemy AI behaviors (flee, calls for help, focus fire, regroup) for hunters and for the vanilla combat AIs.
44. **Kite**: ships with a longer weapon range than the target hold at `KiteRangeFraction` (default 0.9) of their range, edge to edge. A ship kites only when that distance is outside the target's longest weapon range.
45. **Pincer**: in a group of two or more on the same target, ships split and attack from sides that are `PincerAngleDegrees` (default 120°) apart, alternating left and right of the group leader. With more ships than fit in a circle at that angle, the angle shrinks to 360° ÷ group size. The leader turns the whole formation a little every 10 to 20 s.
46. **Disable**: ships aim all weapons at the nearest weapon or thruster part of the target. Default minimum stage: Hunt.
47. **Fall back**: a ship below `FallBackHealthFraction` (default 0.35) moves `FallBackDistance` (default 1,500) away from the nearest player ship and repairs. It returns at `FallBackRecoverFraction` (default 0.6) or after `FallBackMaxSeconds` (default 45 s), and cannot fall back again for the same time. Health is the cost of each part times its health fraction, summed and divided by the blueprint cost. Destroyed parts count as 0. A ship that flees (requirement 193) does not fall back: flee has the higher priority.
48. **Bait**: in a group of two or more, the ship with the most thrust per mass comes within `BaitCloseDistance` (default 150) of the nearest player ship, then pulls back to the middle of its group for `BaitPullBackSeconds` (default 12 s), and repeats. Default minimum stage: Strike.
49. Each tactic has a visible tell, so the players can read it and counter it. For example, a kiting ship keeps its distance, and a ship that falls back turns away. The behaviors of requirements 193 to 202 have tells too: a ring and a label for a call, a label and a jump countdown for a fleeing ship, a label for a ship that regroups.

## 13. Tunables, debug and live tuning

50. The C# code reads all numbers in requirements 1, 4, 7, 12, 17, 19, 23 to 48, 65, 67 to 78, 160 to 175 and 190 to 202 from `tunables.rules`. `tunables.rules` holds only changed values. The mod writes all values and defaults to `tunables.defaults.rules` at game start.
51. The file uses the game's `.rules` format.
52. The mod watches the file. When the file changes, the mod reloads it on the next frame, without a game restart.
53. If the file has an error, the mod keeps the last valid values and shows the error in game. Errors include values that break sim logic later, for example a stage name that `Hunters.Stages` or a tactic does not match (`TunablesFile.Validate`).
54. Part, resource and mission definitions are mod `.rules` content. A change to them needs a game restart.
55. A heat panel shows heat as a bar with a mark for each stage, the heat rate, the fleet tech count, the tech value, the stage, the next stage and the sector index. After a win it shows the score. With debug keys on, it also shows the heat value, the heat multiplier and the tunables hash. F8 toggles it. With `Ux.HeatPanel = false`, a text overlay shows the same values. UI switches are in UX.md.
56. When `Debug.DebugKeys = true` (default false), debug keys work: add or remove heat, set the stage, add or remove tech units (F11 / Shift+F11, one unit), and spawn the next stage.

## 14. Persistence and multiplayer

57. Heat, the stages that already started, the sector index, the run result, the event state (requirement 76) and the chain state (requirement 172) are saved with the career save and loaded with it. Resolved: the save hook is a postfix on `CareerGameModeManager.WriteTo` (path `HotCargo`), and the load hook a postfix on its `GenericSerialReader` constructor. A save from before the mod was enabled has no Hot Cargo state. At load, a new run starts at that point of the career, with the starting Tech Vault and the run intro.
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
| `EscortHeatChange` | −15 | 36 |
| `Events.ArrivalEventChance`, `ArrivalDelaySeconds` | 0.6, 6 s | 69 |
| `Events.StationEventChance`, `StationTriggerDistance` | 0.5, 1500 | 69 |
| `Events.SignalTriggerDistance` | 900 | 69 |
| `Events.CourierChance` | 0.5 | 85 |
| `Events.MaxUnavoidablePerRun` | 1 | 87 |
| `Events.CooldownSeconds`, `MaxEventsPerSystem` | 120 s, 2 | 70 |
| `Events.MoneyScale`, `FameScale`, `HeatScale` | 1, 1, 1 | 73 |
| `Chains.StationChance` | 1 | 160 |
| `Chains.PageTriggerDistance`, `LaterDelaySeconds` | 1500, 8 s | 165 |
| `Chains.StoryLines` | 2 | 170 |
| `KiteRangeFraction` | 0.9 | 44 |
| `PincerAngleDegrees` | 120° | 45 |
| `FallBackHealthFraction` | 0.35 | 47 |
| `Hunters.Stages` Strike `EarlyScale`, `EarlyTierReduction` | 0.67, 1 | 190 |
| `Hunters.Stages` Strike `Waves`, `WaveGapSeconds` | 3, 45 s | 191 |
| `Hunters.MinFuel` | 40 | 192 |
| `FleeHealthFraction`, `FleeChance` | 0.25, 0.5 | 193 |
| `FleeDistance`, `FleeJumpSeconds`, `FleeRecoverFraction` | 1500, 25 s, 0.6 | 195 |
| `CallSeconds`, `CallRange`, `CallCooldownSeconds` | 6 s, 6000, 60 s | 196, 197 |
| `CallAssistSeconds` | 60 s | 198 |
| `CallBlockedInNebulas`, `CallStormStrength` | [ion_storm, electron], 0.5 | 199 |
| `FocusWeakFactor` | 1.5 | 200 |
| `RegroupFirepowerRatio`, `RegroupRadius`, `RegroupMinGroup`, `RegroupMaxSeconds` | 0.75, 1200, 2, 25 s | 201 |

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
  tactics/ai_vanilla.rules enemy AI modules that actions.rules appends to the vanilla combat AIs
  events/                  choice events (events.rules), outcome spawns (spawns.rules), signal POIs
  chains/                  story chains (chains.rules), stage missions (missions.rules), chain pages (pages.rules)
src/HotCargo.Loader/       startup hook entry point, assembly resolve
src/HotCargo/
  Mod.cs                   Harmony setup, area registration
  Core/                    Run, RunState (save), Tunables, TunablesFile (live reload, validation), HeatModel,
                           CareerPatches (lifecycle), Activation (requirement 63), Errors, SimQueries, DebugKeys
  Tech/ Journey/ Hunters/ Tactics/ Missions/ Ux/ Events/ Chains/   one folder for each area: <Area>Area.Register, tunables, patches
  Ai/                      enemy AI behaviors (flee, call, regroup, focus); TacticsArea registers it, Tactics tunables
  Ui/                      overlay, heat panel, run intro and end panels
  Dev/Autotest.cs          headless test commands
tests/HotCargo.Tests/      fast unit tests for the pure model files
tests/autotest/            game tests that scripts/autotest.sh runs on the PC
scripts/                   pull-game, deploy, run, stop, logs, autotest, release
```

Rules:

- The model files (`HeatModel`, `TechModel`, `MissionModel`, `TacticsModel`, `EventModel`, `WaveModel`, `AiModel`) have no game types, so that they are testable on the Mac without the game.
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

67. An event is a page with a title, a text and 1 to 4 choices. Event content is in `mod/events/events.rules`. Chain pages are events in `mod/chains/pages.rules` (requirement 167). Ships that outcomes spawn are generators in `mod/events/spawns.rules`. A change to these files needs a game restart.
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
88. Dead in Space: a broken-down freighter needs repair supplies or a tow. It opens as an Arrival event or as a follow-up of the Distress Beacon. The tow choice needs a Tractor Beam Emitter and starts a Tow Job mission: the freighter has no AI, and the mission completes when it is within 1,200 of the nearest trade station at least 3,000 from it (`HotCargoTow` objective). It fails when the freighter is destroyed.
89. Event texts show no counts that live elsewhere (starting tech, spawn counts). The token `{crates}` shows the live fleet tech count. The choice buttons have no number prefix; the UX area draws the key numbers.

## 23. Strike waves and enemy AI

Tunables: `Hunters` (requirements 190 to 192) and `Tactics` (requirements 193 to 202). Pure math: `Hunters/WaveModel.cs`, `Ai/AiModel.cs`.

190. **Stage scaling.** A stage response sends `ShipCount` ships of the sector tier + `TierOffset` in the last sector of the run. In sector 1 it sends `ShipCount` × `EarlyScale` ships (rounded, at least 1) of the sector tier + `TierOffset` − `EarlyTierReduction`. Both values rise linearly with the sector index. By default only Strike scales: 2 ships of tier + 1 in sector 1, 3 ships of tier + 2 in the last sector.
191. **Waves.** A stage response arrives in `Waves` groups, `WaveGapSeconds` apart. Defaults: Strike 3 groups 45 s apart (one ship at a time), other stages 1 group. The first group arrives when the stage starts, with the stage alert. Each later group plays the alert sound with the "more ships" text. The repeat Strike waves of requirement 30 split the same way. The run state saves the waiting groups. A jump to another system drops them: a jump is how the players escape a strike.
192. **Hunter fuel.** Every hunter carries at least `MinFuel` (default 40) hyperium, the FTL fuel resource. Built-in ships spawn with full hyperdrive tanks. A hunter with less fuel gets the rest in any storage that takes hyperium. Some built-in ships have no hyperdrive and no such storage; the run state holds the fuel that does not fit, and it drops with the same roll when the hunter is destroyed or becomes derelict. Vanilla drop rates apply when it dies: hyperdrive tanks drop their fuel when destroyed (`DropResourcesWhenDestroyed`), each unit with the hyperium `StorageDestroyed` chance (50 %), and crew salvage recovers all of it (`StorageSalvaged` 100 %). Hunters never surrender (requirement 33), so death and salvage are the ways to get their fuel. A hunter that jumps out (requirement 195) takes its fuel with it.

The enemy AI behaviors are the rules modules `HcFlee`, `HcCall` and `HcRegroup` and a patch on `SimModeManager.ModifyAIAttackValue`. The modules are in the hunter AI (`mod/tactics/ai_hunter.rules`) and at the end of the strategy modules of the vanilla combat AIs Normal, Easy, Wanderer, StationGuard and Police (`mod/tactics/ai_vanilla.rules`). Priority, highest first: flee, call (answering ally), then for hunters fall back, bait and regroup, and for vanilla AIs regroup, then the vanilla modules.

193. **Flee on damage.** A ship below `FleeHealthFraction` (default 0.25) health rolls once against `FleeChance` (default 0.5). On success it flees. It rolls again only after its health is back at or above `FleeHealthFraction`. Health is as in requirement 47.
194. **Flee unarmed.** A ship without an offensive weapon part always flees. Point defense and other defensive-only weapons do not count.
195. **Fleeing.** A fleeing ship keeps at least `FleeDistance` (default 1,500) edge to edge from the nearest enemy on its team radar. A corporate hunter (tag `hc_hunter`) jumps out of the system `FleeJumpSeconds` (default 25 s) after it starts to flee, with the vanilla FTL effect; it leaves no wreck. Any other ship repairs when it is clear of enemies and fights again at `FleeRecoverFraction` (default 0.6) health if it has weapons. Other ships never leave the system, so mission targets stay. This includes the shady trap and event ambushers, which run the hunter AI. A ship that surrendered to the players keeps its vanilla behavior (all behaviors of this section).
196. **Call trigger.** A ship calls for help when a human enemy ship first comes onto its team radar, or when it loses `CallDamageTrigger` (default 0.02) health between two AI checks while a human enemy is on radar. It calls only when someone can answer: for a corporate hunter, a hunter group waits to arrive (requirement 191); for any other ship, an ally within `CallRange` (default 6,000) has the call module and does not call or answer already. Allies are ships of the same team: the same faction, or the same hunter or pirate group. Only one ship of a team within `CallRange` calls at a time. After a call, a stopped call or an answer, the ship waits `CallCooldownSeconds` (default 60 s) before it calls again.
197. **Call tells and interrupt.** A call takes `CallSeconds` (default 6 s). The caller keeps fighting. During the call, a red ring around the ship grows to a fixed outer ring, and the ship marker shows "CALLING FOR HELP n%". The log shows a line when a call starts, completes or stops, for ships on the local player's radar. One hit that drains battery power stops the call: disruptor bolts, the disruptor drain status and EMP missiles (factory_emp makes their parts). Damage from a part in `CallInterruptParts` (default ion beam emitter and ion beam prism) stops it too, because ion beams drain no battery power.
198. **Call result.** A completed corporate hunter call brings the next waiting hunter group at once. Any other completed call makes each listener hunt the caller's target for `CallAssistSeconds` (default 60 s): it attacks the target, or flies to it while the target is off its radar. A listener that fights another target keeps that fight.
199. **Storms.** A ship inside a nebula of a `CallBlockedInNebulas` type (default ion storm and electron nebula) at `CallStormStrength` (default 0.5) or more cannot call, and a listener there does not hear. A storm stops a running call. An unknown nebula type in the list is an error that shows in the overlay.
200. **Focus fire.** The attack value of a target is multiplied by 1 + `FocusWeakFactor` × its lost health fraction (default factor 1.5). Hunters use it in their target choice, together with the vault factor of requirement 34. Vanilla AIs use it through `SimModeManager.ModifyAIAttackValue`. Tell: the enemies turn on a badly hurt ship.
201. **Regroup.** A ship with less than `RegroupFirepowerRatio` (default 0.75) of its target's firepower (the vanilla AI firepower rating) does not charge alone. While fewer than `RegroupMinGroup` (default 2) ships of its team, itself included, are within `RegroupRadius` (default 1,200), and some allies within `RegroupSearchRange` (default 5,000) are further away, it flies to the nearest of them. It attacks after `RegroupMaxSeconds` (default 25 s) at the latest, and does not regroup again for the same time. The ship marker shows "REGROUPING".
202. **Switches and determinism.** Each behavior has two switches in `Tactics`: `<Behavior>Enabled` for hunters and `<Behavior>OnVanilla` for the vanilla AIs (`Flee`, `Call`, `Focus`, `Regroup`). With no active run, all behaviors are off and the AIs stay vanilla. All decisions run in the AI tick or in hit effects inside the sim tick, use the ship's sim random source, and walk the ships in sim order. Module state is saved with the ship and is part of the integrity hash. The ring, labels and log lines change no sim state.

## 24. Chains

Story chains: missions in parts, one chain per station. CHAINS.md holds the design and the catalog.

160. When the players first enter a system, each trade or military station gets one chain, with chance `StationChance`. The station must give missions, belong to a faction and not be an enemy of the players. The Buyer gets none.
161. A chain is a graph of 3 to 5 stages in `mod/chains/chains.rules`. The pick is weighted among the chains whose sector range and station kinds fit. Only the chains that the run used least often take part, so no chain repeats while an unused one fits. The pick uses the simulation random source.
162. A stage is a mission (a generator in `mod/chains/missions.rules`) or a page (an event in `mod/chains/pages.rules`). The first stage is always a mission offer at the station, so the players choose to start a chain.
163. A Station mission stage shows its offer in the station's comms. A declined or expired offer comes back on the next visit to the system. Return and Arrival missions go straight into the mission log, and the event window shows their title and briefing.
164. Each Station stage mission carries the contract terms of its stage (`Terms`, a `HotCargoContract`, economy requirement 100 and later). Return and Arrival missions go straight into the log and carry zero terms, like event missions (requirement 86).
165. A Station page stage opens when a player ship comes within `PageTriggerDistance` of the station. A Return stage starts `LaterDelaySeconds` after the players enter the station's system again, after they left it. An Arrival stage starts `LaterDelaySeconds` after the first arrival in a new system, `AfterSectors` sectors after the stage began. `AfterSectors = 0` starts it in the current system.
166. A stage mission gives one result when it leaves the mission log: `success` (completed), `timeout` (a time limit ran out), `lost` (a protected ship died), `abandoned` (abandoned in the log) or `fail`.
167. A page gives the `ChainResult` of the outcome that the players got. Every page outcome has a `ChainResult` or a follow-up page. Choices go through the event choice MP input (requirement 74).
168. The stage's result rows handle the result. The first row whose `On` matches and whose chain flag conditions hold wins. `timeout`, `lost` and `abandoned` fall back to `fail`, and every result falls back to `any`.
169. A result row can apply event outcome effects (requirement 72, same units as requirement 73), reputation with the station's faction (in standard mission reputation at the sector tier, like requirement 81), chain flags and a story line. Then the chain moves to the row's `Next` stage. An empty `Next` ends the chain.
170. Comms names a stage offer "[Chain] <chain title>: <stage title>" with "[Chain]" in the event heading color. Its description starts with the last `StoryLines` story lines. Its reward list has a "Story chain, part N" line. Chain stage missions have their own mission category, "Story Chains", so the comms list and the mission log group them.
171. A result shows in the event window. A page result joins that page's result window. A result before a page that opens at once shows above that page. A mission result waits until no event page is open.
172. The chain state is saved in the run state: each station's chain, stage, flags, story, stage mission, waiting page and timers, and how often the run used each chain.
173. Chain logic changes the simulation only from the sim tick, the system switch, the mission log's removal event and the event choice MP input.
174. A content error in a chain (a bad graph, a missing mission or page, an unhandled result) shows in the overlay at the first chain check and turns off chains for that game.
175. A stage mission that cannot spawn in a system (a placement or tag search failed) tries again on the next visit.

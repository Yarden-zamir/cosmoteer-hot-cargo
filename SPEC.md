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

1. A run has `RunSectorCount` sectors (default 8). The Buyer appears when the players first enter a system with sector index `RunSectorCount` or more, once per run. So a `RunSectorCount` lowered mid-run below the current sector gives a Buyer in the next new system. A mission in the log points to it, and a message shows it.
2. The career map is small enough that a direct route crosses all sectors in about `RunSectorCount` sector jumps. The mod adds the map size "Hot Cargo run" (`mod/journey/galaxy.rules`, 50 systems, about 7 jumps from the start to the far edge). It is first in the map size list, so it is the default while the mod is on. The player can still choose Standard. These are rules values: a change needs a game restart and applies to new careers only.
3. The danger tier of a sector rises with the sector index. Sector 1 is tier 1. The last sector is about tier `FinalSectorTier` (default 12). On the main route the tier rises by 2 for each jump. `FinalSectorTier` is a rules value in `mod/journey/galaxy.rules`, not a live tunable. Side systems keep the vanilla faction tiers, but no system has a low tier above start tier + 2 × its jumps from the start (rules type `HotCargoTierCap`). So a side system is never more dangerous than the main route at the same distance. Faction home systems (low tier above `FinalSectorTier`) keep their tier.
4. Fame and money rewards are scaled by `RewardScale` (default 2.0), so that ship growth keeps up with the shorter run. The scale multiplies the difficulty reward factors. So it applies to mission rewards, fame for defeated ships and the reward text in the UI.
5. The new career screen keeps the vanilla difficulty options. The mod does not add its own difficulty screen. It adds one row: the co-op enemy scaling slider (requirement 330).

## 6. Win and loss

6. **Win**: the players sell at least one tech unit to the Buyer. The run is won when the crew deliver the first units of the sale.
7. When the players win, a win panel shows the score. Score = money + units still held × the Buyer unit price. The Buyer unit price = tech value × `BuyerPriceFactor` (default 4) × the run's Buyer price multiplier (requirement 84; 1 unless the players took an advance). A sale to the Buyer pays that price for each unit, so delivered units are in the money once, and an unsold unit counts as much as a sold one. The players go to the Buyer instead of a station because of this factor. Delivered units are the units of all crew deliveries to the Buyer; a delivery after the win sets the score again. The run state saves the score and the delivered units. `Buyer.Score` holds the formula.
8. After a win, heat turns off. The win panel shows the score and the run numbers, and the players can continue the career.
9. **Loss**: when the fleet tech count reaches 0, the run is lost. Any cause counts: vault damage, a destroyed vault, crew that die while they carry tech, or a sale of the last unit. A Buyer delivery is the only exception, because it wins the run first.
10. A loss shows the game-over screen with the title "The corporation recovered the tech", the reason and the run numbers.
11. The mod does not delete save files after a loss.
12. The loss check runs only when the count is certain: no ship jump is in progress (no ship in jump limbo; the local jump animation does not count, because it differs between multiplayer machines), and no friendly ships wait in another system. The count must stay at 0 for `LossGraceSeconds` (default 3 s), so tech that crew carry between ships does not trigger a loss. The run state saves the time at 0, so a load or a client that joins sees the same timer.

## 7. Tech units and the Tech Vault

13. Tech is a new resource: "Stolen Tech". It uses the normal resource systems: storage, crew carrying, transfer between ships, and trade.
14. Only the Tech Vault part stores tech.
15. The Tech Vault is a buildable part. Its rules define its size, health, cost and capacity. Defaults: 1×1, 3,000 health (3 × a vanilla storage tile, below 1×1 armor at 4,000), 24 steel, capacity 2 units (1 tile × stack size 2). Rules changes need a game restart. There are no vault tiers (requirement 440).
16. To move tech between ships, the players build a vault on the other ship. The crew then carry the tech over, like any other resource. On one ship, the vanilla move resources tool moves tech between vaults.
17. A new run starts with `StartingTechUnits` (default 8) in Tech Vaults on the starting ship: as many vaults as the units need (4 for 8 units). In co-op each player commands only their own ships, so the first ship of each player gets vaults for an equal share of the units (8 = 4 + 4 for two players, 2 vaults each). Requirement 441 gives the placement.
65. Damage to a Tech Vault destroys tech inside it. Vault health has a step every `VaultDamagePerUnitLost` × its maximum health (default 0.5) below the maximum. Each time damage takes the health past a step, the vault loses one unit. A destroyed vault loses all units inside it. With the defaults a vault loses 1 of its 2 units at half health and the other when it is destroyed (requirement 442).
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
| Strike | 100 | A strike force: 2 ships of the sector tier + 1 in sector 1, up to 3 ships of tier + 2 in the last sector (requirement 190). The ships arrive one at a time, 45 s apart (requirement 191). A new wave every `StrikeWaveIntervalSeconds` (default 180 s) while heat stays at 100. |

31. Each stage starts once for each sector visit. The Strike stage repeats its waves. A stage response can arrive in groups (requirement 191). A group spawns only enough ships to reach `MaxLiveHunters` (default 12) live hunters in the system. In co-op the group and the cap scale (requirement 334).
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

50. The C# code reads all numbers in requirements 1, 4, 7, 12, 17, 19, 23 to 48, 65, 67 to 78, 100 to 104, 130 to 141, 160 to 175, 190 to 202 and 221 to 225 from `tunables.rules`. `tunables.rules` holds only changed values. The mod writes all values and defaults to `tunables.defaults.rules` at game start.
51. The file uses the game's `.rules` format.
52. The mod watches the file. When the file changes, the mod reloads it on the next frame, without a game restart. The settings menu (requirement 500) writes the same file.
53. If the file has an error, the mod keeps the last valid values and shows the error in game. Errors include values that break sim logic later, for example a stage name that `Hunters.Stages` or a tactic does not match (`TunablesFile.Validate`).
54. Part, resource and mission definitions are mod `.rules` content. A change to them needs a game restart.
55. A heat panel shows heat as a bar with a mark for each stage, the heat rate (time heat plus the contract trail), the open contracts and the trail rate when any are open, the fleet tech count, the tech value, the stage, the next stage and the sector index. After a win it shows the score. With debug keys on, it also shows the heat value, the heat multiplier and the tunables hash. F8 toggles it. With `Ux.HeatPanel = false`, a text overlay shows the same values. UI switches are in UX.md.
56. When `Debug.DebugKeys = true` (default false), debug keys work: add or remove heat, set the stage, add or remove tech units (F11 / Shift+F11, one unit), and spawn the next stage.

## 14. Persistence and multiplayer

57. Heat, the stages that already started, the sector index, the run result, notoriety and the peak tier (requirement 19), the event state (requirement 76), the chain state (requirement 172), the contract state (requirement 107) and the enemy scaling setting (requirement 330) are saved with the career save and loaded with it. Resolved: the save hook is a postfix on `CareerGameModeManager.WriteTo` (path `HotCargo`), and the load hook a postfix on its `GenericSerialReader` constructor. A save from before the mod was enabled has no Hot Cargo state. At load, a new run starts at that point of the career, with the starting Tech Vault and the run intro.
58. All logic that changes simulation state runs in the deterministic simulation tick. It uses the simulation random source, not `System.Random`. The integrity hash includes heat, sector, stages, result, fleet tech, delivered units, run time, the loss timer, the event serials, notoriety, the peak tier, the contract counts and the tunables hash, so a divergence starts a resync at once. Each desync that the game detects writes an `MP DESYNC` line to the Hot Cargo log.
59. The fleet tech count and the loss rule count all human players together. A loss ends the run for everyone.
60. In multiplayer, the mod does not reload tunables. A new co-op run starts with the default tunables on every machine. On its first frame the host sends its `tunables.rules` as a multiplayer input (`HotCargoTunables`), and every machine applies it at the same tick. The starting vault and the first system's offers exist before that and use the defaults. A loaded game uses the tunables in the save. The client's file has no effect. Changes in the host's settings menu use the same input (requirement 504). The heat panel shows a short hash of the tunables in effect, so players can compare.
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
| `VaultDamagePerUnitLost` | 0.5 | 65, 442 |
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
| `Events.WantedWarningMinStage`, `WantedEventChance` | 1, 0.7 | 470, 471 |
| `Events.CooldownSeconds`, `MaxEventsPerSystem` | 120 s, 2 | 70 |
| `Events.MoneyScale`, `FameScale`, `HeatScale` | 1, 1, 1 | 73 |
| `Contracts.TrailFreeContracts`, `TrailHeatPerContractMinute` | 3, 1 | 101 |
| `Contracts.FeeChance`, `FeeMin`, `FeeMax`, `PaidBonusFactor` | 0.5, 0.3, 0.8, 2 | 102 |
| `Contracts.RivalPairChance` | 0.2 | 103 |
| `Contracts.InformantShare`, `InformantChanceMin`, `InformantChanceMax` | 0.75, 0.05, 0.15 | 104 |
| `Contracts.InformantDelayMin`, `InformantDelayMax`, `InformantHeat`, `InformantShips` | 45 s, 150 s, 15, 2 | 104 |
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
| `Mechanics.<Mission>Chance` | 0.15 to 0.25 | 131 |
| `Mechanics.Siege`, `Guard`, `Scan` (hold-out profiles) | see MISSIONS.md | 132, 137, 140 |
| `Mechanics.DeliveryRadius` | 800 | 134 |
| `Mechanics.SurveyScanSeconds` | 15 s | 136 |
| `Mechanics.ScanRange`, `IntensivePayBonus`, `MaxIntensivePerWave` | 3500, 0.25, 2 | 140 |
| Archetype weights | rules values in `mod/worlds/archetypes.rules`, restart needed | 220 |
| `Worlds.CurrentFlowSpeed`, `CurrentAccel`, `CurrentArrows` | 45, 9, on | 221 |
| `Worlds.PassHoldSeconds`, `PassLostSeconds`, `PassWaveSeconds`, `PassWaveDistance` | 120 s, 15 s, 35 s, 2500 | 222 |
| `Worlds.SundiveSalvageSeconds`, `HeatWarnDepth` | 5 s, 0.5 | 223 |
| `Worlds.LairDiscoverDistance` | 2000 | 224 |
| `Worlds.ArrivalLine`, `ArrivalLineDelaySeconds` | on, 4 s | 225 |

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
  missions/mechanics/      mechanics missions (sysgen_mechanics.rules), Helion Ore, Prototype Core
  tactics/ai_hunter.rules  hunter AI (HotCargoHunter)
  tactics/ai_vanilla.rules enemy AI modules that actions.rules appends to the vanilla combat AIs
  events/                  choice events (events.rules), outcome spawns (spawns.rules), signal POIs
  chains/                  story chains (chains.rules), stage missions (missions.rules), chain pages (pages.rules)
  worlds/                  system archetypes: sector types, run map weights, archetype spawners, current nebula, wave spawns
  scaling/                 co-op enemy scaling: the fleet opt-in tag on the vanilla ambush, slider strings
src/HotCargo.Loader/       startup hook entry point, assembly resolve
src/HotCargo/
  Mod.cs                   Harmony setup, area registration
  Core/                    Run, RunState (save), Tunables, TunablesFile (live reload, validation), HeatModel,
                           CareerPatches (lifecycle), Activation (requirement 63), Errors, SimQueries, DebugKeys
  Tech/ Journey/ Hunters/ Tactics/ Missions/ Ux/ Events/ Chains/ Worlds/ Scaling/   one folder for each area: <Area>Area.Register, tunables, patches
  Ai/                      enemy AI behaviors (flee, call, regroup, focus); TacticsArea registers it, Tactics tunables
  Ui/                      overlay, heat panel, run intro and end panels
  Dev/Autotest.cs          headless test commands
tests/HotCargo.Tests/      fast unit tests for the pure model files
tests/autotest/            game tests that scripts/autotest.sh runs on the PC
scripts/                   pull-game, deploy, run, stop, logs, autotest, release
```

Rules:

- The model files (`HeatModel`, `TechModel`, `MissionModel`, `TacticsModel`, `EventModel`, `MechanicsModel`, `WaveModel`, `AiModel`, `ScalingModel`) have no game types, so that they are testable on the Mac without the game.
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
- **Multiplayer**: co-op autotests (`scripts/autotest-mp.sh`, see CONTRIBUTING.md) run a host and a client on the PC and fail on any desync or state difference. A co-op session with two players checks the rest (PLAYTEST.md, Co-op).
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
69. Triggers (the Courier Job offer in requirement 85 is not an event and has its own roll; the Wanted trigger is in requirement 471): Start (once, `StartDelaySeconds` after a new run), Arrival (`ArrivalDelaySeconds` after the first arrival in a system, chance `ArrivalEventChance`), Signal (a player ship within `SignalTriggerDistance` of a signal POI; the POI goes away), Station (the first trade station within `StationTriggerDistance` in a system visit, chance `StationEventChance`; never the Buyer). Signals and stations wait until the Start event opened.
70. Arrival and Station events wait `CooldownSeconds` after the last triggered event. A system gets at most `MaxEventsPerSystem` Arrival and Station events. An event opens at most `MaxPerRun` times per run (rules, default 1; 0 means no limit). Only one event page waits at a time.
71. A trigger picks one eligible event by weight: `Weight + WeightPerHeat × heat + WeightPerTech × fleet tech count`, never below 0. Outcomes use the same weight rule. The outcome roll uses a seed from the simulation random source, taken when the page opens.
72. Outcome effects: money, money in tech value, fame (gain or loss), heat, reputation (requirement 81), the Buyer price (requirement 84), clearing wanted (requirement 472), tech units (through the Tech Vaults), crew, vanilla resources, hull damage, spawned ships, story flags and a follow-up page. Money gains are split between the human players like a mission reward. The chooser pays money costs.
73. Event money unit: 1 = the middle of a standard mission's pay at the sector tier × the economy money reward factor × `RewardScale` × `MoneyScale`. A fame unit is a standard mission's fame × the economy fame reward factor × `RewardScale` × `FameScale`. Heat effects × `HeatScale`. Money amounts use the tier and the tech value from when the page opened.
74. A choice goes through a multiplayer input (`HotCargoEventChoice`). The input carries the page serial, so a second click on the same page (by another player) does nothing. The input checks the chooser's need again before it applies the choice. If the sim rejects a choice, the buttons work again after 3 seconds. A trigger checks money needs against the human player with the most money, because any player can choose.
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

## 24. Mechanics missions

Missions that use game mechanics, not only combat. MISSIONS.md describes how each one plays. Code: `src/HotCargo/Missions/Mechanics/`. Rules: `mod/missions/mechanics/`.

130. Eight mission types use game mechanics: Drone Siege, Prototype Retrieval, Sunside Ore, Salvage Rights, Repossession, Storm Survey, Claim Jumpers and Listening Post. Their objectives are the rules types `HotCargoHoldOut`, `HotCargoPrototype`, `HotCargoSalvageTow`, `HotCargoCapture` and `HotCargoSurvey`, plus the vanilla `DeliverResources` and `TimeLimit`. Accepted missions are saved with the career and load with it.
131. Each mission type has one spawner group with a `hc_mech_<name>` tag. Before a system generates, the mod sets the group's spawn chance from `Mechanics.<Mission>Chance` (defaults 0.15 to 0.25). Rewards use the vanilla tier tables. Each mission carries contract terms (`HotCargoContract`) for the economy area (requirement 100 onward).
132. **Drone Siege**: drones attack a faction listening post. The objective completes after `Siege.Seconds` (300 s) of guarded time. Guarded means that a human ship, of any kind, is within `GuardRadius` (1,500) of the post. A player-built platform counts. Waves come only while the post is guarded: the first after `FirstWaveSeconds`, then one every `WaveSeconds`. Ships per wave = `ShipsBase` + `ShipsPerTier` × tier, capped at `MaxLiveShips` live wave ships. In co-op the wave and the cap scale (requirement 334). Wave ships are built-in combat ships of tier `ShipTierBase` + `ShipTierPerTier` × tier, in 1 to `ShipTierMax` (3), on one hostile side of their own. The mission fails when the post dies. The offer text hints at building a separate gun platform.
133. **Prototype Retrieval**: the Prototype Core part (`yarden_zamir.prototype_core`, 8,000 health) goes on the carrier's outer hull the first time the carrier is live after acceptance. The mission is recovered when the carrier surrenders to the players. It is also recovered when the ship that holds the core is a wreck or a player ship and a human ship is within `RecoverRange` (400) of the core. The core part then goes away. The mission fails when the core part is destroyed or the ship that holds it is gone. A split moves the core to the piece that has it.
134. **Salvage Rights**: the mission completes when the hull of a crewless freighter is within `Mechanics.DeliveryRadius` (800) of the station's hull. The players tow it, push it, or claim it with crew and fly it. It fails when the hulk is gone, the station dies or 25 minutes pass.
135. **Repossession**: the mission completes when the target ship surrenders to the players or becomes a player ship. It fails when the target is gone. A wreck that nobody claims yet is not a failure.
136. **Storm Survey**: three markers sit inside ion storms. A marker counts scan time while a human ship with a working sensor part is within 400 of it. The mission completes when every marker has `Mechanics.SurveyScanSeconds` (15 s). A system with fewer than two markers offers no survey.
137. **Claim Jumpers**: it works like requirement 132, with the `Guard` profile (240 s, raiders of the sector tier − 1), around a civilian mining barge in an asteroid field.
138. **Sunside Ore**: Helion Ore (`yarden_zamir.helion_ore`) forms only on Helion asteroids. The asteroids sit in one seam 2,000 to 3,300 from a single sun, inside its heat zone. The players sell 30 to 45 units to the nearest station within 30 minutes. Binary suns and sector types with the spawn flag `hc_no_sun_mining` get no seam.
139. A mechanics mission pays a multiple of a standard mission at its tier. MISSIONS.md lists the multiples. Wave ships that the C# code spawns pay no kill fame, so the mission reward holds all of the fame.
140. **Listening Post**: a scout of a faction listening post follows the nearest player ship. The players start the scan from the mission panel when the scout is within `Mechanics.ScanRange` (3,500) of a hostile base. Then it works like requirement 132, with the `Scan` profile (180 s, waves every 45 s from the base side). Before each wave the players can order up to `MaxIntensivePerWave` (2) intensive scans. Each scan gives the next wave `IntensiveExtraShips` more ships and `IntensiveExtraTier` more tiers, and adds `IntensivePayBonus` (0.25) to the money factor.
141. **Leave early** (requirements 132, 137, 140): after the first whole wave interval, the players can leave from the mission panel. The objective completes at once. The money rewards become the earned share × the money factor, and the fame rewards become the earned share. The earned share is the whole `WaveSeconds` intervals guarded ÷ the intervals in `Seconds`. Orders go through the MP input `HotCargoHoldOutOrder`. The input checks the order again when it runs.

## 25. Chains

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
169. A result row can apply event outcome effects (requirement 72, same units as requirement 73), reputation with the station's faction (in standard mission reputation at the sector tier, like requirement 81), chain flags and a story line. Then the chain moves to the row's `Next` stage. An empty `Next` ends the chain. Money gains split between the human players. A money loss comes from the player who chose (a page result), or from the richest human player (a mission result, ties to the lowest player index).
170. Comms names a stage offer "[Chain] <chain title>: <stage title>" with "[Chain]" in the event heading color. Its description starts with the last `StoryLines` story lines. Its reward list has a "Story chain, part N" line. Chain stage missions have their own mission category, "Story Chains", so the comms list and the mission log group them.
171. A result shows in the event window. A page result joins that page's result window. A result before a page that opens at once shows above that page. A mission result waits until no event page is open.
172. The chain state is saved in the run state: each station's chain, stage, flags, story, stage mission, waiting page and timers, and how often the run used each chain.
173. Chain logic changes the simulation only from the sim tick, the system switch, the mission log's removal event and the event choice MP input.
174. A content error in a chain (a bad graph, a missing mission or page, an unhandled result) shows in the overlay at the first chain check and turns off chains for that game.
175. A stage mission that cannot spawn in a system (a placement or tag search failed) tries again on the next visit.
## 26. Contract economy

A contract is a mission offer that a client gives: every offer in the station comms missions tab and the system and galaxy lists. Code: `src/HotCargo/Contracts`. Numbers: the `Contracts` tunables section.

100. **Terms.** Each contract has terms (`ContractTerms`): accept fee, completion bonus, flat heat on accept, trail weight, rival group and informant chance. Rules terms come first: a mission definition can put `{ Type = HotCargoContract; AcceptFee; FlatHeat; TrailWeight; RivalGroup; InformantChance }` in its Rewards list, and a missing field uses the default. Then `Contracts.Metatypes` gives terms for vanilla mission types. The rest comes from a roll seeded by the mission ID, so every client gets the same terms. The sim tick saves the terms of new offers once per second. Missions of a category in `ExcludedCategories` (default: exploration) are not contracts. Per rule:
     - Hot Cargo missions: Convoy Duty leaves half a trail (0.5) and rolls the rest. Joint Strike has flat heat 5, no fee and no informant. Discreet Bounty has a fee of 0.5, flat heat 4 and no informant (its trap is its own sell-out).
     - Vanilla missions: fee, trail and informant use the roll. Pirate and faction base attacks have flat heat 5.
     - Mechanics missions (requirement 130 and later), chain missions (requirement 160 and later) and event missions (requirement 86) carry their own `HotCargoContract` terms. The same UI, accept input, trail and informant rules apply to them. Missions that go straight to the log (not offered) are not contracts.
101. **Trail.** Open contracts (accepted, in progress, with terms) leave a trail. Heat per game minute = (sum of trail weights − `TrailFreeContracts`) × `TrailHeatPerContractMinute`, never below 0. Defaults: 3 free, 1 heat per minute for each contract above 3. The trail ignores the tech multiplier.
102. **Fees.** About half of the contracts (`FeeChance` 0.5) cost a fee. Model: purchase. The player who accepts pays the fee and does not get it back. A fee is `FeeMin` to `FeeMax` (0.3 to 0.8) standard mission pays at the mission tier, with the difficulty money factor and `RewardScale`, rounded to 500. A paid contract pays a bonus of fee × `PaidBonusFactor` (2) on completion to the player who paid the fee, so fee and bonus stay with one player in co-op. If that player left the game, the bonus is split like vanilla mission money. So a paid contract nets one fee more than a free one, and a failed one loses the fee. The bonus is a reward line on the offer (`HotCargoContractBonus`). A player with less money than the fee cannot accept: the accept button shows the fee and does not react, and the accept input refuses.
103. **Rivals.** Offers in one system with the same rival group are rivals. Accepting one dismisses the others, and a chat line names them. `RivalsEnabled` (default on) turns all rivals off, the rival groups of rules terms too, and at once also for offers that got a rival group before the switch. The scan pairs a new offer with an earlier offer of another client (another faction or another station): always when one offer targets the other's faction, otherwise with chance `RivalPairChance` (0.2). A new offer in the group of an open contract is dismissed.
104. **Informants.** `InformantShare` (75 %) of contracts carry an informant chance of 5 to 15 %. The accept rolls it with the simulation random source. On a hit, `InformantDelayMin` to `InformantDelayMax` (45 to 150) game seconds later, if the contract is still open, the client sells the players out: heat + `InformantHeat` (15) and a hunter wave of `InformantShips` (2) at the sector tier. A banner and a chat line name the client and the contract, and the mission log shows "<client> sold you out on this contract".
105. **Smaller systems.** For shorter in-system travel, gameplay spawns move in to about 70 % of the vanilla radius: military stations, bounties, wanderers and pirates, asteroid fields and belts, discoverables, beacons and the jump gates. Counts drop with the area. Trade stations keep the vanilla ring (at least 7500 from the sun), but there are 2 to 3 per system (was 2 to 4). Fewer missions: 2 to 4 bounties (was as many as fit), 1 delivery per station (was 2), Convoy Duty 45 % (was 60 %), Joint Strike 30 % (was 40 %), one Discreet Bounty at 60 % (was 1 to 2 at 70 %). Scenery (suns, planets, nebulas, fog) keeps the vanilla size. Military stations keep 4,500 from other stations and 2,500 from gates (was 6,000 and 3,000), or they rarely fit. Hot Cargo sites move inside the gate ring too: mechanics missions (requirement 130 and later), event signals and the archetype objects (requirement 229). The Buyer (requirement 1) keeps 3,000 from bounty ships, which now crowd its ring. The vanilla changes and the mission counts are rules in `mod/contracts/actions.rules` and `mod/missions/sysgen_hotcargo.rules`; the Hot Cargo sites are in the rules of their areas. The vanilla changes change the shared sector rules, so they apply to every map size while the mod is on, and need a game restart.
106. **Contract UI.** The mission info panel (comms missions tab and mission log) has a "Contract terms" section under the rewards. Before the accept it shows the fee (and the player's money when it is too low), the flat heat, the trail ("counts as N; open X → Y; trail +a/min → +b/min"), the rival offer that the accept dismisses, and the informant risk or "Discreet client". After the accept it shows the fee paid, the open count and trail rate, the informant risk, and a sell-out. The accept button reads "Accept (pay N)". Accepting and paying go through the game's own accept input (`MPAcceptMissionInput`), so they are deterministic in multiplayer.
107. The run state saves the terms, the pending sell-outs and the sold-out contracts. Accepted contracts with a bonus keep a `HotCargoContractBonus` reward, so like other Hot Cargo missions such a save needs the mod.
108. After a win, the contract economy stops: no fees, no flat heat, no trail and no sell-outs.

## 27. System archetypes

WORLDS.md holds the design. The archetypes give each system an identity, a hazard and a matching mission. They add no jumps.

220. A system of the run map gets a sector type by weight: four Hot Cargo archetypes (Trade Hub `hc_trade_hub`, Mining Belt `hc_mining_belt`, Solar Zone `hc_solar_zone`, Deep Space `hc_deep_space`) and the vanilla types at lower weights. The weights are rules values in `mod/worlds/archetypes.rules` (the vanilla `RandomSectorTypes` spawner, used by `mod/journey/galaxy.rules`). A change needs a game restart and applies to new careers only. The starting system stays basic. Deep Space needs tier 3 or more. Each archetype is the basic sector plus spawn flags (`mod/worlds/sectors.rules`), so every spawner that an area adds to the basic sector runs in it too.
221. **Trade Hub**: a hub world (1.5 × an inner planet, no other inner planets) next to a trade station, and a current: a straight band of the `hc_current` cloud, 18,000 long and 4,500 wide (rules type `HotCargoCurrent`), along the flow direction of the system. The flow direction is one of 8 directions, picked from the map node seed, so it is the same after a load and on every client. Each sim tick, the nebula effect `HotCargoCurrentPush` pushes every live ship in the band along the flow with an acceleration of `CurrentAccel` × cloud strength × (1 − speed along the flow ÷ `CurrentFlowSpeed`), clamped to 0 to 1. It never brakes a ship. Stations, asteroids and megaroids do not drift. Arrows drift along the flow over the band (`CurrentArrows`); the vanilla nebula tooltip and HUD line name the current.
222. **Mining Belt**: a dense belt of 1,700 to 1,900 large rocks and some megaroids, 900 wide, 11,900 to 12,500 from the center, instead of the vanilla belt. 3 or 4 passes (ring markers of radius 800) cut through it with no rocks inside. Pass rings and rocks keep clear of station and gate hulls. **Hold the Pass** (`HotCargoHoldPass`): first the players defeat the raiders who camp in one pass (vanilla `DefeatShips`). Then the hold clock runs while a human ship is within the ring. After the first entry, the mission fails when the ring stays empty for `PassLostSeconds`. Every `PassWaveSeconds` of holding, a wave (`PassRaiders` in `mod/worlds/spawns.rules`) comes from `PassWaveDistance` away. The objective completes after `PassHoldSeconds`. Away from the system, the clock waits.
223. **Solar Zone**: a single vanilla sun with its vanilla heat zone (the kill radius is 1,500 and the zone edge is 3,750 for the default sun). Depth = 0 at the zone edge and 1 at the kill radius; the vanilla heat grows with depth. A HUD line shows the deepest own ship in a heat zone and its depth, red from `HeatWarnDepth`, in every system with a sun. **Sundive** (`HotCargoSundive`): a wreck site at depth 0.15 to 0.25. A human ship within 300 of the site salvages; after `SundiveSalvageSeconds` of salvage time that ship carries the recorder, and the objective completes when it is out of every heat zone. It fails when that ship is destroyed. The heat also burns Tech Vaults (requirement 65), so a dive with the tech aboard costs tech. The Sunside Ore seams (requirement 138) sit on the sun too. Other missions can target this archetype with the spawn flag `hc_solar_zone`.
224. **Deep Space**: the vanilla dense nebula sector plus 2 or 3 more ion storms. A pirate lair (the vanilla pirate base shape, hidden until discovered) sits in a calm eye (radius 1,800) of its own ion storm, 8,000 to 13,000 from the center. **Raid the Lair**: `HotCargoFindLair` shows a search circle of radius 4,500 that holds the lair, with the lair 20 % to 60 % of the radius from its center. It completes when a human ship comes within `LairDiscoverDistance` of the lair, or when the lair is gone. Then the players destroy the lair (vanilla `DefeatShips`).
225. On the first arrival in a system, after `ArrivalLineDelaySeconds`, a toast and a chat line name the sector type and its hazard (`ArrivalLine`). The galaxy map tooltip shows the same hazard line under the vanilla sector type line. The hazard lines are strings `HotCargoWorlds/Hazard_<sector type ID>`; a type without one shows no line.
226. Accepted archetype missions save their objective state with the career: the hold time, the empty time and the waves of a pass hold, the salvage time and the carrier of a sundive, the lair and the search circle of a raid. A save with an accepted archetype mission does not load without the mod.
227. The current push, the objectives and the waves change the sim only from the sim tick (the nebula effect and `EventHub.Tick`). Waves use a random source seeded from the mission. Before a run is active (the first system of a new career), sim code uses the default `Worlds` tunables, not the local file.
228. Contract terms (`HotCargoContract`, requirement 100 and later): Hold the Pass is free, flat heat 6, trail weight 1, informant chance 10 %. Sundive costs 0.3 standard mission pay, flat heat 2, trail weight 0.5, informant chance 5 %. Raid the Lair is free, flat heat 4, trail weight 1, informant chance 20 %. Pay: Hold the Pass 2 × a standard mission's money and fame, Sundive 3 × money and 1.5 × fame, Raid the Lair 2.5 × money and 2 × fame.
229. Archetype objects fit the smaller Hot Cargo systems (requirement 105): the belt lies between the trade station ring (up to 11,000 from the center) and the FTL gates (13,500 to 16,000). The hub world keeps 2,500 from the gates. The autotest `worlds-fit` checks that real system generation places each archetype mission and keeps the archetype objects clear of stations and gates.

### Wanted and hunted

470. When the players are wanted (vanilla: a faction in the system hunts them with police) and heat reached `WantedWarningMinStage` (default 1, the first stage), a warning shows once: a short message at the top of the screen and a chat line that names the factions and the ways out. Ways out: pay the bounty (hail one of their police ships; the vanilla surrender dialogue offers to pay it), leave the system (wanted belongs to the system), outlast the search timer, or a Wanted event (requirement 473). The warning shows again only after the players stop being both. Wanted alone stays vanilla.
471. The Wanted trigger: when the warning in requirement 470 shows, a Wanted event opens with chance `WantedEventChance` (default 0.7). It ignores the cooldown and the per-system cap. It does not open while another page waits.
472. A need can require wanted status (`Wanted = true`). An outcome can clear wanted (`ClearWanted`): every faction in the system stops wanting the players, as a paid bounty does.
473. Wanted events: A Clean Record (pay 0.4 + 0.01 × heat money units, with a sell-out risk that grows with heat; or pay one crate for a sure clear and heat −5) and Decoy Run (pay 0.5 money units, with a failure risk that grows with heat; or a Sensor Array ghost). Each opens once per run.

## 28. Co-op enemy scaling

Vanilla Cosmoteer does not scale enemies with the player count: each player gets full starting money and a ship, and enemy counts are fixed. So the mod scales hostile ship counts in co-op. Code: `src/HotCargo/Scaling`. Pure math: `Scaling/ScalingModel.cs`.

330. **Setting.** The single-player and multiplayer new career screens have a slider "Enemy ships per extra player": 1.0 to 2.5 in steps of 0.1, default 1.5. The game saves the last value as a preference. In multiplayer only the host moves it, and the clients see the host's value. The value goes to every machine in the career init (`HotCargoCareerInit`), so the first system generates with it. At run start the run state takes the value, and the game keeps a vanilla `CareerInit`, so the init needs no mod type in the save. A save from before this setting uses 1.5. With the mod off, the screens stay vanilla.
331. **Factor.** Factor = 1 + (setting − 1) × (players − 1). The setting is the factor with 2 players, and each extra player adds the same step: at 1.5, 3 players face 2× and 4 players 2.5×. Solo is always 1×. Players are the human players in the game at that moment; two players on one shared team count as two.
332. **Rules spawns.** One patch on the game's ship spawner (`SimObjectSpawner.GetCount`, `ShipSpawner.PickSpawnShips`) scales every rules ship spawn: system generation, encounters and the Hot Cargo rules spawns. A spawn scales only when it is hostile: unique or barbarian allegiance, or a parent that synchronizes such an allegiance. Faction military, faction civilian, neutral and junk ships, stations and landmarks never scale.
    - A group (`Count` above 1, no sub-spawners) scales its count. Examples: pirate wanderers, asteroid and graveyard pirates, base defense ships and platforms (pirate bases, the Joint Strike stronghold), storage pod guards, station captors, convoy raiders, claim escorts, event and chain ambush groups, pass raiders, the shady trap ambush.
    - A spawner with sub-spawners (a mission or marker for each ship) keeps its count. Only a fleet grows: 2 or more ships in one spawn (the vanilla fleet bounty), or a spawner with the tag `hc_scale_fleet` (the vanilla ambush, `mod/scaling/actions.rules`). Extra ships are copies of random fleet ships in the same fleet, so fleet objectives target them.
    - A single ship stays single: a bounty target, a mission carrier, a capture target.
333. **Rounding.** The whole part of count × factor always spawns. The fractional part is the chance of one more ship, rolled with the spawner's random source (the system seed or the sim random), so every machine gets the same count. Factor 1 rolls nothing, so a solo game spawns exactly as before.
334. **C# spawns.** Hostile ships that C# code spawns go through `Scaling.EnemyScale.Count(n, sim)`: hunter groups of every stage, repeat strike waves and informant sell-outs (requirement 104), and mechanics hold-out waves (requirement 132). Their live caps (`MaxLiveHunters`, `MaxLiveShips`) × the factor, rounded half up (`EnemyScale.Cap`). New C# code that spawns hostile ships uses the same helper.
335. **Police.** The police count that hunts a wanted player (`WantedPoliceCount` of the reputation title) × the factor, rounded half up without a roll, because the game checks the count again every tick.
336. **Rewards.** Rewards do not scale on their own. A fleet bounty pays by the summed value of its targets (`TierCombineMode = ValueSum`), and each enemy pays kill fame, so more ships pay more. Vanilla mission money is still split between the players.

## 29. Settings menu

TUNABLES.md lists when each tunable takes effect. Code: `src/HotCargo/TunablesMenu`.

500. The escape menu of a Hot Cargo run has a "Hot Cargo settings" button under Settings. It opens the "Hot Cargo settings" window. The window has one section for each `Tunables` section and one row for each tunable. The rows come from the tunables classes by reflection, so a new tunable shows without a menu change. Each row shows the current value, the default, a reset button and a description. The descriptions are the XML doc comments of the fields (`HotCargo.xml` next to the DLL). A field without a doc comment shows only its name. Values that differ from the default show their name highlighted. A search box filters the rows by name, path and description. Each section has a reset button.
501. Controls: a check box for a switch, a drop list for an enum and for a heat stage name (the `*MinStage` fields and the `Name` of a `Hunters.Stages` response), a text field for a number or a text, and a comma separated text field for a list of numbers or texts. A list of objects (stage tables, metatype terms) shows one group for each item with Remove, and Add (a copy of the last item) and Reset for the list. A nested object (the hold-out profiles) shows as a group.
502. Each change applies at once and is saved. The menu writes `tunables.rules` with only the values that differ from the defaults, and the comments in that file are lost. A nested object is written in full when its class defaults differ from the defaults of its parent (for example `Mechanics.Siege`), because the reader starts a nested object from its class defaults. Lists are written in full.
503. Each change goes through `TunablesFile.Validate` first. An invalid value or text that is not a number is rejected: the window shows the reason in red, and the values in effect and the file stay. A change replaces the tunables of the run and gets a new tunables hash. New stage thresholds re-baseline the started stages like a system entry: a stage at or below the current heat counts as started and spawns nothing.
504. Multiplayer: only the host can change values. The host's change goes to every machine as the `HotCargoTunables` input (requirement 60), so every machine applies it at the same sim tick. Clients see the window read-only. Live reload of the file stays single player only.
505. Each row with a delayed effect has a tag: next time it shows, next spawned ships, next mission offers, next new system, or new runs only. TUNABLES.md has the full list. The list lives in `TunablesMenu/TunableTiming.cs`, and the mod checks at startup that each entry names a field.
506. The autotests `menu-live` (single player) and `menu` (co-op) change tunables through the window rows and check the effect in the sim, the file, the rejections, the read-only client and that no desync happens.

## 30. One-tile Tech Vault

Code: `src/HotCargo/Tech` (`StartingVault`, `VaultDamagePatch`). Part rules: `mod/tech/tech_vault.rules`. Numbers: the `Tech` tunables section and the part rules.

440. The Tech Vault has one size and no tiers or upgrades. Players decide how many vaults to build, where to put them and how to defend them with hull, armor and shields. Hunters find vaults by the part ID (requirement 34), the vault outline (UX.md H6) marks every vault, and the loss rule counts the tech in all vaults (requirement 9).
441. Starting placement: the vaults go inside the hull in place of the smallest vanilla storage part (one with a flexible storage grid), nearest to the ship center first. The storage part goes like a part that the player grabs: corridor tiles take its place. Then a vault replaces one corridor tile at a time, and the game adds the doors like the build tool. Tiles with a door of the storage part stay corridor while other tiles are free, so a walkway through the room stays. With no storage part left, a vault goes on a free tile next to the hull: the most sheltered tile first (most hull tiles next to it), then the tile nearest to the center, and only where crew can walk to it from crew quarters. Each placement must leave a valid design. If crew still cannot walk to a vault, a notice asks the player to add a door. On the vanilla starters, the 4 vaults fill a 2×2 storage bay (Model-L, Model-C, Model-S), or 4 tiles of a 3×2 bay with a corridor walkway in the middle (Model-M). The autotests `tech-start` and `tech-outside` check the vault count, design validity and crew paths to every vault and part.
442. Vault damage (requirement 65) fits a 2-unit vault: the default step `VaultDamagePerUnitLost` = 0.5 of max health. A destroyed vault loses its 2 units; other vaults keep theirs. The vault health (3,000) and cost (24 steel) are part rules; the damage step is a tunable.

# Tunables in a running game

The settings menu (Esc, "Hot Cargo settings", spec requirements 500 to 506, `spec/29-settings-menu.md`), a change of `tunables.rules` and `scripts/tune.sh` (spec 850 to 854, skill `hot-cargo-tune`) change any tunable while a run is active. This file says when each change takes effect. The in-game tags come from `src/HotCargo/TunablesMenu/TunableTiming.cs`. Keep the two in step.

Every change replaces the tunables of the run at once (single player) or at the same sim tick on every machine (multiplayer, host only: the host's window, the host's file or `tune.sh` on the host's PC). "Applies" says when the game reads the new value.

| Applies | Meaning |
|---|---|
| Now | Within a second. The code reads the value at each use. |
| Next time it shows | The next time the thing opens or shows. One that is open keeps the old value. |
| Next spawned ships | Ships that spawn after the change. Ships in space keep their values. |
| Next mission offers | Offers and contract terms made after the change. An offer keeps its terms, so the price that the offer showed is the price that the players pay. |
| Next new system | The next system that the players enter for the first time. Rolls for systems already visited stay. |
| New runs only | Read once at the start of a run. |

Fields not listed in the "Not now" column apply Now.

| Section | Not now | Notes |
|---|---|---|
| Heat | (none) | `HeatCarryOverFraction` is read at each switch without a jump. `Stages`: new thresholds re-baseline the started stages (spec 503). A stage below the current heat spawns nothing; a stage above it waits for heat. |
| Tech | `StartingTechUnits`, `StartingResearch`: new runs only | The tech value fields apply to the next price query. An open event page keeps the tech value of its opening. |
| Journey | `RunSectorCount`: next new system | Fixed in this change: the Buyer spawns in the first new system with sector index `RunSectorCount` or more, once per run (`RunState.BuyerSpawned`). Before, a value below the current sector meant no Buyer. The galaxy size is a rules value (restart). `RewardScale` applies to payouts at once, but contract fees keep the scale of their offer. |
| Hunters | `Stages` (all fields), `SpawnDistanceMin`, `SpawnDistanceMax`, `WaveArcDegrees`, `ShipFactions`, `ShipTags`, `ShipNamePrefix`, `MaxLiveHunters`, `MinFuel`, `RouteTierFloor`: next spawned ships | The waiting groups of a stage that started take the new count and tier at once and keep their arrival times (spec 854). `StrikeWaveIntervalSeconds` and `VaultTargetFactor` apply now. |
| Missions | `ShadyRewardFactor`, `ShadyTrapSurvivalReward`, `EscortHeatChange`, `EscortRouteShare`: next mission offers | The rewards and the route are part of the offer. The trap chances roll on arrival, so they apply now, also to accepted offers. The convoy ambush tunables apply now. |
| Mechanics | `<Mission>Chance`: next new system. `Siege`/`Guard`/`Scan` ship counts, tiers and tags, `IntensiveExtraShips`, `IntensiveExtraTier`: next spawned ships | The chances go into the spawner rules before a system generates. Mission clocks, radii, `ScanRange`, `DeliveryRadius` and `SurveyScanSeconds` apply now, also to missions in progress. Known limit: a mission HUD line can show an old range until its own counter changes. |
| Tactics | (none) | Every AI check reads the values. A deadline that a ship set (fall back, call and regroup cooldowns, assist time) keeps its length. Personalities, trait weights, `PersonalitiesOff` and the `Profile` score ends apply at the next AI check (spec 361-363); a ship measures its build again within `ProfileRefreshSeconds`. The weak-side numbers (`VulnerableParts`, `WeakSideCover`, `ArmorCoverWeight`, `WeaponSideWeight`) apply at that measure (spec 1030-1031); `WeakSideEnabled` applies now. |
| Events | `ArrivalDelaySeconds`: next new system. `RelicEnabled`, `RelicMinSector`: new runs only. `PauseOnEvent`: next time it shows | `MoneyScale` applies at once, also to an open event page. The chances are read at the roll: the arrival roll comes `ArrivalDelaySeconds` after the entry, the station and courier rolls at the first station that the fleet comes near in a system visit. A system that rolled keeps its result. |
| Contracts | `FeeChance`, `FeeMin`, `FeeMax`, `PaidBonusFactor`, `MoneyRoundTo`, `RivalPairChance`, `InformantShare`, `InformantChanceMin`, `InformantChanceMax`, `InformantDelayMin`, `InformantDelayMax`, `ExcludedCategories`, `Metatypes`: next mission offers | The terms of an offer are saved within a second of its first scan (spec 100). Fixed in this change: `RivalsEnabled` off now also stops the rival groups that offers got before the switch. The trail, `InformantHeat` and `InformantShips` apply now. |
| Chains | `StationChance`, `MaxNewChainsPerSystem`, `LaterDelaySeconds`: next new system. `StoryLines`: next time it shows | `Enabled` and `PageTriggerDistance` apply now. |
| SharedChoice | (none) | `VoteTimeoutSeconds` applies to open votes at once: the timeout counts from the first vote. |
| Worlds | `PassWaveDistance`: next spawned ships. `ArrivalLine`, `ArrivalLineDelaySeconds`: next new system | Before a run is active (the first system of a new career), sim code uses the defaults (spec 227). |
| Factions | (none) | The jump heat cuts are read at each jump. The roles, zone factors, fence premium, price factors and refusal values apply at once, also to offers that exist (their money reward is computed when it shows and when it pays) and to an open comms window. |
| Ux | `RunIntro`: new runs only. `StageAlertSeconds`, `CommsStayOnMap`, `PauseOnComms`, `PauseOnCommsMultiplayer`: next time it shows | All other switches apply on the next frame. Known limit: in an open event window, the dialogue key numbers keep their state until the page changes. |
| Diplomacy | (none) | Chances and prices apply to the next choice and the open dialogue, the pay-off chances and `PayChanceCap` too. `BluffShare` and the pay-off slider ranges apply to the open dialogue (a draft value outside the new range snaps into it). `HandOverHeatDrop` applies to the next wave that leaves. `HunterSurrenderMoneyShare` applies to the next surrender. Hail and mercenary rolls happen once per ship, so a ship that rolled keeps its result; ships that did not roll yet use the new value. A running hire keeps its end time. |
| Onboarding | (none) | `VanillaTutorialDelaySeconds` counts run time, so a loaded run that is older than the delay does not hold vanilla tutorials. |
| RunModes | `Combat`, `Economy`, `Crew` rows: `HunterShips`, `HunterTiers`: next spawned ships. `ContractFees`: next mission offers | `HeatRate`, `TechValue` and `StrikeMusic` apply now. A change of the vanilla difficulty level in the game settings follows the same timing (spec 680). |
| Balance | (none) | A higher fuel floor tops up the live stations at once (spec 852). A lower floor takes no fuel away. Trade ships use the new floor at once. |
| Debug | (none) | |

## Why some values stay delayed

A value stays delayed only when an earlier change would be wrong or would break co-op. The live pass for spec 850 to 854 kept these:

- Next spawned ships: ships in space keep their build, tier and fuel. Changing a live ship mid-fight would be a new spawn, not a tuning change.
- `Events.ArrivalDelaySeconds`: the arrival roll time is set at the system entry.
- Next mission offers: an offer keeps its terms, so the price that the offer showed is the price that the players pay.
- Next new system: the roll for a system happened (missions, events, chains, the arrival line). Rolling again would change a system that the players already see.
- New runs only: the value sets up the run start (starting tech, starting research, relic sector).
- Next time it shows: an open window or alert keeps its layout until it closes.
- `Diplomacy.PunishSeconds`: a waiting punishment keeps the end time that all players saw.

## Values that a change cannot reach

- Rules content (parts, missions, archetype weights, `FinalSectorTier`, the galaxy size): needs a game restart (spec 54).
- A new co-op run starts with the defaults. The host's file arrives as an input on the first frame, so the starting vault and the first system's offers use the defaults (spec 60). Later changes of the host's file reach every machine (spec 850). A client's file has no effect.
- Test commands that change a tunable in place (`ev_set`, `sc_set`, `ux`, `onboarding`, `contract_set`, `mech_set`, `aiset`, `buyer sectors`, `maxhunters`, `tacticlog`): a later menu change or file reload replaces the tunables and drops those edits. Tests that mix the two set every value through the menu.

## Screenshots

`tests/screens/tunables-menu.txt` takes these (main slot only, see UX.md "Screenshots"):

| Image | Shows |
|---|---|
| `docs/screenshots/menu-escape.jpg` | The escape menu with the "Hot Cargo settings" button under Settings |
| `docs/screenshots/menu-window.jpg` | The window with all sections closed |
| `docs/screenshots/menu-search-stage.jpg` | A search: the heat stage table with its Add, Reset list and Remove buttons |
| `docs/screenshots/menu-heat-error.jpg` | A changed value (highlighted name), a rejected value with its message, and a "next new system" tag |
| `docs/screenshots/menu-hunters-list.jpg` | The hunter stage responses with their "next spawned ships" tags |

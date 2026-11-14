# Test tiers

Three tiers of tests:

| Tier | Where | Runs | Cost |
|---|---|---|---|
| unit | `tests/HotCargo.Tests` (every `*Model.cs`) | every build, CI | under 1 s for all |
| smoke | game tests with the line `# tier: smoke` | `scripts/autotest-affected.sh` (default `AUTOTEST_TIER=smoke`) | about 60 to 150 s each |
| full | every game test | `scripts/integrate.sh` merge batches (`AUTOTEST_TIER=full`), `scripts/autotest-all.sh` | about 2.5 h of slot time |

Rules:
- Put logic (math, rules, state machines) in a `*Model.cs` file with no game types, and test it there.
- A game test checks what only the game can show: spawns, AI, physics, UI, saves, co-op sync, vanilla integration. It may compare a game value with a model value (a "gap" field). It does not test the model again with other inputs.
- Each area has one smoke test: it loads and runs the main flow. An area without a smoke test runs all its tests in the smoke tier.
- A touched area always runs every co-op session with a step of the area (`step <area>.<name>`): they guard co-op determinism. A change in shared code runs every smoke test and the co-op session marked smoke (`coop-core`).
- Put the marker at the end of the file, after `quit`, so it does not conflict with edits at the top. The `# runner: pc` line (headless sim) goes there too.

## Audit

Time: median wall time per run from `../.state/logs` (deploy, game start and script). Tier: what the checks need. Unit: the model that covers the logic of the checks.

Actions: **smoke** (in the smoke tier), keep (full tier only), done (changed in this branch), proposed (for later, after the area's open fixes land).

### Single player

| Test | Time | Checks | Needs the game for | Unit | Action |
|---|---|---|---|---|---|
| ai-call | 87 s | call start, complete, interrupt by disruptor and EMP, storm block | AI radar, weapon hits, storms | AiModel.NextCall | keep |
| ai-call-intercept | 60 s | call time, and the longer call that a player sensor intercepts | AI radar, sensors | AiModel.NextCall | keep |
| ai-flee-station | 50 s | a fleeing faction ship short of crew docks at a station and fights again | stations, crew spawn, repair | AiModel.NextFlee | keep |
| tactics-fallback-cover | 50 s | a damaged ship with resources hides behind a rock and repairs | AI movement, crew repair | RepairModel.PickPlan | keep |
| tactics-fallback-donor | 60 s | a damaged cargo ship takes resources from an ally and repairs | resource transfer, crew repair | RepairModel.DonorGives | keep |
| tactics-fallback-station | 50 s | a damaged faction ship gets crew and repairs at a station | stations, crew spawn, repair | RepairModel.PickPlan | keep |
| ai-cover | 68 s | a ship hides behind a rock | AI movement, line of sight | PersonalityModel.CoverSpot, NextCoverPhase | keep |
| ai-flee | 79 s | roll once, unarmed runs, flee jump | AI, damage, jumps | AiModel.NextFlee | keep |
| ai-personality | 69 s | personalities by faction, heavy ships never kite | ship builds, spawns | PersonalityModel | **smoke** |
| ai-team | 71 s | team shares target | AI targets | PersonalityModel.AssignRoles | keep |
| balance-hunter-floor | 93 → 58 s | route tier floor on a real spawn | hunter spawn pool | TierCapModel.RouteTier | done: `sectorindex 4` in place of three jumps |
| balance-rules | 92 s | fuel rules, blueprint price, peak tier cap, station fuel floor, fame over the cap | vanilla rules, stations, jumps | TierCapModel, BalanceModel, HeatModel | **smoke** |
| chains-content | 91 s | every page renders, transport loss fails the stage | rules load, spawns | ChainModel.Validate, Classify | keep |
| chains-fail | 90 s | abandon input, refusal, save mid-chain, Return mission | mission log input, save | ChainModel.Match | keep |
| chains-later | 114 s | guarded wreck pay, Arrival page, careful targeting, tech reward | comms pay, spawns | ChainModel | keep |
| chains-walk | 80 s | chains load, offer in comms, result, choice, loop, end | comms, MP input | ChainModel | **smoke** |
| contracts-contracts | 71 s | fee, bonus, flat heat, refused accept, rival dismissal | accept input, money | ContractsModel.Fee, Bonus | **smoke** |
| contracts-trail | 122 s | trail heat after 20 s, informant sell-out, save | sim tick, save | ContractsModel.TrailPerMinute | proposed: shorter trail wait (contracts fixes open) |
| contracts-value | 55 s | tech value fraction, notoriety ratchet | run state | TechModel.Value | keep |
| core | 89 s | heat, stages, fame heat, save and load | run hooks, save | HeatModel | **smoke** |
| diplomacy-friends | 100 s | hold, fee, betrayal, hire, mercenaries, wanted | comms pages, AI, money | DiplomacyModel | keep |
| diplomacy-hunters | 142 s | hand-over, punishment combos, bluff, deal crowd, demand hail | comms pages, hunters leave | DiplomacyModel, MultiChoiceModel | keep |
| diplomacy-surrender | 96 s | demand, fuel overflow, yield offer, players surrender | AI, fuel, comms | DiplomacyModel.SurrenderChance | **smoke** |
| events-abandoned | 99 s | hulk loot, hold reveal, blast, relic ship | proximity triggers | EventModel | keep |
| events-batch2 | 78 s | five events: spawns, flags, toll by heat, Buyer price | rules, spawns, prices | EventModel.HeatScaledUnits, BuyerPriceAfter | keep |
| events-courier | 65 s | courier offer, contact out of stasis | stations, stasis | — | keep |
| events-hail | 78 s | ship hails, players hail, attack mid-dialogue | comms | — | keep |
| events-outcomes | 89 s | every event renders, choices change money, heat, tech, flags | rules, MP input | EventModel | **smoke** |
| events-research | 67 s | research offers from the tech tree | vanilla tech tree | EventModel.BuildResearchTrade | keep |
| events-save | 79 s | pending event after save and load | save | — | keep |
| events-tow | 69 s | Dead in Space, Tow Job, live text tokens | missions, text | — | keep |
| events-triggers | 77 s | Start, Arrival, Signal, Station triggers | game hooks | EventModel.CooldownOver | keep |
| events-unavoidable | 67 s | every choice costs, per-run cap | rules | EventModel.UnderRunLimit | keep |
| events-wanted | 68 s | warning, Wanted trigger, clear vanilla wanted | vanilla wanted | EventModel.WantedWarning | keep |
| factions-combat-jump | 123 s | blind spin-up time, jump waits for a save block | hyperdrive, combat | FactionsModel.BlindSpinDone | keep |
| factions-jumps | 161 s | heat cut of clear, watched and blind jumps; blind fuel and damage | jumps (one per game) | FactionsModel.HeatKept | keep |
| factions-peace | 17 s (Mac) | ally faction and hunters do not target each other in ally space; no hire there; neutral control | AreEnemies, AI targeting, hunters | FactionsModel.Peace, HunterPeace.Covers | keep |
| factions-prices | 55 s | markup by heat, crew price, refusal message | trade prices, comms | FactionsModel.MarkupFactor, CrewFactor | **smoke** |
| factions-zones | 89 → 73 s | ally zone, shelter, fence prices, zone rate, markers | map factions, sim tick | FactionsModel.TimeHeatFactor, NotorietyFactor | done: one rate measurement in place of two |
| hunters-spawn | 100 s | stage spawns, distance, tier, strike waves, save | spawns, AI | WaveModel | **smoke** |
| hunters-tactics | 114 s | every tactic acts | AI | TacticsModel | keep |
| hunters-takeover | 33 s | players take over a hunter, then new hunters stay hostile; a wave joins the hunters in stasis; save | spawns, stasis, ownership | — | keep |
| hunters-waves | 128 s | strike scaling by sector, wave gaps, save, fuel drop | spawns, save | WaveModel | proposed: shorter late-sector gap (hunter fixes open) |
| journey-jump | 77 s | Buyer appears through the real jump hook | jumps | — | keep |
| journey-run | 152 s | run map, tier cap, Buyer price, save, win | map generation, trade | TierCapModel | **smoke** |
| mechanics-holdout | 118 s | hold clock, waves, done, fail, leave early | spawns, positions | MechanicsModel | keep |
| mechanics-listening | 91 s | scout, scan range, intensive pay, leave early | spawns, positions | MechanicsModel | keep |
| mechanics-offers | 69 s | rules load, chance tunable, forced offers, sun seam | — | — | done: merged into mechanics-placement |
| mechanics-placement | 77 → 89 s | chance tunable, forced offers, real placement in three systems | system generation | MechanicsModel.Gap | **smoke** (with mechanics-offers) |
| mechanics-prototype | 100 s | core on the carrier, wreck, break, surrender | objectives | — | keep |
| mechanics-salvage | 122 s | tow and capture: done and fail | objectives | — | keep |
| mechanics-save | 100 s | every mechanics objective in a save | save | — | keep |
| mechanics-survey-mining | 146 s | survey markers, sun mining, delivery | flight, ore, trade | — | keep |
| missions-escort | 74 s | escort accept, steering, arrival | AI steering | — | **smoke** |
| missions-offers | 53 s | offers and the sides of AI ships | spawns, factions | — | keep |
| missions-save | 89 s | escort and trap in a save | save | — | keep |
| missions-shady | 68 s | trap roll on arrival, ambush pays | spawns | MissionModel.TrapChance | keep |
| onboarding-hints | 118 s | each trigger shows its page once | codex UI, settings | — | **smoke** |
| onboarding-log | 185 s | run log lines for each kind, save | career log UI, trade, win | — | proposed: cheaper sale and win steps (onboarding fixes open) |
| runmodes-difficulty | 110 → 87 s | every level has a row, hard combination through vanilla inputs | vanilla settings inputs, sim tick | PressureModel | **smoke**; done: one rate measurement in place of two |
| runmodes-ironman | 109 s | Ironman save deleted on a loss | files, game over screen | — | keep |
| runmodes-music | 63 s | battle music at the last stage | vanilla music | — | keep |
| runmodes-paneltip | 67 s | heat panel tooltip lines | UI | — | keep |
| scaling-counts | 55 s | enemy counts for 1 to 3 players | spawns | ScalingModel | **smoke** |
| scaling-ui | 58 s | new career screen slider, save | UI, save | ScalingModel.Snap | keep |
| shipai-basic | 110 s | tactics rows on a real ship | AI orders, weapons | ShipAiModel.Evaluate | **smoke** |
| shipai-save | 101 s | tactics in saves and design files | save, design files | ShipAiModel.Encode, Decode | keep |
| tech-drop | 76 s | destroyed vault: crates or burn, crew collect | crew, construction | TechModel | keep |
| tech-hunters | 68 s | hunter takes crates, wreck spills them | AI | — | keep |
| tech-outside | 102 s | vault outside the hull, move tool, loss timer | ship design, crew | — | keep |
| tech-sell | 106 s | sale through the trade window, loss on the last sale | trade inputs | TechModel.Value | keep |
| tech-start | 123 s | starting vaults, price, damage steps, save; price after 55 s | ship design, save | TechModel.Value, UnitsLostToDamage | **smoke**; proposed: drop the 55 s price wait (tech fixes open) |
| tech-timer | 175 s | loss timer, crate markers, save; loss after 60 s | crew, save | — | proposed: shorter grace after the load (tech fixes open) |
| telemetry-basic | 97 s | session file events, overhead | game hooks, files | — | **smoke** |
| telemetry-run | 256 s | every event kind in a scripted three-system run | whole game | — | keep |
| tunablesmenu-live | 67 s | settings window applies, writes the file, rejects bad values | UI, files | — | **smoke** |
| ux-basic | 80 s | panels, comms pause, ship alert, loss panel | UI | — | **smoke** |
| ux-more | 80 s | dialogue keys, supplies, trade hints, comms on the map | UI | — | keep |
| worlds-current | 150 s | a ship and a crate drift in the current and out of it | physics | WorldsModel.Push, WorldsModel.ItemDrift | keep |
| worlds-fit | 89 → 85 s | map archetypes, hazard tooltip, real placement in each archetype, trade hub offers | system generation, UI | — | **smoke** (with worlds-map) |
| worlds-lair | 79 s | lair found only near it | proximity | — | keep |
| worlds-map | 68 s | map archetypes, hazard tooltip, trade hub | — | — | done: merged into worlds-fit |
| worlds-pass | 114 s | hold the pass: done and fail | positions, waves | WorldsModel.PassStep | keep |
| worlds-save | 92 s | pass and sundive in a save | save | — | keep |
| worlds-solar | 91 s | heat by depth, sundive | sun damage | WorldsModel.HeatDepth | keep |

### Co-op

Co-op tests are 4 sessions (`tests/autotest-mp/`): `coop-core`, `coop-economy`, `coop-combat` and `coop-stress` (a cost measure, spec 42). Each session checks determinism with a hash of the whole run state and has one step for each old co-op test check. A touched area runs every session with a step of the area. `coop-core` is the co-op smoke test for shared code. The old 21 tests map to steps as listed in the session files (the step names start with the area).

## Findings

- Most logic already lives in models: 15 of 20 model files had unit tests for every function. Game tests mostly check that the game calls the model (gap fields). So few game checks can move as a whole.
- The large cost is the number of game tests that a change runs. Before the tiers, a change in Core or shared UI ran all 100 game tests. A change in one area ran all its tests (Events: 11 single player and 1 co-op).
- The fixed cost of a game test is about 30 s (deploy, game start) plus about 10 s for a new career. Merging two tests with overlapping checks saves about 40 s of slot time.

## Numbers

Medians from `../.state/logs` (November 2026). A co-op test counts as two slots for about 115 s each. "Ideal" divides slot time by 6 slots; a busy PC takes longer (the full suite on 12 November took 2 h 40 min).

| | Before | After |
|---|---|---|
| Game tests (single player + co-op) | 79 + 21 | 77 + 21 (co-op later: 3 sessions) |
| Full suite slot time | 12,257 s (ideal 34 min) | 12,052 s (ideal 33 min) |
| `autotest-affected.sh` after a Core change | 100 tests, 12,257 s | 23 tests, 2,332 s (ideal 6.5 min) |
| `autotest-affected.sh` after an Events change | 13 tests, 1,163 s | 3 tests, 408 s |
| Unit tests | 145 | 149 |

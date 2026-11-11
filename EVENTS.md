# Hot Cargo events

FTL-style choice events for the Hot Cargo run. Spec requirements 67 to 87 (SPEC.md section 22) describe the system. This file holds the design and the catalog.

## 1. Goals

- Each event asks one question about the core loop: **hold or sell** the tech, **move on or burn** time and heat, **risk the cargo** or pay to keep it safe.
- Events feel like the game: comms-style window, vanilla ships and missions, vanilla resources, reward lines in the game's colors.
- Choices matter: a sure cost on the button, a hidden roll behind it, and conditions that unlock better options (FTL's blue options).
- Text is short and readable at a glance: 2 to 4 sentences, second person, no lore dumps.

## 2. How events work

### Triggers

| Trigger | When | Limits |
|---|---|---|
| Start | `StartDelaySeconds` (3 s) after a new run starts. | Once per run. Signals and stations wait for it. |
| Arrival | `ArrivalDelaySeconds` (6 s) after the first arrival in a system, with chance `ArrivalEventChance` (60 %). | Cooldown, per-system cap. |
| Signal | A player ship comes within `SignalTriggerDistance` (900) of a signal POI ("?" icon). The POI goes away. | None: the player chose to go there. 1 to 2 signals spawn in 80 % of systems. |
| Station | The first trade station that a player ship comes within `StationTriggerDistance` (1,500) of, once per system visit, with chance `StationEventChance` (50 %). | Cooldown, per-system cap. The Buyer station never triggers it. |

- Cooldown: Arrival and Station events wait `CooldownSeconds` (120 game s) after the last triggered event of any kind.
- Per-system cap: `MaxEventsPerSystem` (2) Arrival and Station events per system, all visits together.
- A trigger picks one eligible event by weight. Eligible means: the trigger matches, the event has not reached `MaxPerRun` (default 1), and its `Need` is met.
- Only one event page waits at a time. A trigger does nothing while a page waits.

### Choices and outcomes

- 1 to 4 choices. Each choice has an optional sure **Cost** (shown on the button and always paid), an optional **Need** (greys the button out with the reason, or hides it), and 1 or more weighted **Outcomes**.
- The game rolls the outcome when a player chooses. Weight = `Weight + WeightPerHeat × heat + WeightPerTech × fleet tech`, never below 0. The roll seed comes from the sim when the page opens, so all co-op clients get the same result.
- Outcome effects: money, money in tech value, fame (gain or loss), heat, reputation with the system's faction, the Buyer price, tech units (through the vaults), crew, vanilla resources, hull damage, spawned ships (from `spawns.rules`, some with a mission), story flags, and a follow-up page (`Next`).
- Needs can also check how old a flag is (`FlagAges`, in sectors) and how many sectors are left before the Buyer (`MinSectorsLeft`, `MaxSectorsLeft`). A cost can grow with heat (`MoneyPerHeat`).
- After the choice, the window shows the outcome text and one line per effect, in the colors of vanilla reward lines.

### Units and balance

- **Event money unit**: 1 = the middle of a standard mission's pay at the sector tier, times the economy reward factor and `RewardScale`. On the default economy it is about 10,000 credits at tier 1, a bit less than one Stolen Tech at the start (15,000). It grows with the tier, so late events stay relevant.
- **Fame unit**: 1 = a standard mission's fame. Fame adds heat through spec 25, like a mission.
- **Heat**: plain heat points. A stage starts every 25.
- **Resources**: `Amount + PerTier × (tier − 1)`.
- Rewards stay below a vanilla mission of the same tier (0.3 to 0.8 units) unless the player pays with risk or tech. An event takes seconds, not minutes, so it must not replace missions.
- A new career starts with about 10,000 credits after the starter ship. Costs at sector 1 stay at 0.5 units or less.
- Every choice that pays tech also needs 1 tech left over (`Need { Tech = 1 }` next to the cost), so a click never sells the last crate by accident. Fights can still lose it.

## 3. Catalog

Odds are at the default weights. "Sector" is the sector index (1 to 8). All numbers live in `mod/events/events.rules`.

### Implemented

#### 1. Clean Getaway? (Start, sector 1)
The intro: the lab alarms fade, six tagged crates in the vault.
| Choice | Cost | Outcome |
|---|---|---|
| Buy forged transponder codes | 0.5 money | Flag `fake_codes`: a hidden option at the Corporate Checkpoint. |
| Sell one crate to the fence | 1 tech | 1.2 × tech value. |
| Keep every crate | — | Nothing. |

Reads as: the run's first decision. Cash now, a trick for later, or the full score.

#### 2. Distress Beacon (Arrival, any sector)
| Choice | Need | Outcomes |
|---|---|---|
| Answer the call | — | 40 %: 0.4 money, 0.3 fame. 20 %: +1 crew (if a bunk is free), 0.3 fame. 20 %: pirate lure, 2 raiders (tier − 1). 20 %: a broken-down ship, page 24 (Dead in Space). |
| Scan the tender first | Sensor Array | 55 %: 0.5 money, 0.3 fame. 25 %: you spot the trap and leave. 20 %: Dead in Space. |
| Ignore it | — | Nothing. |

Reads as: classic FTL beacon. The Sensor Array turns a gamble into a safe gain.

#### 3. Running on Empty (Arrival, any sector)
A dry freighter wants hyperium.
| Choice | Cost | Outcomes |
|---|---|---|
| Give them the fuel | 20 + 5/tier hyperium | 0.4 fame, heat −10 (they send the corporation the wrong way). |
| Sell at triple price | same | 70 %: 0.8 money. 30 %: 0.8 money, heat +8. |
| Take their cargo | — | 60 %: steel and coil, heat +15. 40 %: 2 raiders, heat +10. |
| Leave | — | Nothing. |

Reads as: fuel is a currency. Kindness buys heat, greed buys money, piracy buys trouble.

#### 4. Corporate Checkpoint (Arrival, sector 2+, heat 10+; weight 8 + 0.15 × heat)
| Choice | Cost / Need | Outcomes |
|---|---|---|
| Transmit forged codes | flag `fake_codes`, hidden without it | Heat −20, flag cleared. |
| Private fee | 0.6 money | 80 %: heat −10. 20 %: heat +10. |
| Submit to the scan | — | Clean weight 60 − 8 × tech, caught weight 20 + 8 × tech (6 tech: 12 vs 68, so 85 % caught). Caught opens page 4b. |
| Run the checkpoint | — | 60 %: heat +15. 40 %: heat +10 and a corporate patrol. |

**4b. Caught** (follow-up): hand over 1 crate (heat +5), or break away (heat +20, corporate patrol of 2 hunters at the system tier).

Reads as: the more tech you hold, the less a scan can hide. Selling down makes this event safer.

#### 5. The Defector (Arrival, sector 3+)
Dr. Ines Varga, a corporate engineer in an escape pod.
| Choice | Outcomes |
|---|---|
| Bring her aboard | Weights 75 vs 25 + 0.2 × heat (at heat 50: 68 % vs 32 %). Good: +1 crew, heat −20, flag `defector`. Bad: a plant, heat +25, corporate patrol. |
| Turn her in | 1 money, heat +15. |
| Leave her | Nothing. |

#### 6. Varga's Old Office (Arrival, sector 5+, flag `defector`, 2+ tech; weight 30)
The defector's payoff, two or more systems later.
| Choice | Cost | Outcomes |
|---|---|---|
| Sell a crate through her assistant | 1 tech | 80 %: 1.6 × tech value, heat −10. 20 % + heat: heat +25, corporate patrol. |
| Clean the tags once more | — | Heat −20, flag cleared. |
| Too risky | — | Nothing. |

Reads as: an FTL quest chain. A choice in sector 3 pays off in sector 5.

#### 7. The Magpies (Arrival, sector 2+, 2+ tech)
A rival thief crew, Captain Rook, wants one crate.
| Choice | Cost / Need | Outcomes |
|---|---|---|
| Hand over a crate | 1 tech | Flag `magpies_paid`. |
| Pay in credits | 1.5 money | Nothing more. |
| Fake a vault breach | Sensor Array | 60 %: they leave. 40 %: page 7b. |
| Come and take it | — | The Magpies attack: one fleet at the system tier with the hunter AI (they go for the vault). A "Rival Crew" mission pays a bounty (1 standard mission) when the fleet is gone. |

**7b. Bluff called** (follow-up): hand over a crate, or fight.

Reads as: tech, money or blood. Vault damage destroys tech, so the fight risks the cargo.

#### 8. Freighter Under Fire (Arrival, sector 2+)
A freighter with failing shields and two raiders (tier − 1), 2,000 to 2,600 away.
- Help: a "Freighter Under Fire" mission with a 4-minute time limit. Defeat the raiders and keep the freighter alive. Pays 1.5 × standard money, 1 × fame and reputation with the freighter's faction.
- Stay out: nothing.

Reads as: FTL's station under siege. The time limit makes "move on or burn" a real choice; fame from the rescue adds heat.

#### 9. The Odd Merchant (Arrival, any sector)
| Choice | Cost | Outcomes |
|---|---|---|
| Buy the mystery crate | 0.5 money | 35 % processors, 20 % diamonds, 20 % +1 crew, 15 % scrap, 10 % a corporate tracker (heat +15). |
| Trade one crate for his hold | 1 tech | 0.6 money, processors and steel (scale with tier). |
| No thanks | — | Nothing. |

#### 10. The Fence (Station, 2+ tech)
| Choice | Cost | Outcomes |
|---|---|---|
| Sell one crate | 1 tech | 0.75 × tech value, heat −20. |
| Sell two crates | 2 tech | 1.5 × tech value, heat −35. |
| Not today | — | Nothing. |

Reads as: below market price, but the corporation chases the pieces. The heat drop is the product.

#### 11. Smugglers' Den (Station, sector 2+, heat 20+)
| Choice | Cost | Outcomes |
|---|---|---|
| New transponder and repaint | 1 money | Weights 85 vs 15 + 0.15 × heat. Good: heat −35. Bad: sold out, heat +10. |
| Trade a crate, no questions | 1 tech | Heat −50. |
| Leave | — | Nothing. |

#### 12. The Informant (Station, sector 2+)
| Choice | Cost / Need | Outcomes |
|---|---|---|
| Pay for the information | 0.3 money | Heat −15. |
| Lean on him | 3 crew | 55 %: heat −15. 45 %: heat +15. |
| Get lost | — | Nothing. |

#### 13. Derelict Survey Ship (Signal, any sector)
| Choice | Need | Outcomes |
|---|---|---|
| Salvage team | 2 crew | 50 % steel and coil, 25 % 0.6 money, 25 % −1 crew and some steel. |
| Cut it open from outside | — | 60 % steel, 40 % hull damage (30 % of max health on up to 6 parts). |
| Leave | — | Nothing. |

#### 14. Old Handshake (Signal, any sector)
A corporate authentication request, and the stolen tech hums in answer.
| Choice | Need | Outcomes |
|---|---|---|
| Let the tech answer | 1 tech, 1 vault room | 35 %: +1 tech. 35 %: a security drone (tier + 1, hunter AI). 30 %: heat +20. |
| Sell the recording | — | 0.3 money. |
| Move on | — | Nothing. |

Reads as: FTL's mysterious signal. The only event that can give tech back.

#### 15. Hunter Wreck (Signal, sector 3+)
| Choice | Need | Outcomes |
|---|---|---|
| Pull the flight recorder | 1 crew | 75 %: heat −25. 25 %: heat −10, hull damage 20 %. |
| Strip its armor | — | Steel and tristeel. |
| Leave | — | Nothing. |

#### 16. Ghost Signal (Signal fallback; weight 2, no run limit)
Debris only: sweep it for a little steel, or move on. A signal always shows something.

#### 17. Wanted Broadcast (Arrival, sector 2+, heat 60+; weight 6 + 0.1 × heat)
Every buoy plays your hull number and a reward. Local bounty hunters: 2 ships with local faction designs, named "Bounty Hunter ...", tag `hc_ev_bounty` (never `hc_hunter`), with a bounty mission that pays 0.75 × a standard mission.
| Choice | Cost / Need | Outcomes |
|---|---|---|
| Pay the broadcaster | 1 money | Heat −10. |
| Jam the buoy | Sensor Array | 70 %: heat −5. 30 %: bounty hunters. |
| Let them come | — | Bounty hunters. |
| Ignore it | — | Weights 50 vs 50 + 0.3 × heat (at heat 80: 40 % vs 60 %). Bad: bounty hunters. |

Reads as: high heat has a local price too, not only corporate waves.

#### 18. The Magpies Return (Arrival, 3+ tech, flag `magpies_paid` set 2+ sectors ago; weight 30)
Rook is back with two friends and wants two crates. Paying once made you a mark.
| Choice | Cost / Need | Outcomes |
|---|---|---|
| Pay two crates | 2 tech | The flag renews (the event opens once per run, so this ends it). |
| Hand over a crate that Varga rigged | flag `defector`, 1 tech, hidden without the flag | Rook's ship blows up: 0.5 fame, heat +5, flag cleared. |
| Pay in credits | 2 money | Nothing more. |
| Come and take them | — | The Magpies attack: one fleet at the system tier + 1, hunter AI, bounty 1.5 × a standard mission. Flag cleared. |

Reads as: an FTL extortion arc, and a payoff for the Defector chain.

#### 20. Refugee Convoy (Arrival, sector 3+)
Miners fleeing a corporate company town. The corporation searches every convoy.
| Choice | Cost | Outcomes |
|---|---|---|
| Take some aboard | — | +2 crew (if bunks are free), 0.3 fame, +1 reputation with the system's faction, heat +10. |
| Give fuel and food | 15 + 3/tier hyperium | 0.3 fame, +1 reputation. |
| Sell their position | — | 1 money, heat −15, −0.5 fame, −2 reputation. |
| Fly on | — | Nothing. |

Reads as: a moral choice with a heat cost on the kind side and a fame and reputation cost on the cruel side.

#### 21. Pirate Toll Gate (Arrival, sector 2+)
The twist: the pirates sell sightings to the corporation, so the toll grows with your heat. A vanilla pirate base wants to be destroyed; this gate wants to be paid, and the price is the bounty on you.
| Choice | Cost | Outcomes |
|---|---|---|
| Pay the toll | 0.3 + 0.01 × heat money (heat 50: 0.8) | Nothing more. |
| Pay double for a false sighting | 0.6 + 0.02 × heat money | Heat −20. |
| Run the gate | — | Heat +15, 3 pirates at the system tier (no bounty). |

#### 22. The Buyer's Agent (Arrival, 1 to 2 sectors before the Buyer, 1+ tech; weight 40)
An advance against the final sale. The run state keeps a Buyer price multiplier; the Buyer price and the score use it (spec 7, 84).
| Choice | Outcomes |
|---|---|
| Small advance | 1.5 × tech value now; the Buyer pays 80 % later. |
| Large advance | 3 × tech value now; the Buyer pays 60 % later. |
| Decline | Nothing. |

Reads as: the last hold-or-sell question. Money now buys repairs and escorts for the final jumps; the score pays for it. With 4 units delivered, the small advance costs about 1.2 tech values of score at the end.

#### 23. Courier Job (station mission offer)
Not an event page: a Hot Cargo mission in the station's mission list (spec 85). The first trade station that the players reach in a system offers it with chance `CourierChance` (50 %), once per system.
- The job: fly a sealed case to a neutral contact ("Quiet Contact", "The Night Ferry", ...) 8,000 to 12,000 away within 7 minutes, then collect the pay at the station.
- Pay: 1.25 × a standard mission's money, 0.5 × its fame. A quiet job: good money, little fame, so little heat.
- Contract terms (economy): free, flat heat 3, trail weight 0.5, informant chance 15 %.

#### 19. Vault Coolant Leak (Arrival, 2+ tech; category Unavoidable)
A ship event: the vault's coolant line ruptures. Every choice costs something (user decision: at most one Unavoidable event per run, tunable `MaxUnavoidablePerRun`).
| Choice | Cost / Need | Outcomes |
|---|---|---|
| Vent one crate | 1 tech (keeps the last one) | Nothing more. |
| Send a team in | 2 crew | 60 %: hull damage 15 %. 40 %: −1 crew and hull damage 15 %. Vaults are never damaged. |
| Emergency coolant from a station tug | 0.5 money | Nothing more. |

Reads as: FTL's ship fire. The player picks which resource to lose: tech, crew and hull, or money.

#### 24. Dead in Space (Arrival, any sector; weight 8; also a Distress Beacon outcome)
From Cosmoteer's Career 2.0 dev blog: a distress signal can be "a broken-down ship needing supplies for repairs or a tow to the nearest shipyard".
| Choice | Cost / Need | Outcomes |
|---|---|---|
| Hand over repair supplies | 25 + 5/tier steel, 5 + 1/tier coil | 0.5 money, 0.3 fame, +0.5 reputation. |
| Tow them to the nearest station | Tractor Beam Emitter | A Tow Job mission: pull the freighter (no AI, no drive) to the nearest trade station at least 3,000 away. Pays 1 × standard money, 0.75 × fame and reputation. Fails if the freighter is destroyed. |
| Strip their cargo | — | Steel, heat +10, −1 reputation. |
| Leave them | — | Nothing. |

Reads as: a use for the tractor beam, and a slow, visible job for a ship that wants to stay hidden.

All catalog items are implemented.

## 4. Content rules for authors

- Do not write counts that live elsewhere (the starting tech, spawn counts in spawns.rules): they drift. Use `{crates}` for the live fleet tech count. Counts that the same choice's Cost sets are fine.
- Do not number the choices: the UX area draws the key numbers next to the buttons.
- Keep text to 2 to 4 short sentences. Name concrete things (a crate, a cutter, a forger), not abstractions.
- Put the cost on the button (`Cost`), not in the text. The window adds "(-10,000 credits)" for you.
- Hide story options (`HideIfUnmet = true`) when a flag gates them. Show part and crew options greyed out, so players learn what to build.
- Give every event a safe "leave" choice as the last choice. The exception is the Unavoidable category (spec 87): put such events in `Category = Unavoidable`, so the per-run cap applies.
- Use `Need { Tech = 1 }` next to every tech cost.
- Weights that depend on heat or tech make the core loop visible: more tech held, worse scans; more heat, more traps.
- `mod/events/events.rules` changes need a game restart. Validation runs at the first event and shows errors in the overlay.

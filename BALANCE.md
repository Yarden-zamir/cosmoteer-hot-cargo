# Hot Cargo balance

A balance audit of Hot Cargo against vanilla Cosmoteer 0.30.5a, and the tuning that follows from it.
`docs/balance.html` shows the same numbers as charts. `spec/35-balance.md` (requirements 300 to 304) holds the rules.

## Method

- **Vanilla rules.** `tools/balance/model.py` reads the game data: `TierValueMaximums` and the standard money and fame tables (`modes/career/tier_tables.rules`), the Fast economy (`modes/career/career.rules`) and every blueprint cost (`modes/career/techs.rules`).
- **Measured.** Two game scripts in `tests/balance/` run headless on the PC:
  - `balance-census.txt` follows the main route for 7 sectors. In each system it loads every station, then logs each offer (money and fame in standard mission units, contract terms), the station fuel stock and the map distances.
  - `balance-hunters.txt` spawns 3 hunters at each tier, logs their mass, value and fuel, destroys them and logs the salvage value.
- **Model.** The script models one run with the measured mission mix: 6 missions and 17.5 minutes per sector on the main route (tiers 1, 3, 5, 7, 9, 11, 12, 12), 8 tech units held, 1.5 hunters destroyed per sector (the Warning scouts). The logs are in `tools/balance/logs/`. To refresh:

```
HOT_CARGO_SLOTS=8 scripts/autotest.sh tests/balance/balance-census.txt 500 > census.log
HOT_CARGO_SLOTS=8 scripts/autotest.sh tests/balance/balance-hunters.txt 300 > hunters.log
uv run --no-project tools/balance/model.py ../.game/Data <census before> <hunters> <census after>
uv run --no-project tools/balance/render.py
```

Units: **std** = one standard mission's money (or fame) at the sector tier, with the economy factors and `RewardScale`. **TVM(T)** = the vanilla ship value maximum of tier T (tier 1: 60,000; tier 7: 230,000; tier 12: 800,000).

Limit: the model is a single representative run. It does not model travel time, failed missions or ship losses. The telemetry branch (`feat/telemetry`) records real runs; check these numbers against it when it lands.

## Findings

### 1. Rewards and `RewardScale`

- A mission offer pays 3.6 std money on average (deliveries left out). Bounties pay 2.1, faction hunts 3.6, pirate base attacks 6.5.
- With `RewardScale` 2 on the Fast economy, six missions pay about **4 × TVM(T) per sector**. A fleet of even power needs about 0.5 to 0.75 × TVM(T) per sector to follow the tier ramp. So money was 5 to 8 times the need, and the bank grew to about 12 million by the Buyer.
- With `RewardScale` 1 (the vanilla Fast rates), six missions pay about **2.2 × TVM(T) per sector**: about twice the need. The margin pays for repairs, a lost ship, blueprints and fuel.
- Hunter wrecks already pay: a destroyed hunter drops about **10 % of its ship value** in salvage (measured 9 % to 10 % at every tier, Fast economy). That is about 1 std at `RewardScale` 1.
- **Change:** `RewardScale` 2 → **1**. The Normal and Slow economies now make a tighter run, so the economy choice matters again.

### 2. Money against ship power

| Sector | Tier | Income (before) | Income (after) | Income ÷ TVM (after) | Tech unit ÷ TVM | Tech unit in typical missions (after) |
|---|---|---|---|---|---|---|
| 1 | 1 | 214,000 | 107,000 | 2.1 | 0.32 | 1.1 |
| 3 | 5 | 536,000 | 268,000 | 2.2 | 0.45 | 1.4 |
| 5 | 9 | 1,501,000 | 750,000 | 2.2 | 0.59 | 1.9 |
| 7 | 12 | 3,109,000 | 1,554,000 | 2.2 | 0.72 | 2.2 |

- **Tech sale value** follows the spec targets with the new heat: 0.32 of a ship at the start, about 0.5 mid-run, 0.79 at the Buyer. One or two units still buy a ship of even power. The tech value does not use `RewardScale`, so a sale is now worth 1 to 2.4 typical missions (was 0.6 to 1.2). Selling is a real bail-out again.
- **Contract fees** are 0.3 to 0.8 std, so they scale with `RewardScale` and keep their weight. Starting money after the starter ship is 10,000 = 2 std at tier 1: enough for 2 to 6 early fees, or 2 to 4 early blueprints. No change.
- **The Buyer factor 4.** At the Buyer one unit is worth 4 × 0.79 × TVM(12) ≈ 2.5 million. Eight held units are 20 million, the bank about 6 million. So the score rewards the tech that the players bring, and money is the tie-breaker. A unit sold at sector 1 for 19,000 costs 2.5 million of score. That is the "hold or sell" pillar. No change.

### 3. Blueprint costs

- Vanilla blueprints: 95 techs, base techs 546,500 in total, overclocks 4,195,000. Against the old income (13 million a run), all base techs cost 4 % of a run. They were not a decision.
- **Change:** new tunable `Journey.TechCostScale` = **2.5** (requirement 301). Base techs now cost 1.37 million, about 19 % of a run's income (7 million). Overclocks cost 10.5 million, more than a run earns, so a player picks a few. Early blueprints stay cheap: a basic weapon or the small hyperdrive costs 2,500, half a tier 1 mission.

### 4. Heat budget and mission density

**Bug:** the vanilla fame cap hid heat. A mission or a kill pays fame only up to the MaxFame of its tier (tier 1: 40, tier 5: 310, tier 12: 2,510), and heat came from the fame gained. With `RewardScale` 2 the players reached the cap after 2 or 3 missions in most sectors. After that, missions added no heat at all. Before the cap, one typical mission (3.4 std fame) added 3.4 × 15 × 1.2 = 61 heat, so the Strike came after 1 or 2 missions. **Change:** heat counts the uncapped fame (requirement 302).

A typical mission pays 3.4 std fame (median), not 1: vanilla missions pay the kill fame of their targets and their own fame reward. The old `HeatPerStandardMission` 15 assumed 1.

Heat sources and sinks in one sector (8 units held, multiplier 1.2, after the change):

| Source or sink | Rule | Heat per sector |
|---|---|---|
| Carried in | a jump removes 75 % (watched relay), 85 % (clear relay) or 90 % (blind) of the heat (requirement 650) | about +25, +15 or +10 |
| Missions | 6 × 3.4 std fame × 1.425 × 1.2 | +35 |
| Time | 17.5 min × 1.14 × 1.2 | +24 |
| Contracts | 6 × (flat heat 1.5 + informant 0.75 × 10 % × 15) | +16 |
| Trail | open weight above 3 | about 0 (a player keeps 2 to 3 open) |
| Events | outcomes −50 to +25, chosen by the player | about 0 on average |
| Convoy cover | −8 per completed Convoy Duty (45 % of systems) | −4 on average |
| **Total at the end of the sector** | | **about 96: the Strike after about 6.4 missions in 18.5 minutes** |

Heat stages and missions. One typical mission adds about 11.8 heat: 5.8 from fame, 2.7 from its contract, 4.0 from its 2.9 minutes, −0.7 from convoy cover. Missions before a stage = (stage heat − carried heat) ÷ 11.8. The charts in `docs/balance.html` leave out convoy cover, so they show the Strike after 6.0 missions, not 6.4. The jump kind sets the carried heat (requirement 650): a jump from heat 100 keeps 25, 15 or 10.

| Stage (heat) | Watched relay (75 % cut, 25 kept) | Clear relay (85 % cut, 15 kept) | Blind jump (90 % cut, 10 kept) |
|---|---|---|---|
| Warning (40) | 1.3 missions, 4 min | 2.1 missions, 6 min | 2.5 missions, 7 min |
| Hunt (75) | 4.2 missions, 12 min | 5.1 missions, 15 min | 5.5 missions, 16 min |
| Strike (100) | 6.4 missions, 18.5 min | 7.2 missions, 21 min | 7.6 missions, 22 min |
| With no missions: Warning, Hunt, Strike | 11, 37, 55 min | 18, 44, 62 min | 22, 48, 66 min |

- Three stages give three clear steps: scouts (Warning), a real fight (Hunt), then repeat waves until the players jump (Strike). Warning is above the highest carried heat (25), so the scouts always come after the players arrive. Hunt starts with the station markup (75, requirement 655).
- Before the 5 % cut, the Strike came after 6.1, 6.9 and 7.3 missions. The cut gives about 0.3 more missions in each sector. A very fast player who picks low-fame missions (bounties, 2.5 std fame) does about 8.
- A clear relay and a blind jump are earned (a relay without hunters near it, or 1.5 × fuel, a slower spin-up and a damage risk), and they stay inside "about 6, a fast player more".
- **Changes:** `HeatPerStandardMission` 15 → **1.5** (per std fame unit). `HeatPerGameMinute` 2 → **1.2**. `EscortHeatChange` −15 → **−8** (the old value was one old mission; it would now be three). Later: both heat rates −5 % (`HeatPerStandardMission` **1.425**, `HeatPerGameMinute` **1.14**), and 3 heat stages in place of 4 (Patrol removed).
- **Boards.** Each system offers 20 to 26 missions on its station boards (about 4 per station), plus 50 galaxy "visit" offers in a separate list. Hot Cargo adds 4 to 6 of them; the largest share was story chains (2 to 3 per system, one at every station). With 6 missions per sector, a board of about 3 × 6 = 18 to 24 offers is enough choice. **Change:** chains start at 60 % of stations and at most 2 per system (`Chains.StationChance` 1 → 0.6, new `Chains.MaxNewChainsPerSystem` = 2). Measured after: 0 to 2 chains and 3 to 4 Hot Cargo offers per system, 20 to 25 offers in total. Mechanics missions already average under 1 per system (they need asteroids, storms or a single sun). No other cut.

### 5. Fuel

- **Every jump to another system costs fuel.** The ships fly to a relay (gate), and a human ship needs FTL drives and the map jump fuel (`FtlManager`: the spin-up checks `GetTotalFtlMapJumpFuelCost` for a gate jump of a human ship). Cost: about mass × light years ÷ 1,000 (vanilla). A blind jump (factions, requirement 651) costs 1.5 ×. So fuel is the travel currency and the escape from a Strike, and a fleet without fuel is stuck.
- Vanilla fuel was not a decision: a tier 12 jump cost about 17 fuel = 850 credits, 0.2 % of a mission. Stations sold 180 to 1,900 fuel per system.
- **Changes** (requirement 300, `mod/balance/actions.rules`): price 50 → **150**; stack 20 → **10** (a tile or a crew trip holds half); station stock 80–90 % → **25–40 %** of typed fuel tiles and 0–10 % → **0–2 %** of untyped tiles; jump fuel cost **× 2**; hunters carry **60** fuel (was 40).
- Vanilla stations hold less than their trade target: after the target, `CareerSimModeManager.OnShipSpawned` scales every NPC fuel storage by 25 % to 75 % (`NpcSpawnedResourceFractions`).
- Measured after, same map seed, before the floor below: stations hold **19 to 300 fuel per system** (was 180 to 1,940). A tier 12 jump of 7.5 LY costs about 33 fuel at full drive efficiency (more with poor drive coverage). A destroyed hunter drops about 30 fuel, a salvaged wreck 60. So late in the run a station sells 1 to 9 jumps, and each hunter kill pays about one jump.
- **Fuel floor** (requirement 304, `Balance.StationFuelPerMillion` 100, `StationFuelMinimum` 10): every trade station keeps at least one worst-case relay jump for sale: a fleet of the next tier, 10 LY, 70 % drive efficiency. That is 10 fuel at tier 1, 30 at tier 7 and 100 at tier 12. A system has 2 or 3 trade stations (the start 1), so it always sells enough for the next jump, and the random stock adds more. Trade ships do not take the floor away; players can buy all of it. So a run never gets stuck on fuel, and fuel above the floor (an escape, a blind jump, a spare) still comes from hunters, mining and lucky stations.
- Money is not the limit (a jump costs about 2 % of a mission); the stock is. Hunters and hyperium asteroids are now the best sources above the floor.
- Factions (requirements 655 and 657): from heat 75, stations charge up to 2 × for fuel, and from heat 90 each station refuses with chance 0.5. A player who stays until heat 90 can lose the floor of a refusing station. Buy the next jump before the Hunt stage (75).
- Hyperium events cost 20 + 5 per tier fuel (80 at tier 12, about 2.5 jumps). That is now a real price. No change.

### 6. System scaling

- Measured run map (`maptiers`): by jump distance the low tiers are 1 | 1–3 | 3–5 | 6–7 | 9 | 10–11 | 10–12 | 9–12. The main route reaches tier 12 after 6 jumps. `HotCargoTierCap` holds for all normal systems.
- **Bug:** faction home systems (tier 13 or more) keep their tier and can sit 2 jumps from the start. A short visit raised the peak tier from 5 to 13 and so the tech value four-fold (and the Buyer score with it). **Change:** the peak tier counts a system only up to start tier + 2 × (sector index − 1) (requirement 303). A home system stays dangerous: its hunters use its own tier.
- The new economy keeps a fleet of tier T + 1 affordable in each sector (income about 2.2 × TVM(T), fleet step about 0.5 to 0.75 × TVM(T)), so the +2 tier step per jump fits.
- **Danger at the Buyer:** hunters used the tier of the current system, so a player who took low side routes met weak hunters late, and could meet the Buyer in a tier 9 or 10 system. **Change:** hunters (every stage response and strike wave) start from at least the main route tier of the sector index, the same route tier as the peak tier cap, up to the final sector tier (`Hunters.RouteTierFloor`, requirement 305). Missions in a low side system still pay its lower tier, and the peak tier (so the tech value and the score) stays low, so a side route is now the harder and poorer path, as in FTL.

### 7. Hunter bounty

- Hunters give no fame (no kill fame, measured 0 at every tier) and no money reward, but their wrecks pay about 10 % of their value in salvage (1 std at `RewardScale` 1), and 30 to 60 fuel.
- **Recommendation: no bounty.** Kill fame would add heat, so a fight would call the next wave (a loop). A money bounty would make repeat Strike waves (every 180 s, up to 12 hunters) a farm. Salvage already pays, and it takes time to collect under pressure, which is the trade-off we want.

## Changes

| Tunable or rule | Before | After | Requirement |
|---|---|---|---|
| `Journey.RewardScale` | 2.0 | 1.0 | 4 |
| `Journey.TechCostScale` (new) | 1 (vanilla) | 2.5 | 301 |
| `Heat.HeatPerStandardMission` | 15 | 1.425 | 25 |
| `Heat.HeatPerGameMinute` | 2 | 1.14 | 24 |
| `Heat.Stages` | Warning 25, Patrol 50, Hunt 75, Strike 100 | Warning 40, Hunt 75, Strike 100 | 30 |
| Heat for fame above the cap | none | uncapped fame heat | 302 |
| Peak tier | any system tier | at most start tier + 2 × (sector − 1) | 303 |
| `Missions.EscortHeatChange` | −15 | −8 | 36 |
| `Chains.StationChance` | 1 | 0.6 | 160 |
| `Chains.MaxNewChainsPerSystem` (new) | no limit | 2 | 160 |
| `Hunters.MinFuel` | 40 | 60 | 192 |
| `Hunters.RouteTierFloor` (new) | system tier | at least the route tier of the sector index | 305 |
| Hyperium price | 50 | 150 | 300 |
| Hyperium stack | 20 | 10 | 300 |
| Station fuel stock (typed, untyped) | 80–90 %, 0–10 % | 25–40 %, 0–2 % | 300 |
| Jump fuel cost (map, local) | 1 per 100 mass per 10 LY, 25 km | per 5 LY, 12.5 km | 300 |
| `Balance.StationFuelPerMillion`, `StationFuelMinimum` (new) | no floor | 100 per million of the next tier's ship value, at least 10 | 304 |
| Convoy reward sort value | int.MinValue (the sum wrapped) | 0 | — |

## Dependencies on other areas

- **Run modes (spec 680 to 683):** the difficulty table multiplies these defaults at each read (heat rate, hunter ships and tiers, fees, tech value). The vanilla defaults (Easy, Fast, Standard) are 1, so the numbers here hold for them. On the Normal economy, missions pay half of Fast, fees cost 1.2 × and the tech value is 0.9 ×: income is then about 1.1 × TVM(T) per sector, close to the need.
- **Vault (Tech Vault to 1 tile, 2 units per vault, 4 vaults at start):** the start stays at 8 units, so the heat multiplier (1.2) and the budget above hold. If the start changes, check `Heat.HeatMultiplierByUnits[start units]`: the budget scales with it.
- **Scaling (enemy count multiplier for co-op):** more hunters per wave means more salvage and fuel, and more danger. The heat budget is per sector, not per player, so co-op income per player drops (rewards split). Revisit `RewardScale` for co-op with telemetry.
- **AI2:** the salvage numbers assume destroyed hunters. Hunters that flee (requirement 195) take their fuel and drop nothing.

## Open questions

1. Resolved: hunters use a floor of the main route tier (requirement 305). The fame title prototype of run modes is removed.
2. Co-op: rewards split between players, but heat and hunter counts do not. Telemetry should show if co-op needs a higher `RewardScale`.
3. The model assumes 6 missions in 17.5 minutes. Telemetry should give the real mission time and the real share of base attacks (the biggest fame and heat).

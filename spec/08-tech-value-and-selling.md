## 8. Tech value and selling

18. Players sell tech through the normal station trade screen. The mod adds no special sell screen.
19. Tech value = ship value × (`TechShipFraction` + `TechShipFractionPerGameHour` × game hours + `TechShipFractionPer100Notoriety` × notoriety ÷ 100), rounded to 100.
    - Ship value = the vanilla `TierValueMaximums` entry (`Data/modes/career/tier_tables.rules`) of the peak tier: the highest low tier of a system that the players were in. It is the most that a ship of that tier is worth.
    - Notoriety = the sum of every heat gain in the run (time, fame, contracts, events, debug keys), times `Factions.AllyNotorietyFactor` in corporate ally space (requirement 642). Heat losses do not lower it. The run state saves it.
    - Game time, notoriety and the peak tier never go down, so the tech value never goes down. Exception: a lower economic difficulty factor set mid-run (requirement 683). Time and notoriety count in whole steps (`PriceStepMinutes` 1 min, `NotorietyStep` 5), so a sale in flight sees one price.
    - Defaults 0.25, 0.10 and 0.05. Purpose: 1 or 2 units pay for a ship of even power for the current stage. Examples: start (tier 1) 15,000, a quarter of a tier 1 ship. Mid-run (tier 7, 1.2 h, 250 notoriety) about 114,000 = 0.5 × 230,000, so 2 units buy a full tier 7 ship. Late (tier 12, 2.5 h, 600 notoriety) 640,000 = 0.8 × 800,000. A later sale pays more.
    - Against rewards: a standard mission pays about 0.05 × the tier's ship value × the difficulty money factor × `RewardScale`. On the default Fast economy (2×) with `RewardScale` 1 that is about 0.1 × ship value, so one unit mid-run is worth about 5 standard missions, or about 1.5 typical missions (a typical offer pays 3.6 standard missions, BALANCE.md). Ship prices do not change with difficulty, so the tech value does not use the money factors.
    - The peak tier counts a system's tier only up to the cap of requirement 303, so a short visit to a faction home system does not multiply the tech value.
20. The trade screen shows the current tech value as the resource price.
21. Trade stations buy tech. Stations do not sell tech. A station stocks only the resources in the `StationResourceTradeDeltas` lists of the sector rules, and tech is in none of them. Stations take sold tech into free untyped storage tiles. Players can buy back tech that they sold, at `BuyBackFactor` (default 1.5) × the tech value.
22. Tech value is computed from simulation state only, so it is the same on all multiplayer clients.

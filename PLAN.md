# Plan: hulk loot in the world

Abandoned Ship and Cold Hulk dialogs go. The outcome is real cargo in the hulk.

Decisions:
- `mod/events/hulks.rules` lists hulk kinds (ID, Weight, MaxPerRun, approach Text) and their loot outcomes (Weight, WeightPerHeat, Cargo, reveal Text, Blast). Old weights.
- The sim tick sees a new live vanilla abandoned ship (not the relic): with chance `Events.HulkLootChance` (0.6, the old event chance) it rolls a kind and an outcome (sim random generator, heat at that moment). It clears the vanilla cargo and puts the outcome's cargo in the hulk's storage. Its stasis spawner keeps the cargo (`RememberResourcesAndCrew`).
- Approach (`AbandonedTriggerDistance`): a center message and chat line with the kind's text, once per hulk.
- Reveal: when the hulk loses its first storage part (shot or salvaged) or is gone, a message names the outcome, once.
- Corporate trap: a Tracer Beacon among bait steel. Shots destroy a beacon (drop rate 0); careful salvage drops it.
- Cold Hulk "reactor coughs": less hyperium, and at the reveal its tanks blow: hull damage (0.2 of max health) to the 6 nearest parts of each player ship within `HulkBlastRadius` (300).
- `EventTrigger.Abandoned` and the two events go. `AbandonedEventChance` is renamed `HulkLootChance` (breaking for a tunables file that sets it).

Steps:
1. Rules file and loader with validation.
2. Stock, approach, reveal, blast.
3. Tests: `events-abandoned` reworked, screenshot of drops.
4. EVENTS.md 30, 31; spec 40 (764), spec 46 (1124 to 1126), default tunables, PLAYTEST.md.

# Hot Cargo system archetypes

Spec: `spec/27-system-archetypes.md`, requirements 220 to 229.

Cosmoteer's Career 2.0 dev blog ([Steam news](https://store.steampowered.com/news/app/799600/view/698779962638862457)) splits star systems into sector types. Hot Cargo takes the part that fits a short, tense run: each system gets an identity, one hazard and one mission that uses the hazard. Archetypes add no jumps and no long flights. The objects sit inside the smaller Hot Cargo systems.

## Archetypes

| Archetype | Sector ID | Weight | Hazard | Mission |
|---|---|---|---|---|
| Trade Hub | `hc_trade_hub` | 1 | A current: a cloud band that pushes ships and loose items one way | none of its own; the hub world sits next to a trade station |
| Mining Belt | `hc_mining_belt` | 1 | A dense rock belt with 3 or 4 cleared passes | Hold the Pass |
| Solar Zone | `hc_solar_zone` | 0.8 | The vanilla sun heat zone | Sundive (and the gameplay area's Sunside Ore) |
| Deep Space | `hc_deep_space` | 0.8, tier 3+ | Dense clouds and ion storms | Raid the Lair |
| Vanilla types | `basic`, `binary`, `high_density_nebula`, `supernova`, `electron` | 0.5 to 0.1 | vanilla | vanilla |

With the defaults, about 3 of 4 systems are archetypes. The start system stays basic. A test map had 11 trade hubs, 9 mining belts, 8 solar zones and 11 deep space systems among 50.

## How it is built

- **Sector types** (`mod/worlds/sectors.rules`): each archetype is the vanilla basic sector with spawn flags. The basic sector holds every vanilla and Hot Cargo spawner, so missions, events, chains and contracts of the other areas also appear in archetype systems.
- **Weights** (`mod/worlds/archetypes.rules`): the vanilla `RandomSectorTypes` spawner. `mod/journey/galaxy.rules` points to it. The weights are rules values, not live tunables: the map is made once per career.
- **Spawners** (`mod/worlds/sysgen_worlds.rules`): one group per archetype, each gated by its spawn flag. The file runs at the end of the basic sector generator.
- **Galaxy map and arrival**: the vanilla tooltip shows the sector type name. The mod adds the hazard line under it, and on the first arrival a center text ("Trade Hub", the hazard below it) and a chat line ("Trade Hub. A current runs through the system. Ships in it drift downstream.") show it.

## Mechanics

### The current (Trade Hub)

- Shape: the C# spawner `HotCargoCurrent` paints a straight band of nebula triangles (6,000 × 2,400, about 11 triangles) along the system's flow direction. A band reads like a river. A playtest found the old band (18,000 × 4,500) too big. The nebula grid (triangle side 2,000) sets the minimum width: under about 2,300, diagonal bands break into patches. The size does not follow the system scale (spec 105).
- Direction: one of 8 directions from the map node seed. Nothing extra is saved, and every co-op client gets the same direction.
- Push: the nebula effect `HotCargoCurrentPush` runs in the vanilla nebula tick, like the ion storm lightning. Acceleration = `CurrentAccel` × cloud strength × how far the ship's speed along the flow is below `CurrentFlowSpeed`. Full push at rest or upstream, none at flow speed or faster. The force scales with mass, so all ships drift alike.
- Exit drift: the same rule pushes across the flow, toward the nearer side of the band, with `CurrentSideShare` (0.25) of the push and of the flow speed. The drift leans atan(0.25) ≈ 14° outward. From the band center, the side is 1,200 away and the end 3,000: a share above 0.4 makes drift from the center leave at the side. With 0.25, drift from the center leaves at the far end, and drift from more than about 450 off the axis leaves at its side. The share was 0.5 (27°) until a playtest asked for a gentler sideways push. The band axis is the mean of the current triangles (one current per system).
- Items: loose nuggets (resources, loot, tech crates) in the cloud get the same push, and the vanilla nugget drag (0.5 per second) does not apply there, so they move like ships without engines. With the drag, the push fades in the soft cloud edge and items stopped there. Outside the cloud the drag stops them again. Junk capsules and wrecks are ships and get the ship push.
- Exceptions: stations, asteroids and megaroids do not drift.
- Look: the vanilla cloud with a teal tint and full sight, plus arrows that drift in the drift direction (they lean toward the nearer side). The vanilla nebula HUD shows "current" while a ship is inside.
- Found in tests: an idle player ship brakes against the drift and settles at about 5.5 units per second downstream; from the middle of the band it still drifts out, in about 400 s of game time. A tech crate dropped near the band axis leaves near the far end in about 67 s, 3,340 along the flow from the center and 740 across (side share 0.25). With the old share 0.5 it left at the near side in about 57 s. A ship that maneuvers gets the full push.

### Dense belt and passes (Mining Belt)

- About 1,140 to 1,280 rocks (mostly L and XL, some XXL and megaroids) in a band 900 wide in the middle between the trade station ring and the FTL gates (about 8,200 from the center at `SystemScale` 0.5). The count follows the orbit length. That is about one large rock every 195 units: small ships thread it, big ships hit rocks.
- 3 or 4 passes: ring markers of radius 800 that the rocks keep clear of.
- The belt lies between the stations and the FTL gates, so the passes matter for the route in and out.

### Heat zone depth (Solar Zone)

- The damage is vanilla (`PlanetDoodad.ProximityEffects`): death inside the kill radius (1,500), heat out to the zone edge (3,750), stronger toward the sun.
- The mod measures depth: 0 at the edge, 1 at the kill radius. A HUD line shows the deepest own ship and its depth in any system with a sun.
- Found in tests: the heat status of an unshielded starting ship grows about twice as fast at depth 0.35 as at depth 0.1. The vanilla heat is harsh on cargo: at depth 0.5 a Tech Vault loses all 6 units in about 2 s; a 6 s dive at depth 0.19 cost 3 of 6 units. Shields take a tenth of the heat.

### Storm eye (Deep Space)

- The lair group paints its own ion storm, then clears an eye of radius 1,800 with an empty nebula. The lair is the vanilla pirate base shape, hidden until discovered, in the eye.
- The players must cross the storm (lightning) to reach the lair.

## Missions

| Mission | Steps | Fails when | Pay | Contract terms |
|---|---|---|---|---|
| Hold the Pass | Defeat the campers in a pass. Hold the pass ring for `PassHoldSeconds` (120 s); a wave comes every `PassWaveSeconds` (35 s) of holding. | The ring stays empty for `PassLostSeconds` (15 s) after the first entry | 2 × money, 2 × fame | free, heat 6, trail 1, informant 10 % |
| Sundive | Fly to the wreck at depth 0.15 to 0.25. Stay `SundiveSalvageSeconds` (5 s). Leave the heat with the same ship. | The carrier is destroyed | 3 × money, 1.5 × fame | fee 0.3, heat 2, trail 0.5, informant 5 % |
| Raid the Lair | Find the lair in a search circle of radius 4,500 (come within `LairDiscoverDistance`, 2,000). Destroy it. | (vanilla) | 2.5 × money, 2 × fame | free, heat 4, trail 1, informant 20 % |

Pay is in standard mission units at the system tier, before `RewardScale`. Every mission ends with the vanilla "receive rewards" step at the station.

## Hooks for other areas

- Spawn flags `hc_trade_hub`, `hc_mining_belt`, `hc_solar_zone`, `hc_deep_space`: put `Conditions { RequireSpawnFlags = [hc_solar_zone] }` on a spawner to place it only in that archetype.
- Tags: `hc_pass` (pass rings), `hc_belt_rock`, `hc_hub_planet`, `hc_lair`, `hc_sundive_site`, `hc_current`.
- The Sunside Ore seams (gameplay area) sit on any single sun, so they appear in Solar Zones without a Worlds change.

## Tests

- Unit (`tests/HotCargo.Tests/WorldsModelTests.cs`): flow directions, push, outward side, near-side exit of a ship and an item, heat depth, pass hold steps, waves, band distance.
- Game (`tests/autotest/worlds-*.txt`): map weights and hazard tooltip, current drift (and none beside it), a crate that leaves at the near side, a ship that drifts out, belt and passes, pass hold win and loss, heat depth, sundive, lair discovery, save and load.
- Co-op (step `worlds.current` in `tests/autotest-mp/coop-combat.txt`): a current painted on both machines pushes the host ship and a dropped crate the same way, with no desync.

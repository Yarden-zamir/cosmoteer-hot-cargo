# Performance plan (feat/perf) — PAUSED (feature freeze)

Goal: lower the frame cost of Hot Cargo code on every machine, because co-op lockstep waits for the slowest client.
Spec: `spec/42-performance.md`, requirements 870 to 873 so far (range 870 to 889).

## Decisions
- Measure with `selfprof` (sim tick and frame time of every Hot Cargo entry point) and `frameprof` (draw-time frame hooks, called once per frame headless; time and bytes per frame).
- Baseline worktree `perf-base` (local, never pushed) = harness commit f2759b1 + `frameprof` commit + origin/main d00e1fe. Its `src` differs from `feat/perf` only by the fixes. Run before and after at the same time, so PC load is equal.
- Co-op scripts use line filters (`@3p`, `@c1`) where a 3-player game differs.
- A 3-player test needs 3 slots. While the player plays, an agent gets 2 slots, so `mp3:` tests run only while the player does not play.
- Behavior stays identical in the sim; HUD refresh rate is a tunable (`Ux.HudRefreshSeconds`).

## Baseline (single player, Strike, 3-player enemy scale, ~230 ships with 60 wrecks; perf-base before the d00e1fe merge)
- Sim: Hot Cargo 0.423 ms per tick, 11.9 % of the tick (tick mean 3.55 ms). Top: Run.OnTick 249 µs incl. HunterSpawner.OnTick 140 µs (spawns).
- Frame: EventFramePatch 48 µs every frame (event page built each frame). Blip postfixes ~0.5 µs per ship, ~700 bytes per ship per frame. Map node tooltip postfix 6.3 µs per node.
- Telemetry 0.093 % of wall time.

## Steps
- [x] 1. 3-player co-op harness (`PLAYERS=3`, `mp3:<name>`, line filters) and `selfprof`.
- [x] 2. Per-tick trait cache for personalities (spec 870).
- [x] 3. Baseline numbers, single player (above). `frameprof` and `wrecks` test commands.
- [x] 4. Frame code: comms row and hire tab stable text (871); HUD texts and event dialog at `HudRefreshSeconds`, blips, crates, edge labels and map tags without closures or LINQ (872).
- [x] 5. Sim: `SimQueries.TaggedShips` filters before it sorts (873). Hunter spawns are the rest of the tick cost; no other hot spot found.
- [x] 6. Network volume: checked. Presence 0.15 s on change and 1 s heartbeat, hail slider 0.25 s, lobby slider one message per 0.1 step. Nothing per frame.
- [ ] 7. After numbers: run `telemetry-stress` in `perf-base` and `perf` at the same time; compare SELFPROF and FRAMEPROF lines.
- [ ] 8. 3-player runs (only while the player does not play): core, save, trade, events, sharedchoice, diplomacy, factions, timer, win, telemetry-stress. Done: `mp3:ai` passed. Then 2-player runs of the same tests (scripts changed for events, diplomacy, trade, timer, factions, sharedchoice).
- [ ] 9. `scripts/autotest-affected.sh` (Core and scripts changed, so the full suite).
- [ ] 10. Docs: "slow co-op client" settings for hosts (TUNABLES.md): `Ux.HudRefreshSeconds`, Ux switches (TechMarkers, StationBadges, DamagePings, EdgeLabels), `Worlds.CurrentArrows`, enemy scale slider, `Hunters.MaxLiveHunters`, client graphics settings.
- [ ] 11. Screenshot of the comms list badges (stable text, spec 871) in the main slot.
- [ ] 12. Merge origin/main, delete this file, PR body with numbers.

## Merge notes (files of other agents)
- `Ui/HeatPanel.cs` (pursuit): text refresh behind `UxKit.HudDue`.
- `Events/EventsChoiceDomain.cs` (ui): event dialog update behind `UxKit.HudDue`.
- `Core/SimQueries.cs`: TaggedShips sort. `Tech/TechMarkers.cs`, `Ux/EdgeLabels.cs`, `Factions/MapPatches.cs`, `Diplomacy/DiplomacyMarkers.cs`: no closures or LINQ.
- Spec file `spec/42-performance.md`: section 42 may collide with another new section number.

# Performance plan (feat/perf)

Goal: lower the frame cost of Hot Cargo code on every machine, because co-op lockstep waits for the slowest client.
Spec: `spec/42-performance.md`, requirements 870 to 889.

## Decisions
- Baseline worktree `perf-base` = f2759b1 + local main + the `frameprof` commit. Both sides include main, so only our fixes differ.
- Co-op scripts use line filters (`@3p`, `@c1`) where a 3-player game differs.
- Measure with `selfprof` (Telemetry/SelfProfile.cs): it times every Hot Cargo entry point, split into sim tick time and frame time outside ticks.
- Baseline from worktree `perf-base` (commit f2759b1), so edits in `perf` do not change the baseline.
- Headless slots draw nothing: draw-only hooks (overlays, `GetNonMouseoverText`, `OnBeforeDraw`) show no time there. Fix them by review: refresh text at a few Hz, cache strings, no LINQ or allocations per frame.
- Behavior stays identical; UI refresh rates are tunables.

## Steps
- [x] 1. 3-player co-op harness (`PLAYERS=3`, `mp3:<name>`) and `selfprof`.
- [x] 2. Per-tick trait cache for personalities (spec 870).
- [ ] 3. Baseline numbers: `telemetry-stress`, `mp3:telemetry-stress`.
- [x] 4. Frame code: comms row stable text (871), HUD refresh rate and allocation-free blips (872). Measure with `frameprof`.
- [ ] 5. Sim hot spots from the profile.
- [x] 6. Network volume: checked. Presence 0.15 s on change and 1 s heartbeat, hail slider 0.25 s, lobby slider one message per 0.1 step. Nothing per frame.
- [ ] 7. 3-player runs: core, save, trade, events, sharedchoice, diplomacy, factions, ai, timer, win. Fix desyncs.
- [ ] 8. After numbers, host settings for a slow client, spec, merge origin/main, delete this file.

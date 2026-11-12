# Performance plan (feat/perf)

Goal: lower the frame cost of Hot Cargo code on every machine, because co-op lockstep waits for the slowest client.
Spec: `spec/42-performance.md`, requirements 870 to 889.

## Decisions
- Measure with `selfprof` (Telemetry/SelfProfile.cs): it times every Hot Cargo entry point, split into sim tick time and frame time outside ticks.
- Baseline from worktree `perf-base` (commit f2759b1), so edits in `perf` do not change the baseline.
- Headless slots draw nothing: draw-only hooks (overlays, `GetNonMouseoverText`, `OnBeforeDraw`) show no time there. Fix them by review: refresh text at a few Hz, cache strings, no LINQ or allocations per frame.
- Behavior stays identical; UI refresh rates are tunables.

## Steps
- [x] 1. 3-player co-op harness (`PLAYERS=3`, `mp3:<name>`) and `selfprof`.
- [ ] 2. Per-tick trait cache for personalities (spec 870).
- [ ] 3. Baseline numbers: `telemetry-stress`, `mp3:telemetry-stress`.
- [ ] 4. Frame code: throttle and cache UI text, markers, panels, map labels.
- [ ] 5. Sim hot spots from the profile.
- [ ] 6. Network volume: presence and slider inputs.
- [ ] 7. 3-player runs: core, save, trade, events, sharedchoice, diplomacy, factions, ai, timer, win. Fix desyncs.
- [ ] 8. After numbers, host settings for a slow client, spec, merge origin/main, delete this file.

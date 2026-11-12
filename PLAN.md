# Ship AI 2: plan

Branch `feat/shipai2`. Spec `spec/30-player-ship-tactics.md` (patch 400 to 412, new 413 to 439).

1. [ ] Model: condition chains (terms with own parameter, NOT, AND/OR; AND binds tighter), text superset of `HC1:`, hysteresis. Unit tests.
2. [ ] Hot Cargo conditions: heat stage, hunters in system, Strike incoming, tech loss timer, tech crates within R, enemy holds our tech, enemy calls for help.
3. [ ] Hot Cargo actions: collect tech crates, target the tech holder, target the caller (and a disruptor/EMP fire mode).
4. [ ] Facing action (nose, broadside left/right, stern).
5. [ ] Editor: "+ condition", remove per term, NOT and AND/OR toggles, scroll.
6. [ ] Fleet tools: apply to selected ships; personal presets (local file).
7. [ ] Tunables, strings, spec 413 to 439.
8. [ ] Autotests (shipai-*, co-op shipai), screenshots, merge origin/main, PR.

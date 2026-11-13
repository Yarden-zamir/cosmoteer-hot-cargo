# Ship AI 2: plan

Branch `feat/shipai2`. Spec `spec/30-player-ship-tactics.md` (400 to 412 patched, new 413 to 422; 423 to 439 stay free).
Paused for the feature freeze. Resume from this list.

## Done
1. [x] Model: condition chains (own parameter, NOT, AND/OR; AND binds tighter), text superset of `HC1:`, hysteresis, preset file format. Unit tests pass (64 ship AI cases).
2. [x] Hot Cargo conditions: heat stage, hunters in system, Strike wave within S (new `Run.SecondsToStrikeWave`, Core), loss timer, crates within R, enemy holds our tech, enemy calls for help.
3. [x] Hot Cargo actions: collect tech crates (vanilla `ManualResourceTransfer`), target tech holder, target caller, fire disruptors (tunable part and ammo lists).
4. [x] Facing action (nose, left, right, stern) through the attack order rotation options.
5. [x] Editor: term lines, "+ cond", remove, NOT and AND/OR toggles, scrolling window.
6. [x] Fleet tools: apply to selected; personal presets in `hot_cargo_tactics_presets.txt` next to the game settings.
7. [x] Tunables (`HysteresisMargin`, `MinHoldSeconds`, `CollectDistance`, `DisruptorParts`, `DisruptorAmmo`), strings, spec.
8. [x] Merged origin/main at d00e1fe.
9. [x] Autotest `shipai-hotcargo` passes.

## Left
10. [ ] Run `shipai-editor` and `shipai-calls` (written, never ran because of slot and link failures).
11. [ ] Rerun `shipai-basic`, `shipai-save`, co-op `mp:shipai` against the new code.
12. [x] Extend the co-op `shipai` test with a term edit (apply to selected stays in the single-player editor test: co-op tests have one ship per player).
13. [ ] Screenshots: `tests/screens/shipai.txt` (main slot, under the lock); add them to the PR with `gh-pr-images`.
14. [ ] Check in a game that +90° turns the left side to the target (spec 415).
15. [ ] Merge origin/main again, delete PLAN.md, final push.

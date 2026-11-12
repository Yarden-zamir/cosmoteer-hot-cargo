# Faster dev loop (feat/devloop)

Baseline from 153 logs in `../.state/logs/*/` (median per run, slot wait not included):
deploy + launch 23 s, game load to first script line 7 s, script 46 s, end detection 7 s. One PC call costs about 1 s.

## Steps
1. Deploy cache: hash the staged mod on the Mac. The PC keeps one unpacked copy per hash in `HotCargoSlots\_builds\<hash>`. A deploy with a known hash skips the upload and copies the cached build into the slot. Stop, deploy, script install and start go in one PC call.
2. One stage per suite: `autotest-all.sh` builds and stages once and exports `HOT_CARGO_STAGE`; `deploy.sh` uses it as is.
3. Phase times: `autotest.sh` prints `== timing` (wait, deploy, start, script, teardown, total). `autotest-all.sh` prints totals and medians per phase.
4. Polling: one small status call every 3 s (running, started, done). Full log once at the end.
5. Keep the game running between tests: check if safe; skip if not.
6. `--detach` for `autotest-all.sh` and `integrate.sh` (own session, output in `../.state/suites/`), `scripts/suite-wait.sh <log>`.
7. Batch merges in `integrate.sh`: several branches, one suite, failure names the branch per area.
8. Docs: testing skill, `CONTRIBUTING.md`. Before and after numbers (10+ tests each).

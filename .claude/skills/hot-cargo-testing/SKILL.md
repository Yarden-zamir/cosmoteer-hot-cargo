---
name: hot-cargo-testing
description: Build Hot Cargo, run unit tests, game tests and co-op tests (in the headless sim on this Mac, in CI, and on the Windows PC), and tell infrastructure failures from game failures. Use before every commit that changes code or rules, and when a test fails.
---

# Testing Hot Cargo

## Build and unit tests
1. Build: `dotnet build src/HotCargo/HotCargo.csproj -c Release -nologo -v quiet`.
2. Unit tests (pure math only, no game types): `cd tests/HotCargo.Tests && dotnet run`. Pure math goes in a file named `*Model.cs` with no game types; the test project picks up every such file.

## Headless game tests
- Where tests run: CI runs every game test in the headless sim before a merge (see "Before a merge"). On this Mac, run them in the headless sim (`scripts/autotest-sim.sh`, `tools/headless-sim`, `docs/headless-sim.md`). The PC runs the tests with `# runner: pc`, screenshots, and checks against the real game. Manual PC work: `hot-cargo-pc`.
- One test on the PC: `scripts/autotest.sh tests/autotest/<name>.txt [timeoutSeconds]`. It takes a free headless slot on the PC (t1..t6, own save, settings and cache folders, no window), deploys your worktree's build and runs the script. Exit 0 means pass.
  - The PC caches each build by a hash of its content. A build that the PC already has is not uploaded again.
  - It prints a `== timing` line: seconds for wait (slot), deploy, start (to the first script line), script and teardown.
  - Exit code 3 ("AUTOTEST LINK REFUSED"): the PC refused the calls, because your turn was not active. Rerun it with the turn open.
  - It prints only the lines that explain a failure. The full log stays in `../.state/logs/<worktree>/` (it survives restarts), and its path is in the output.
  - A failed run is retried once. "AUTOTEST FLAKY" means the retry passed. Fix a flaky test with `waitfor <condition>` in place of a fixed `wait`. `../.state/flaky.log` lists flaky runs.
- Several tests: `scripts/autotest-all.sh 400 <name> <name> mp:<coop name>`. They run in parallel on the free slots, also from one worktree. The suite builds once, and every test runs that build. At the end it prints the seconds per phase (sum, median, max).
- Long suites that use the PC: start the script itself as a Bash `run_in_background` command (for example `scripts/autotest-all.sh 400 <names> > <log> 2>&1`) and call `t3_background_wait` with its task ID. PC calls work only while your turn is active. Never end your turn while a PC run is going. A suite whose PC calls are refused stops starting tests and fails.
- `--detach` (`autotest-all.sh`, `autotest-affected.sh`, `integrate.sh`) runs a suite in its own session, and a killed shell does not stop it. It prints a log path in `../.state/suites/`; `scripts/suite-wait.sh <log>` waits for it and exits with the suite's code (124: still running). With the current T3, a `--detach` run started from a short Bash call loses PC access when that call ends. Use `--detach` only for runs that do not need the PC (for example headless sim runs).
- headless sim (no PC): `scripts/autotest-sim.sh 400 <name> <name>` runs tests on this machine in parallel (co-op sessions too: `mp:coop-core`), same pass rule, logs in `../.state/logs/<worktree>/*.sim.log`. One retry, like the PC runner (a pass on the retry is FLAKY). It needs the game files from `scripts/pull-game.sh`. Screenshots and real-time single-player checks need the PC; a test or session with the line `# runner: pc` stays there. A Mac-only failure can be a sim difference: compare with `scripts/autotest.sh` on the PC.
- The tests that your change touches: `scripts/autotest-affected.sh` (base `origin/main`). It runs tests in the headless sim on this machine, co-op sessions included, and tests with `# runner: pc` on the PC at the same time (`AUTOTEST_SIM=0`: all on the PC; `RUNNER=pc` or `RUNNER=sim` keeps one kind). A test belongs to an area when its name starts with the area folder name in lowercase (`tech-sell` for `src/HotCargo/Tech`). Name new tests that way. It runs the smoke tier (`tests/TIERS.md`): each touched area runs its smoke test (the line `# tier: smoke`) and all its co-op tests; a change in Core or shared UI runs every smoke test. `AUTOTEST_TIER=full` runs every test of the touched areas. `LIST_ONLY=1` prints the list.
- A new logic rule goes in a `*Model.cs` file with a unit test. A game test checks only what needs the game (`tests/TIERS.md`).
- All tests: `scripts/autotest-all.sh 400`. A full clean pass on a clean tree writes `../.state/autotest_pass_<sha>`.
- Find the commit that broke a test: `scripts/bisect-autotest.sh <name> <good commit> [bad commit]`.
- While the player's game runs, tests use at most 4 slots (`HOT_CARGO_SLOTS_WHILE_PLAYING`; shared settings in `../.state/slots.env`), so the player keeps a smooth frame rate.
- Long runs: a background Bash command and `t3_background_wait` (above). Do not poll in a tight loop.
- Game log of a slot: `SLOT=tN scripts/logs.sh`. A rules error usually shows there at load. Failed runs also save the vanilla log in their log file.
- Script commands live in `src/HotCargo/Dev/Autotest.cs` (base) and each area's `Register()`. Useful: `newgame run`, `wait s` (game seconds while a run ticks), `waitreal s`, `waitfor field op value seconds`, `heat n`, `expect field op value`, `save name`, `load name`, `godmode on`, `ev_set Enabled false` (turns random events off), `ev_pause on` (an event window pauses the game; autotests do not pause by default).
- Invariants (spec 44) run in every test: a broken rule logs `ERROR invariant <name>: <details>` and fails the test. Read the rule in `src/HotCargo/Dev/Invariants.cs` before you change it. A rule that a correct game breaks is a wrong rule; otherwise the bug needs a fix. The `Invariants.Enabled` tunable turns them off.
- `core-playthrough` plays to sector 2 without cheats (only `pt_*` commands that stand in for one click). It runs in the full suite only. A script asks for a longer timeout with a `# timeout: <seconds>` line.
- A test reads what the game shows, not our own value source. For UI, check the vanilla widget's text or state (for example the tooltip text the game stores), and take a screenshot when the result is visual.

## Before a merge (CI)
- CI (`.github/workflows/check.yml`) runs game tests and co-op sessions in the headless sim on Linux, in up to 6 jobs of 4 sims each (`scripts/ci-sim-tests.sh`): the set of the PRs for each merge queue group, every test for each manual and scheduled run. The check `game-tests` passes when every shard passed. The game tests run in the merge queue, not on PRs: a PR run passes `game-tests` without them. Before you push, choose the tests that your change needs and run them yourself (`scripts/autotest-sim.sh <names>`, `scripts/autotest-affected.sh`). The queue runs the test set of the group's PRs (`scripts/ci-test-set.sh`). A PR's default set: the affected tests (`scripts/autotest-affected.sh`) and the baseline `core` and `tech-start`; no tests when it changes only docs or scripts that the tests do not run; every test when it changes `check.yml`, the sim host or a script that the tests run (`scripts/ci-change-kind.sh`). A `### Game tests` block in the PR text changes the set, one line each: `+ <test>`, `- <test>`, `all`, or `none: <reason>`. The PR run checks the block and shows the set in its summary; an unknown test fails it. A full suite runs on `main` at most 3 hours after a merge (`full-suite-trigger.yml`, and the `check.yml` schedule as a second path), and on request (`gh workflow run check.yml --ref <branch>`). It reports the `full-suite` check; a release needs it green. Each shard's job summary lists its results; the logs are an artifact (`scripts/ci-check-logs.sh` lets only text logs in).
- Not in CI: tests with the line `# runner: pc`. That one marker is the only skip of a test. `LIST_ONLY=1 RUNNER=pc scripts/autotest-affected.sh` lists the affected ones; the `game-tests` job summary of a PR lists them too. The owner runs only those the merge depends on, on the PC, and ticks the box of the PR template.
- Then `gh pr merge --auto --merge` (CLAUDE.md, Git). The merge queue tests the PR on the newest `main`. When `main` moves, merge it into the branch only for a conflict or a change that the PR must adapt to.
- Game files for CI: the private repo `cosmoteer-hot-cargo-game-refs` (`Bin/`, `Data/`, `StandardMods/example_mod/`). After a game update: `scripts/pull-game.sh`, then `scripts/refresh-game-refs.sh --push`.
- A release tags a tested commit: `scripts/release.sh <version>` takes the newest commit on `origin/main` whose required checks and `full-suite` check all passed (`--commit <sha>` picks one), and builds the zip at that commit. No merge freeze is needed.

## Co-op tests
- Co-op tests are sessions in `tests/autotest-mp/<session>.txt`. In the headless sim: `scripts/autotest-sim.sh 400 mp:coop-core` (one session: `scripts/autotest-sim-mp.sh tests/autotest-mp/coop-core.txt`). On the PC: `scripts/autotest-all.sh 400 mp:coop-core`, or every session with `scripts/autotest-mp-all.sh [timeout]`. Two headless instances play host and client over UDP on 127.0.0.1. The session file holds all choreography; the client follows (`client.txt`).
- The verdict comes from the two logs only: `scripts/coop-report.sh <host log> <client log>` (no PC calls, so it works for logs of every runner).
- Each `step <area>.<name>` reports on its own. The summary gives one reason: join failed, steps failed, desync, state differs, PC link. A state difference names the step and the `RunState` members that differ.
- `as 1 <command>` runs a command in the sim as the client player. `on 1 <command>` runs it on the client's machine (its UI, its real inputs). Details: CONTRIBUTING.md, Co-op tests.
- On the Mac both processes keep the real-time clock (`SIM_REALTIME=1`): network requests and lockstep read the game clock. The headless sim can run slower than real time when the Mac is busy, so give waits that depend on crew or flight time some margin.
- A new co-op check is a new step in the session that fits, not a new test. Keep each session under about 6 minutes.
- 3 players: `scripts/autotest-all.sh 400 mp3:<session>` (or `PLAYERS=3 scripts/autotest-mp.sh ...`). It needs 3 slots, so run one at a time. Every client follows; `on 1` and `as 1` address the first client. `mpstart {PLAYERS}` starts with the player count, and a line that starts with `@3p ` or `@2p ` runs only with that count.
- Frame and tick cost of Hot Cargo code: `selfprof on|reset|report <label>` (test `telemetry-stress`, session `coop-stress`). Headless slots draw nothing, so `frameprof on|report <label>` calls the draw-time hooks once per frame and logs time and bytes per frame.

## Infrastructure failures
- After a change to the slot locks in `scripts/autotest-lib.sh`, run `scripts/test-slot-locks.sh`: dummy takers on this Mac, in their own lock folder (no PC, no game).
These are not game failures. Rerun the test:
- "relay was lost", "no signed connection to kitshn", "kitshn refused the run", "tar: Write error", "the PC did not confirm the deploy", "not enough free memory to start slot" (the PC is full; it waited 120 s).
- An empty test log after the deploy line.
A game failure shows `CHECK FAILED`, `ERROR` or `MP DESYNC` in the log.

## Rules
- The PC is shared with other agents and with a person. Use only the scripts. They never take focus.
- Do not change PC settings, files or saves except through the scripts.

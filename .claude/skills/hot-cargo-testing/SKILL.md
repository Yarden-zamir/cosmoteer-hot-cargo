---
name: hot-cargo-testing
description: Build Hot Cargo, run unit tests, headless game tests and co-op tests on the Windows PC, and tell infrastructure failures from game failures. Use before every commit that changes code or rules, and when a test fails.
---

# Testing Hot Cargo

## Build and unit tests
1. Build: `dotnet build src/HotCargo/HotCargo.csproj -c Release -nologo -v quiet`.
2. Unit tests (pure math only, no game types): `cd tests/HotCargo.Tests && dotnet run`. Pure math goes in a file named `*Model.cs` with no game types; the test project picks up every such file.

## Headless game tests
- One test: `scripts/autotest.sh tests/autotest/<name>.txt [timeoutSeconds]`. It takes a free headless slot on the PC (t1..t8, own save, settings and cache folders, no window), deploys your worktree's build and runs the script. Exit 0 means pass.
  - It prints only the lines that explain a failure. The full log stays in `/tmp/hot_cargo_logs/<worktree>/`, and its path is in the output.
  - A failed run is retried once. "AUTOTEST FLAKY" means the retry passed. Fix a flaky test with `waitfor <condition>` in place of a fixed `wait`. `/tmp/hot_cargo_flaky.log` lists flaky runs.
- Several tests: `scripts/autotest-all.sh 400 <name> <name> mp:<coop name>`. They run in parallel on the free slots, also from one worktree.
- The tests that your change touches: `scripts/autotest-affected.sh` (base `origin/main`). A test belongs to an area when its name starts with the area folder name in lowercase (`tech-sell` for `src/HotCargo/Tech`). Name new tests that way. A change in Core, shared UI or scripts runs the full suite. `LIST_ONLY=1` prints the list.
- All tests: `scripts/autotest-all.sh 400`. A full clean pass on a clean tree writes `/tmp/hot_cargo_autotest_pass_<sha>`, which `scripts/release.sh` needs.
- Find the commit that broke a test: `scripts/bisect-autotest.sh <name> <good commit> [bad commit]`.
- While the player's game runs, tests use at most 3 slots (`HOT_CARGO_SLOTS_WHILE_PLAYING`), so the player keeps a smooth frame rate.
- Long runs: start them with Bash `run_in_background` and wait for the completion notice. Do not poll in a tight loop.
- Game log of a slot: `SLOT=tN scripts/logs.sh`. A rules error usually shows there at load. Failed runs also save the vanilla log in their log file.
- Script commands live in `src/HotCargo/Dev/Autotest.cs` (base) and each area's `Register()`. Useful: `newgame run`, `wait s`, `waitfor field op value seconds`, `heat n`, `expect field op value`, `save name`, `load name`, `godmode on`, `ev_set Enabled false` (turns random events off).
- A test reads what the game shows, not our own value source. For UI, check the vanilla widget's text or state (for example the tooltip text the game stores), and take a screenshot when the result is visual.

## Co-op tests
- `scripts/autotest-mp.sh tests/autotest-mp/<name>.host.txt tests/autotest-mp/<name>.client.txt`. Two headless instances play host and client over LAN. Any desync fails the test. `mp <command>` runs a test command in the sim on both sides.
- 3 players: `PLAYERS=3 scripts/autotest-mp.sh ...` or `scripts/autotest-all.sh 400 mp3:<name>`. Every client runs the client script. `{PLAYERS}` becomes the player count (`mpstart {PLAYERS}`).
- Line filters in co-op scripts: a line that starts with `@3p ` runs only with 3 players, `@c1 ` only on the first client.
- Frame and tick cost of Hot Cargo code: `selfprof on|reset|report <label>` (test `telemetry-stress`, `mp3:telemetry-stress`). Headless slots draw nothing, so draw hooks show no time there.

## Infrastructure failures
These are not game failures. Rerun the test:
- "relay was lost", "no signed connection to kitshn", "no longer owns an active thread run", "kitshn refused the run", "tar: Write error".
- An empty test log after the deploy line.
A game failure shows `CHECK FAILED`, `ERROR` or `MP DESYNC` in the log.

## Rules
- The PC is shared with other agents and with a person. Use only the scripts. They never take focus.
- Do not change PC settings, files or saves except through the scripts.

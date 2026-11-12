---
name: hot-cargo-testing
description: Build Hot Cargo, run unit tests, headless game tests and co-op tests on the Windows PC, and tell infrastructure failures from game failures. Use before every commit that changes code or rules, and when a test fails.
---

# Testing Hot Cargo

## Build and unit tests
1. Build: `dotnet build src/HotCargo/HotCargo.csproj -c Release -nologo -v quiet`.
2. Unit tests (pure math only, no game types): `cd tests/HotCargo.Tests && dotnet run`. Add a pure model file to the csproj `Compile Include` list when you add pure math.

## Headless game tests
- One test: `scripts/autotest.sh tests/autotest/<name>.txt [timeoutSeconds]`. It takes a free headless slot on the PC (t1..t8, own save, settings and cache folders, no window), deploys your worktree's build, runs the script and prints the Hot Cargo log. Exit 0 means pass.
- All tests: `scripts/autotest-all.sh 400`. It runs every single-player test, then every co-op test. A full clean pass on a clean tree writes `/tmp/hot_cargo_autotest_pass_<sha>`, which `scripts/release.sh` needs.
- Run one autotest at a time per worktree.
- Long runs: start them with Bash `run_in_background` and wait for the completion notice. Do not poll in a tight loop.
- Game log of a slot: `SLOT=tN scripts/logs.sh`. A rules error usually shows there at load.
- Script commands live in `src/HotCargo/Dev/Autotest.cs` (base) and each area's `Register()`. Useful: `newgame run`, `wait s`, `heat n`, `expect field op value`, `save name`, `load name`, `godmode on`, `ev_set Enabled false` (turns random events off).

## Co-op tests
- `scripts/autotest-mp.sh tests/autotest-mp/<name>.host.txt tests/autotest-mp/<name>.client.txt`. Two headless instances play host and client over LAN. Any desync fails the test. `mp <command>` runs a test command in the sim on both sides.

## Infrastructure failures
These are not game failures. Rerun the test:
- "relay was lost", "no signed connection to kitshn", "no longer owns an active thread run", "kitshn refused the run", "tar: Write error".
- An empty test log after the deploy line.
A game failure shows `CHECK FAILED`, `ERROR` or `MP DESYNC` in the log.

## Rules
- The PC is shared with other agents and with a person. Use only the scripts. They never take focus.
- Do not change PC settings, files or saves except through the scripts.

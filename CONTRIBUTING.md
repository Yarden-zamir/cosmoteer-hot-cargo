# Working on Hot Cargo

Read SPEC.md and the section files in `spec/` first. They hold the design, the numbered requirements and the plan.

## Layout

- `src/HotCargo/Core`, `Ui`, `Dev`: shared core. Change it only when an area cannot work through the hooks in `Core/Run.cs` (`Run.Hooks`), `Core/DebugKeys.cs` (`DebugKeys.Extra`) and `Dev/Autotest.cs` (`Autotest.Commands`, `Autotest.Fields`).
- `src/HotCargo/<Area>`: one folder for each area. Each area has `<Area>Area.Register()`, its tunables section class and its patches. Ship comms options go through `Ui/CommsHail.cs` (one provider per area); dialog sliders use `Ui/SliderRow.cs`, option rows `Ui/OptionRows.cs`, and multiselect pages `MultiChoice/` (register an `IMultiChoiceDomain`).
- Shared helpers in Core: `Errors.Guard` / `Errors.Report` for any failure in a patch, sim tick or UI frame (log once, show in the overlay, never crash the game), and `SimQueries` for tagged ships, human ships and sim lookups. Use them before you write a new one.
- `mod/mod.header.rules`: the mod header. `scripts/assemble-mod.sh` builds `mod.rules` from it and from every `mod/<area>/actions.rules`.
- `mod/<area>/actions.rules`: only Action objects `{ ... }`, without an outer list.
- `mod/<area>/strings.en.rules`: string sections for the area. Two areas must not define the same top-level section.
- `mod/tunables.rules`: changed values only. The mod writes `tunables.defaults.rules` with every value at game start.
- New tunables show in the in-game settings menu by themselves (`src/HotCargo/TunablesMenu`). Give each one a `/// <summary>` comment: the menu shows it as the description. If a change does not take effect within a second, add the field to `TunableTiming.cs` and to TUNABLES.md.

## How to Play pages and the run log

Each player-facing mechanic gets a How to Play page (spec 720 to 726) and writes its events to the run log (730 to 734). Add both in the same change as the mechanic.

1. Write the page in `mod/<area>/codex.rules` as a `CodexPages` list. Copy a page from `mod/onboarding/codex.rules`. The ID starts with `hc_`. Use `TabNameKey = "HotCargoCodex/Tab"`. Start the title with "Hot Cargo:".
2. Add the list before the vanilla pages in `mod/<area>/actions.rules`, for example for an area `diplomacy`: `{ Action = AddBase; AddBaseTo = "<codex/codex.rules>/CodexPages"; BaseToAdd = &<diplomacy/codex.rules>/CodexPages; Index = 0 }`.
3. Put the page strings in the area's own strings section. Keep each entry to one or two short sentences. Name no numbers that a tunable sets.
4. In `<Area>Area.Register`, add the trigger: `Onboarding.Hints.Add("hc_<name>", run => <condition>)`. The condition runs in UI frames and only reads state. Pick the moment the player first meets the mechanic.
5. Write run log lines from sim logic with `Onboarding.RunLog.Add(run, kind, xml)`. Escape names with `StringTools.EscapeForXml`. Add a `RunLogKind` value only when no kind fits.
6. Test: add the trigger to `tests/autotest/onboarding-hints.txt` (`expect hint_hc_<name> == 1`) and the line to `tests/autotest/onboarding-log.txt`. Add the row to the page table in spec requirement 721 (`spec/33-how-to-play-and-the-run-log.md`).

The mod checks at the first frame of a run that each trigger has a page and each `hc_` page has a trigger, and shows a mismatch in the overlay.

## Build and test

1. Create `scripts/pc.local.sh` with `PC_MACHINE="<T3 name of the PC>"` (see the top of `scripts/pc.sh`). Then run `scripts/pull-game.sh` once. It copies the game DLLs and rules from the PC to `../.game` and decompiles them to `../.game/decompiled`.
2. Build: `dotnet build src/HotCargo/HotCargo.csproj -c Release`.
3. Unit tests (pure math only): `cd tests/HotCargo.Tests && dotnet run`.
4. Game test on the PC: `scripts/autotest.sh tests/autotest/<name>.txt`. It deploys, turns the mod on, runs the game headless in a test slot and prints the result and a `== timing` line (seconds per phase). Commands are in `src/HotCargo/Dev/Autotest.cs`.
5. Other PC scripts: `deploy.sh`, `run.sh`, `stop.sh`, `logs.sh`, `mod-toggle.sh on|off`. `stage.sh <dir>` builds and stages the mod with a content hash. The PC keeps the 30 newest builds in `%USERPROFILE%\HotCargoSlots\_builds\<hash>`, so `deploy.sh` uploads a build only once. The PC side of a deploy is `scripts/slot-install.ps1`; it ships inside the build, because the T3 link allows only 8192 characters for a command.
6. Suites: `scripts/autotest-all.sh [--detach] [timeout] [tests...]` and `scripts/autotest-affected.sh [--detach]`. A suite stages once, and all its tests run that build. `--detach` runs it safe from a killed shell; `scripts/suite-wait.sh <log>` waits for it.
7. Telemetry: every game writes a JSONL file per session (TELEMETRY.md). `scripts/pull-telemetry.sh` copies the files to `../.telemetry`, and `cd tools/telemetry && uv run hc-telemetry` writes a report. `scripts/autotest-all.sh --telemetry` does both after a test pass.

### Co-op (multiplayer) tests

Run a co-op test with `scripts/autotest-mp.sh tests/autotest-mp/<name>.host.txt tests/autotest-mp/<name>.client.txt [timeout]`. Run them all with `scripts/autotest-mp-all.sh`.

- The script takes two test slots. One slot hosts a co-op career, and the other slot joins it over UDP on `127.0.0.1`. Both run headless with `--nosteam`, through the LAN lobby and game setup screens.
- `{PORT}` in a script becomes the host's port (35870 + host slot number).
- The test fails on an `AUTOTEST FAIL` or `ERROR` line in either log. A desync that the game detects writes an `MP DESYNC` error line, in tests and in normal play.
- Both machines log an `MPSTATE` line every 300 sim ticks. The script compares the lines of the ticks that both machines logged. This catches a divergence that the game's integrity hash does not cover.

Commands for co-op scripts (`src/HotCargo/Dev/MpAutotest.cs`):

- `mphost <port>` (host): host a career on the Hot Cargo map with starter ship 0. `mpstart [players]` (host): wait for the clients to be ready, then start.
- `mpjoin <address:port>` (client): join like Direct Connect. `mpready [shared]` (client): pick a ship if needed, say ready and wait for the game. With `shared`, the client joins the host's team.
- `mp <command>`: run a test command inside the sim on every machine. Use it for every test command that changes the sim (`heat`, `stage`, `godmode`, `tostation`, `buyer goto`, `hc_generate`, `event` and so on). A test command that changes the sim without `mp` causes a desync.
- Real player actions run without `mp`, from one machine: `choose`, `tech sell`, `buyer sell`, `pause`. They send the game's own MP inputs.
- `waitfor <field> <op> <value> [timeout]`: wait until a check holds. Use it to keep the two scripts in step. `mp mpmark <n>` with `waitfor mpmark >= <n>` is a barrier between host and client.
- `mplobby` (host): return all players to the game setup screen. `mpload <name>` (host): the next `mpstart` loads the save `HotCargoTest_<name>`.
- `mpresync` (host): do the resync that follows a desync (the host's game goes to every machine). Then `waitfor mpgames >= <n>`.
- `mpchoose <serial> <choice>`: send an event choice for any page, like a late second click.
- `tunablesfile <text>` (before `mphost`): give this slot a different `tunables.rules`.
- Fields: `desyncs`, `mpgames`, `ishost`, `mpplayers`, `mpconnected`, `mpsetup`, `localplayer`, `mpmark`, `heatperminute`.
- End each test with the client quitting first. The host waits with `waitfor mpconnected == 0`, then quits.

The game runs on a Windows PC that a person also uses. `autotest.sh` runs headless test instances (no window) in slots t1..t6, each with its own save folder `%USERPROFILE%\HotCargoSlots\<slot>`, so several tests run at the same time. Other scripts take `SLOT=<name>`; without it they act on the player's own game (slot main), which starts minimized.

## Git

- Feature work: push a feature branch and open a draft pull request. CI builds it and runs the unit tests and the private-data check. The game tests run on the PC: `scripts/integrate.sh <branch|PR>...` merges into `main` after they pass, so the PR shows as merged. Several branches merge as one batch with one test suite and one push; on a failure it names the branches whose areas own the failed tests. `--full` runs the full suite, `--detach` runs it in the background. Small fixes may go straight to `main`.
- Use conventional commits (`feat:`, `fix:`, `docs:`, `chore:`).
- Feature branches are local only. Merge them into `main`, build, run the tests, then push.

Parallel autotests work, also from one worktree: `deploy.sh` holds a build lock. `scripts/autotest-affected.sh` runs the smoke tests of the areas a change touches (`tests/TIERS.md`); name a test after its area folder (`tech-sell` for `src/HotCargo/Tech`).
- Never commit personal machine data: user home paths, machine names, Steam IDs. `scripts/check-private.sh` checks this, and CI runs it on every push. Machine settings go in `scripts/pc.local.sh` (git ignores it).

## Releases

1. Merge, push, and run `scripts/autotest-all.sh` on a clean tree. A full pass writes a marker for that commit.
2. Run `scripts/release.sh <version> [--prerelease]`. It builds `hot-cargo-<version>.zip` (mod, loader, `install.bat`, `uninstall.bat`, `INSTALL.md`), tags the commit and publishes a GitHub release.

Every push to `main` also publishes an untested dev prerelease (`v<last tag>-dev.<run>`) through `.github/workflows/dev-release.yml`. It builds, runs the unit tests and the private-data check. A newer push cancels a running build, so only the newest commit gets a release. CI compiles against the game DLLs in the private repo `Yarden-zamir/cosmoteer-hot-cargo-game-refs`, read with the deploy key in the `GAME_REFS_DEPLOY_KEY` secret. After a game update, push the new `Cosmoteer.dll` and `HalflingCore.dll` there.

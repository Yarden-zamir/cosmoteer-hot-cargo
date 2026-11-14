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

1. Create `scripts/pc.local.sh` with `PC_MACHINE="<T3 name of the PC>"` (see the top of `scripts/pc.sh`). Then run `scripts/pull-game.sh` once. It copies the game DLLs, rules, ship designs and name generator files from the PC to `../.game` and decompiles them to `../.game/decompiled`.
2. Build: `dotnet build src/HotCargo/HotCargo.csproj -c Release`.
3. Unit tests (pure math only): `cd tests/HotCargo.Tests && dotnet run`.
4. Game test on the PC: `scripts/autotest.sh tests/autotest/<name>.txt`. It deploys, turns the mod on, runs the game headless in a test slot and prints the result and a `== timing` line (seconds per phase). Commands are in `src/HotCargo/Dev/Autotest.cs`.
5. Other PC scripts: `deploy.sh`, `run.sh`, `stop.sh`, `logs.sh`, `mod-toggle.sh on|off`. `stage.sh <dir>` builds and stages the mod with a content hash. The PC keeps the 30 newest builds in `%USERPROFILE%\HotCargoSlots\_builds\<hash>`, so `deploy.sh` uploads a build only once. The PC side of a deploy is `scripts/slot-install.ps1`; it ships inside the build, because the T3 link allows only 8192 characters for a command.
6. Suites: `scripts/autotest-all.sh [--detach] [timeout] [tests...]` and `scripts/autotest-affected.sh [--detach]`. A suite stages once, and all its tests run that build. `--detach` runs it safe from a killed shell; `scripts/suite-wait.sh <log>` waits for it. A `--detach` run loses PC access when the Bash call that started it ends, so run PC suites as a background Bash command instead (see the `hot-cargo-testing` skill).
   - headless sim: `scripts/autotest-sim.sh [timeout] [tests...]` runs tests on this machine (macOS or Linux) in parallel, with no PC (`tools/headless-sim`, `docs/headless-sim.md`). CI runs it on Linux for every PR and every push to `main`. Co-op sessions (`mp:<session>`) run as two sim processes on 127.0.0.1 with the real-time clock (`scripts/autotest-sim-mp.sh`). `autotest-affected.sh` runs tests there, co-op included. A test with the line `# runner: pc` stays on the PC; `LIST_ONLY=1 RUNNER=pc scripts/autotest-affected.sh` lists the affected ones.
7. Telemetry: every game writes a JSONL file per session (TELEMETRY.md). `scripts/pull-telemetry.sh` copies the files to `../.telemetry`, and `cd tools/telemetry && uv run hc-telemetry` writes a report. `scripts/autotest-all.sh --telemetry` does both after a test pass.

### Co-op (multiplayer) tests

Co-op tests are sessions: `tests/autotest-mp/<session>.txt` (`coop-core`, `coop-economy`, `coop-combat`, `coop-shared`, where both humans share one player index, and `coop-stress`, a cost measure for spec 42). Run one with `scripts/autotest-all.sh 400 mp:<session>` or `scripts/autotest-mp.sh tests/autotest-mp/<session>.txt [timeout]`. Run them all with `scripts/autotest-mp-all.sh`.

- The script takes two test slots. One slot hosts a co-op career, and the other slot joins it over UDP on `127.0.0.1`. Both run headless with `--nosteam`, through the LAN lobby and game setup screens.
- The session file is the host's script and holds all choreography. The client runs `tests/autotest-mp/client.txt`: it joins, then follows the host. A `client: <command>` line in the session file runs on the client before it joins (local settings).
- `{PORT}` in a script becomes the host's port (35870 + host slot number).
- 3 players: `mp3:<session>` in `scripts/autotest-all.sh`, or `PLAYERS=3 scripts/autotest-mp.sh`. It takes 3 slots, and both clients follow. `{PLAYERS}` becomes the player count (`mpstart {PLAYERS}`). A line that starts with `@2p ` or `@3p ` runs only with that player count. `scripts/coop-report.sh` runs once for each client.
- `step <area>.<name>` starts a step. Each step logs its own result. A failure skips only the rest of its step, so one failure does not hide the others. The area is the lowercase area folder name: `scripts/autotest-affected.sh` runs every session with a step of a changed area.
- `end` closes the last step, sends the client away and quits.
- Both machines log `MPHASH` and `MPFIELDS` lines every 150 sim ticks: a hash of every saved `RunState` member, money per player, ship and part counts and the sim random state. A desync that the game detects writes an `MP DESYNC` error line, in tests and in normal play.
- `scripts/coop-report.sh <host log> <client log>` gives the verdict from the two logs only: one line per step, then `== result: PASS` or `== result: FAIL: <reasons>` (join failed, steps failed, desync, state differs, state hash gap, errors). On a state difference it names the first game and tick, the step and the members that differ.
- The join retries once inside the game. A whole session runs again only when nothing ran in the host.

Commands for co-op sessions (`src/HotCargo/Dev/MpAutotest.cs`):

- `mphost <port>` (host): host a career on the Hot Cargo map with starter ship 0. `mpstart [players]` (host): wait for the clients to be ready, then start.
- `mpjoin <address:port>` (client): join like Direct Connect. `follow` (client): say ready in every game setup, run the commands that the host sends, quit when the host ends.
- `mp <command>`: run a test command inside the sim on every machine, as the sender. Use it for every test command that changes the sim (`heat`, `stage`, `godmode`, `tostation`, `hc_generate`, `event` and so on). A test command that changes the sim without `mp` causes a desync.
- `as <player> <command>`: like `mp`, as that player index (0 the host, 1 the client). An input that the command sends runs at once as that player, after the game's `AllowMPInput` check. Use it for choices that read only sim state: `choose`, `vote`, `dip`, `dip_pick`, `dip_punish`, `spawnstage`, `tostation`, `buyer goto`.
- `mpshare` (client, as a `client:` line): the client joins the host's team in every game setup, so both humans share player index 0 and its ships.
- `on <player> <command>`: run a command on that player's own machine and wait until it reports back. `on shared <command>` runs it on the clients that share the host's player index (`mpshare`). A failure there fails the step. Use it for that machine's UI and view (`menu`, `hover`, `expectcomms`, `tactics_editor`, `expect` of local fields such as money gains) and for inputs that it sends over the network like a real click (`choose`, `tech sell`, `fa_buy`, `buyer sell`, `contract_accept`, `tactics`).
- Real player actions of the host run without `mp`: `choose`, `tech sell`, `vote`, `pause`.
- `waitfor <field> <op> <value> [timeout]`: wait until a check holds.
- `mplobby` (host): return all players to the game setup screen. `mpload <name>` (host): the next `mpstart` loads the save `HotCargoTest_<name>`. A loaded game starts paused: `pause off`. `mprestart` (host): a new run with the connection kept, for steps after a won or lost run.
- `mpresync` (host): do the resync that follows a desync (the host's game goes to every machine). Then `waitfor mpgames >= <n>`.
- `mpchoose <serial|marked> <choice>`: send an event choice for any page, like a late second click. `marked`: the page that was pending at the last `ev_mark`.
- `tunablesfile <text>` (before `mphost`): give this slot a different `tunables.rules`. Fields are separated by `;`.
- Fields: `desyncs`, `mpgames`, `ishost`, `mpplayers`, `mpconnected`, `mpsetup`, `localplayer`, `mpmark`, `heatperminute`.

The game runs on a Windows PC that a person also uses. `autotest.sh` runs headless test instances (no window) in slots t1..t6, each with its own save folder `%USERPROFILE%\HotCargoSlots\<slot>`, so several tests run at the same time. Other scripts take `SLOT=<name>`; without it they act on the player's own game (slot main), which starts minimized.

## Git

- Feature work: push a feature branch and open a pull request. CI builds it and runs the unit tests, the private-data check and the game tests in the headless sim (`game-tests`). The owner runs the affected tests outside CI that the merge depends on (`# runner: pc`; the PR template has the box), then turns on auto-merge (`gh pr merge --auto --merge`). `main` requires the checks and an up-to-date branch, so GitHub orders the merges. Small fixes go through a short PR too.
- Use conventional commits (`feat:`, `fix:`, `docs:`, `chore:`).

Parallel autotests work, also from one worktree: `deploy.sh` holds a build lock. `scripts/autotest-affected.sh` runs the smoke tests of the areas a change touches (`tests/TIERS.md`); name a test after its area folder (`tech-sell` for `src/HotCargo/Tech`).
- Never commit personal machine data: user home paths, machine names, Steam IDs. `scripts/check-private.sh` checks this, and CI runs it on every PR and every push to `main`. Machine settings go in `scripts/pc.local.sh` (git ignores it).

## Releases

1. Merge, push, and run `scripts/autotest-all.sh` on a clean tree. A full pass writes a marker for that commit.
2. Run `scripts/release.sh <version> [--prerelease]`. It builds `hot-cargo-<version>.zip` (mod, loader, `install.bat`, `uninstall.bat`, `INSTALL.md`), tags the commit and publishes a GitHub release.

Every push to `main` also publishes an untested dev prerelease (`v<last tag>-dev.<run>`) through `.github/workflows/dev-release.yml`. It builds, runs the unit tests and the private-data check. A newer push cancels a running build, so only the newest commit gets a release. CI compiles against the game DLLs in the private repo `Yarden-zamir/cosmoteer-hot-cargo-game-refs`, read with the deploy key in the `GAME_REFS_DEPLOY_KEY` secret. After a game update, push the new `Cosmoteer.dll` and `HalflingCore.dll` there.

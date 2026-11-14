---
name: hot-cargo-pc
description: Operate the Windows PC that runs Cosmoteer for Hot Cargo. Slots (main against test slots), deploy, start, stop, logs, mod on and off, the player hold, slot locks and detached runs. Use for manual PC work outside a test or screenshot script, and before you write code that finds or stops games on the PC.
---

# The PC

Every PC script sources `scripts/pc.sh`. Its header names the settings: `PC_MACHINE` (from `scripts/pc.local.sh`), `SLOT`, `PS_PATHS` and `Get-SlotProcess`. Tests: `hot-cargo-testing`. Screenshots: `hot-cargo-screenshots`.

## Slots

- `SLOT=t1` to `tN`: test games. Each runs headless with `--nosteam` and its own save folder `%USERPROFILE%\HotCargoSlots\<slot>`. `HOT_CARGO_SLOTS` in `../.state/slots.env` sets N.
- `SLOT=main`: the player's own game, save folder and Mods folder. An empty `SLOT` is main too. So always set `SLOT=tN`.

## The player's game (slot main)

- Never start or stop the game in slot main (CLAUDE.md, Agents).
- While `../.state/player-hold` exists, the scripts refuse slot main and windowed test slots (exit 3). Do not remove the file and do not work around it. `scripts/player-hold.sh status` shows it. Only the coordinator sets and clears it (`hot-cargo-coordinate`).
- Allowed in slot main: `scripts/install-player.sh` (`hot-cargo-install`). It copies files only while the game is closed. `scripts/pull-game.sh` and `scripts/pull-telemetry.sh` only read files.

## Scripts

- `scripts/with-slot.sh <command> [args]`: takes a free test slot lock, exports `SLOT`, runs the command, then stops the game and frees the slot. The scripts below take no lock, so run manual test slot work through it. Example: `scripts/with-slot.sh bash -c 'scripts/deploy.sh && scripts/run.sh 60'`.
- `scripts/stage.sh <dir>`: builds and stages the mod with a content hash. It makes no PC call.
- `scripts/deploy.sh`: stages (or uses `HOT_CARGO_STAGE`) and installs into the slot. The PC caches builds by hash, so it uploads a build only once. The PC side is `scripts/slot-install.ps1`. It ships inside the build, because the T3 link allows only 8192 characters for a command.
- `scripts/run.sh [seconds]`: starts the slot's game. With seconds, it waits and then prints the logs.
- `scripts/stop.sh`: closes the slot's game.
- `scripts/logs.sh [lines]`: the Hot Cargo log and the newest game log of the slot.
- `scripts/mod-toggle.sh on|off`: turns the mod on or off in the slot's settings. The game must be closed.
- `scripts/detach.sh <name> <command>` and `scripts/suite-wait.sh <log> [timeout]`: a run in its own session, with its log in `../.state/suites/`. A detached run loses PC access when the Bash call that started it ends. Use it only for runs without PC calls.

## Locks (on the Mac, in /tmp)

- `hot_cargo_slot_<n>.lock`: one for each test slot, with `owner` (the worktree) and `pid`. `take_slot` and `take_slots` in `scripts/autotest-lib.sh` take them. Co-op takers queue on `hot_cargo_slot_pair.lock`.
- `hot_cargo_main_slot.lock`: `install-player.sh` and `vault-saved-ships.sh` take it.
- `hot_cargo_build_<hash>.lock`: one build at a time in each worktree (`stage.sh`).
- A lock whose pid is gone is stale, and the next taker takes it over. Never remove a lock whose owner process lives.
- After a change to the lock code, run `scripts/test-slot-locks.sh`.

## Games on the PC

- Find a slot's game only with `Get-SlotProcess`. It finds a test slot's game by its `--rootsavepath` folder `HotCargoSlots\<slot>`, and the player's game as each game without that path. Never use a pid file or a process id: Windows reuses ids, and a stale id can name the player's game.
- Never kill Cosmoteer processes by hand. Use `SLOT=tN scripts/stop.sh`.
- Never print process command lines.
- PC calls work only while your turn is active (CLAUDE.md, Agents).

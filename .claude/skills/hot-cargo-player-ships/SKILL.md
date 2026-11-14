---
name: hot-cargo-player-ships
description: Build Tech Vaults into the player's saved ships (after a backup) or into the starter ships with scripts/vault-saved-ships.sh. Use when the player asks for vaults in their ship designs, or when the starter designs in mod/tech/starters need new vaults. Only the coordinator runs it, when the player says the PC is free.
---

# Vaults in ship designs

`scripts/vault-saved-ships.sh` builds the vaults with the game's own code (autotest command `vaultdesigns`, spec 1016 to 1018). Read its header for the options.

## Who and when

- The script starts and stops the game in slot main, the player's game. An agent never runs it (CLAUDE.md, Agents). Ask the coordinator.
- The coordinator runs it only when the player says that the PC is free. Clear the player hold first (`hot-cargo-coordinate`). With the hold on, the script refuses (exit 3).
- While the player's game is open, it changes nothing (exit 2).

## Run

1. Saved ships: `scripts/vault-saved-ships.sh`. It first copies `Saved Ships` to `Saved Ships.backup-<time>` in the save folder and checks the file count. Then it changes the designs in place.
2. From clean designs: `scripts/vault-saved-ships.sh --from "Saved Ships.backup-<time>"`. It reads the designs from that backup and writes them to `Saved Ships`.
3. Starter ships: `scripts/vault-saved-ships.sh --starters mod/tech/starters`. It reads the vanilla starters and copies the new designs to that folder. Commit them in a PR (`hot-cargo-pr-flow`).
4. Many designs: split the run with `--range FIRST,COUNT` (name order), so each call fits in one foreground Bash call. Add `--no-install` to every call but the last.
5. Read the output: the backup path, the `=== report` lines, and failures. The full log is `../.state/logs/<worktree>/vault-<mode>.log`.
6. At the end, the script installs `origin/main` into the player's game again (`scripts/install-player.sh`).

No script restores a backup. The backup folder holds the designs from before the run.

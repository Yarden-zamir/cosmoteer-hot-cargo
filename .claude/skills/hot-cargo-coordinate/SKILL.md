---
name: hot-cargo-coordinate
description: Coordinator work on Hot Cargo. Watch the agent worktrees, set and clear the player hold, and run work in the player's game slot. Use when you coordinate several Hot Cargo agents, or when an agent asks for main-slot work.
---

# Coordinate

## Watch the agents

- `scripts/agent-watch.sh` prints one line for each agent worktree, and the count of held test slots and the main-slot lock. Its header names the columns.
- A scheduled run calls `../.state/agent-watch.sh`, a wrapper that runs the script in `../main`.
- Signs that an agent needs a message:
  - No commit for a long time, with no running tests.
  - Commits that are not pushed (ahead).
  - Refused logs: the agent ran PC work without an active turn (CLAUDE.md, Agents).
  - Slots held with no running tests: a dead run. Its lock goes stale when its owner process ends.

## The player hold

- `scripts/player-hold.sh on` when the player plays or says the PC is theirs. `off` only when the player says the PC is free. `status` shows it.
- While the hold is on, the scripts refuse slot main and windowed test slots. Headless test slots stay allowed.

## Work in the player's game slot

Only the coordinator does this, and only while the hold is off:

- Vaults in ship designs: `hot-cargo-player-ships`.
- A new loader version, which needs every game on the PC closed once: `hot-cargo-game-update`.
- Installs need no hold check: `hot-cargo-install`.

## Main

- Agents merge their own PRs (`hot-cargo-pr-flow`). The GitHub merge queue of `main` orders the merges. No agent runs a merge queue of its own.
- `scripts/integrate.sh` stays for release and maintenance runs in the main worktree. Read its header first.
- Releases: `hot-cargo-release`.

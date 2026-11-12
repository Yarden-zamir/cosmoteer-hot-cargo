---
name: hot-cargo-install
description: Install the newest pushed Hot Cargo main into the player's own Cosmoteer on the Windows PC. Use after every push to main, when the player's game is closed.
---

# Install into the player's game

The player wants the newest pushed main installed at all times. Never touch a running game.

Run `scripts/install-player.sh`. It takes the main-slot lock, checks that the game is closed, installs `origin/main` from the detached `../play` worktree, turns the mod on and records the commit in `../.state/installed_sha`. When the game runs, it prints "game running" and installs nothing; try again later.

`scripts/keep-installed.sh ../play 115` does steps 2–5 in a loop. It only works while the calling session's turn is active, because background commands lose the link to the PC when the turn ends.

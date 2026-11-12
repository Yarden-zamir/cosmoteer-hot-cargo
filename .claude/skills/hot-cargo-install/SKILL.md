---
name: hot-cargo-install
description: Install the newest pushed Hot Cargo main into the player's own Cosmoteer on the Windows PC. Use after every push to main, when the player's game is closed.
---

# Install into the player's game

The player wants the newest pushed main installed at all times. Never touch a running game.

1. Take the slot lock (see `hot-cargo-screenshots`, step 1).
2. Check the game is closed (`Get-SlotProcess` prints nothing). If it runs, release the lock and install later.
3. In the detached `../play` worktree: `git fetch -q origin && git checkout -q --detach origin/main`.
4. `SLOT=main scripts/deploy.sh`, then `SLOT=main scripts/mod-toggle.sh on`.
5. Record the commit: `git rev-parse HEAD > /tmp/hot_cargo_installed_sha`.
6. Release the lock.

`scripts/keep-installed.sh ../play 115` does steps 2–5 in a loop. It only works while the calling session's turn is active, because background commands lose the link to the PC when the turn ends.
